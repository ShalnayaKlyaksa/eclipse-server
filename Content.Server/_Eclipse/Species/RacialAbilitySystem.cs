using System.Linq;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Temperature.Systems;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.Species;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.Temperature.Components;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Eclipse.Species;

[RegisterComponent]
public sealed partial class LavriteHeatComponent : Component;

[RegisterComponent]
public sealed partial class ShadowVeilComponent : Component;

[RegisterComponent]
public sealed partial class StickySpitProjectileComponent : Component;

public sealed partial class RacialAbilitySystem : EntitySystem
{
    private const float ReformHungerCost = 20f;
    private const float TailSweepRange = 1.6f;
    private const float TailSweepDamage = 28f;
    private const float TailSweepCone = 0.7071068f; // Cosine of the 45-degree half-angle.
    private const float PulseRange = 6f;
    private readonly Dictionary<EntityUid, TimeSpan> _pulseListeners = new();
    private TimeSpan _nextPulseSample;
    private static readonly EntProtoId ShadowVeil = "StatusEffectShadowVeil";
    private static readonly DamageSpecifier ReformDose = new() { DamageDict = { ["Blunt"] = FixedPoint2.New(-12) } };

    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private SharedAdvancedExertionSystem _exertion = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private MovementModStatusSystem _movement = default!;
    [Dependency] private SharedAdvancedMedicineSystem _medicine = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private SharedStealthSystem _stealth = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedGunSystem _guns = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<HumanoidProfileComponent, RacialInstantActionEvent>(OnInstant);
        SubscribeLocalEvent<HumanoidProfileComponent, RacialTargetActionEvent>(OnTarget);
        SubscribeLocalEvent<HumanoidProfileComponent, AvianThrowFeatherEvent>(OnFeather);
        SubscribeLocalEvent<HumanoidProfileComponent, SaurianTailSweepEvent>(OnTailSweep);
        SubscribeLocalEvent<StickySpitProjectileComponent, ProjectileHitEvent>(OnStickySpitHit);
        SubscribeLocalEvent<HumanoidProfileComponent, LavriteThermalBurstEvent>(OnLavriteThermalBurst);
        SubscribeLocalEvent<LavriteHeatComponent, AtmosExposedUpdateEvent>(OnLavriteHeat);
        SubscribeLocalEvent<ShadowVeilComponent, StatusEffectAppliedEvent>(OnVeilApplied);
        SubscribeLocalEvent<ShadowVeilComponent, StatusEffectRemovedEvent>(OnVeilRemoved);
    }

    private void OnInstant(Entity<HumanoidProfileComponent> ent, ref RacialInstantActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        args.Handled = args.Ability switch
        {
            RacialInstantAbility.DwarfBrace => _movement.TryUpdateMovementSpeedModDuration(user,
                "StatusEffectDwarfBrace", TimeSpan.FromSeconds(7), 0.65f),
            RacialInstantAbility.LavriteVent => VentHeat(user),
            RacialInstantAbility.CanineScent => Scent(user),
            RacialInstantAbility.ElirReadPulse => StartPulseListening(user),
            RacialInstantAbility.TherianScurry => _movement.TryUpdateMovementSpeedModDuration(user,
                "StatusEffectTherianScurry", TimeSpan.FromSeconds(5), 1.35f),
            RacialInstantAbility.ShadowVeil => !HasComp<StealthComponent>(user)
                && _status.TrySetStatusEffectDuration(user, ShadowVeil, TimeSpan.FromSeconds(6)),
            RacialInstantAbility.SlimeReform => Reform(user),
            _ => false,
        };
    }

    private void OnTarget(Entity<HumanoidProfileComponent> ent, ref RacialTargetActionEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        var target = args.Target;
        args.Handled = args.Ability switch
        {
            RacialTargetAbility.MoleiSilk => ApplySilk(user, target),
            RacialTargetAbility.SlimeAdhesive => SpitAdhesive(user, target),
            _ => false,
        };
    }

    private void OnLavriteHeat(Entity<LavriteHeatComponent> ent, ref AtmosExposedUpdateEvent args)
    {
        if (!_mobs.IsDead(ent))
            TransferToAir(ent, args.GasMixture, 220f, 323.15f);
    }

    private bool VentHeat(EntityUid user)
    {
        if (_atmos.GetContainingMixture(user, excite: true) is { } air && TransferToAir(user, air, 15000f, 321.15f))
        {
            Spawn("LavriteHeatPuff", Transform(user).Coordinates);
            Notify(user, "racial-lavrite-vent-success");
            return true;
        }

        Notify(user, "racial-lavrite-no-heat-sink");
        return false;
    }

    private bool TransferToAir(EntityUid user, GasMixture air, float maxEnergy, float minimumBodyTemperature)
    {
        if (air.Immutable || air.TotalMoles < 0.1f || !TryComp<TemperatureComponent>(user, out var body))
            return false;

        var bodyCapacity = _temperature.GetHeatCapacity(user, body);
        var airCapacity = _atmos.GetHeatCapacity(air, applyScaling: false);
        var difference = body.CurrentTemperature - air.Temperature;
        var energy = Math.Min(maxEnergy, Math.Min((body.CurrentTemperature - minimumBodyTemperature) * bodyCapacity,
            difference * bodyCapacity * airCapacity / (bodyCapacity + airCapacity)));
        if (energy <= 0 || !float.IsFinite(energy))
            return false;

        air.Temperature += energy / airCapacity;
        _temperature.ChangeHeat(user, -energy, ignoreHeatResistance: true, temperature: body);
        return true;
    }

    private void OnLavriteThermalBurst(Entity<HumanoidProfileComponent> ent, ref LavriteThermalBurstEvent args)
    {
        if (args.Handled)
            return;

        const float range = 2.5f;
        var user = args.Performer;
        var origin = _transform.GetMapCoordinates(user);
        var aim = _transform.ToMapCoordinates(args.Target);
        var delta = aim.Position - origin.Position;
        if (aim.MapId != origin.MapId || delta.LengthSquared() < 0.0001f)
            return;

        var direction = Vector2.Normalize(delta);
        Spawn("LavriteHeatPuff", Transform(user).Coordinates);

        foreach (var target in _lookup.GetEntitiesInRange(Transform(user).Coordinates, range, LookupFlags.Uncontained))
        {
            if (target == user || !HasComp<MobStateComponent>(target) || _mobs.IsDead(target))
                continue;

            var offset = _transform.GetWorldPosition(target) - origin.Position;
            var distance = offset.Length();
            if (distance > range || Vector2.Dot(direction, offset) <= distance * TailSweepCone
                || !_interaction.InRangeUnobstructed(user, target, range: range, overlapCheck: false))
                continue;

            _throwing.TryThrow(target, direction * 1.25f, baseThrowSpeed: 4f, recoil: false,
                animated: false, playSound: false, doSpin: false);
        }

        args.Handled = true;
    }

    private bool ApplySilk(EntityUid user, EntityUid target)
    {
        if (_mobs.IsDead(target) || !TryComp<AdvancedHealthComponent>(target, out var health))
            return false;

        if (!SealWound(health, 2f))
        {
            Notify(user, "racial-silk-no-wound");
            return false;
        }

        Dirty(target, health);
        Notify(user, "racial-silk-applied");
        return true;
    }

    private static bool SealWound(AdvancedHealthComponent health, float reduction)
    {
        var wound = health.BodyParts.Values.Where(part => !part.IsDestroyed && !part.IsSurgicalOpen)
            .SelectMany(part => part.Wounds)
            .Where(wound => !wound.IsSurgicalIncision && wound.ExternalBleedingRate > 0.01f)
            .MaxBy(wound => wound.ExternalBleedingRate);
        if (wound == null)
            return false;

        wound.ExternalBleedingRate = Math.Max(0f, wound.ExternalBleedingRate - reduction);
        wound.IsBandaged = true;
        return true;
    }

    private void OnTailSweep(Entity<HumanoidProfileComponent> ent, ref SaurianTailSweepEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        var origin = _transform.GetMapCoordinates(user);
        var aim = _transform.ToMapCoordinates(args.Target);
        var delta = aim.Position - origin.Position;
        if (aim.MapId != origin.MapId || delta.LengthSquared() < 0.0001f)
        {
            Notify(user, "racial-tail-sweep-miss");
            return;
        }

        var direction = Vector2.Normalize(delta);
        var candidates = _lookup.GetEntitiesInRange(Transform(user).Coordinates, TailSweepRange, LookupFlags.Uncontained)
            .Where(target => target != user && HasComp<MobStateComponent>(target) && !_mobs.IsDead(target)
                && (HasComp<AdvancedExertionComponent>(target) || HasComp<StaminaComponent>(target)))
            .Select(target => (Target: target, Offset: _transform.GetWorldPosition(target) - origin.Position))
            .Where(candidate => candidate.Offset.LengthSquared() <= TailSweepRange * TailSweepRange
                && Vector2.Dot(direction, candidate.Offset) > candidate.Offset.Length() * TailSweepCone)
            .OrderBy(candidate => candidate.Offset.LengthSquared());
        foreach (var (target, _) in candidates)
        {
            if (!_interaction.InRangeUnobstructed(user, target, range: TailSweepRange, overlapCheck: false))
                continue;

            if (TryComp<AdvancedExertionComponent>(target, out var exertion))
                args.Handled = _exertion.TryDrainReserve((target, exertion), TailSweepDamage);
            else if (TryComp<StaminaComponent>(target, out var stamina))
            {
                var before = stamina.StaminaDamage;
                _stamina.TakeStaminaDamage(target, TailSweepDamage, stamina, source: user);
                args.Handled = stamina.StaminaDamage > before;
            }

            if (args.Handled)
            {
                Notify(user, "racial-tail-sweep-hit");
                return;
            }
        }

        Notify(user, "racial-tail-sweep-miss");
    }

    private bool Scent(EntityUid user)
    {
        var count = _lookup.GetEntitiesInRange(Transform(user).Coordinates, 6f, LookupFlags.Uncontained)
            .Count(entity => entity != user && HasComp<MobStateComponent>(entity) && !_mobs.IsDead(entity));
        _popup.PopupEntity(Loc.GetString("racial-canine-scent-result", ("count", count)), user, user);
        return true;
    }

    private bool StartPulseListening(EntityUid user)
    {
        if (!_players.TryGetSessionByEntity(user, out var session))
            return false;

        _pulseListeners[user] = _timing.CurTime + TimeSpan.FromSeconds(5);
        var samples = CollectPulseSamples(user);
        if (samples.Length == 0)
            Notify(user, "racial-elir-pulse-empty");
        SendPulseSamples(session, samples);
        return true;
    }

    public override void Update(float frameTime)
    {
        if (_pulseListeners.Count == 0 || _timing.CurTime < _nextPulseSample)
            return;

        _nextPulseSample = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        foreach (var (user, expires) in _pulseListeners)
        {
            if (!_players.TryGetSessionByEntity(user, out var session))
            {
                _pulseListeners.Remove(user);
                continue;
            }

            if (_timing.CurTime >= expires || _mobs.IsDead(user))
            {
                _pulseListeners.Remove(user);
                SendPulseSamples(session, Array.Empty<ElirPulseSample>());
                continue;
            }

            SendPulseSamples(session, CollectPulseSamples(user));
        }
    }

    private void SendPulseSamples(ICommonSession session, ElirPulseSample[] samples)
    {
        RaiseNetworkEvent(new ElirPulseSamplesEvent(samples), Filter.SinglePlayer(session), recordReplay: false);
    }

    internal ElirPulseSample[] CollectPulseSamples(EntityUid user)
    {
        var origin = _transform.GetMapCoordinates(user);
        var samples = new List<ElirPulseSample>();
        foreach (var target in _lookup.GetEntitiesInRange(Transform(user).Coordinates, PulseRange, LookupFlags.Uncontained))
        {
            if (target == user || !TryComp<AdvancedHealthComponent>(target, out var health) || _mobs.IsDead(target)
                || health.IsHeartStopped || !float.IsFinite(health.HeartRate) || health.HeartRate <= 0f)
                continue;

            var position = _transform.GetMapCoordinates(target);
            if (position.MapId == origin.MapId && Vector2.DistanceSquared(position.Position, origin.Position) <= PulseRange * PulseRange)
                samples.Add(new ElirPulseSample(position, health.HeartRate));
        }

        return samples.ToArray();
    }

    private void OnVeilApplied(Entity<ShadowVeilComponent> ent, ref StatusEffectAppliedEvent args)
    {
        _stealth.SetVisibility(args.Target, -0.25f, EnsureComp<StealthComponent>(args.Target));
        EnsureComp<StealthOnMoveComponent>(args.Target);
    }

    private void OnVeilRemoved(Entity<ShadowVeilComponent> ent, ref StatusEffectRemovedEvent args)
    {
        RemComp<StealthOnMoveComponent>(args.Target);
        RemComp<StealthComponent>(args.Target);
    }

    private bool Reform(EntityUid user)
    {
        if (!TryComp<HungerComponent>(user, out var hunger) || !TryComp<AdvancedHealthComponent>(user, out var health)
            || _hunger.GetHunger(hunger) < ReformHungerCost)
        {
            Notify(user, "racial-slime-reform-hungry");
            return false;
        }

        var sealedWound = SealWound(health, 1.5f);
        if (!sealedWound && !health.BodyParts.Values.Any(part => SharedAdvancedMedicineSystem.CanRepairSoftTissue(part)
            && (part.SkinFraction < 1f || part.MuscleFraction < 1f)))
        {
            Notify(user, "racial-slime-reform-uninjured");
            return false;
        }

        _hunger.ModifyHunger(user, -ReformHungerCost, hunger);
        _medicine.ApplyDose((user, health), ReformDose, ignoreResistances: true);
        Notify(user, "racial-slime-reform-applied");
        return true;
    }

    private void OnFeather(Entity<HumanoidProfileComponent> ent, ref AvianThrowFeatherEvent args)
    {
        if (args.Handled)
            return;

        var feather = Spawn("AvianThrownFeather", Transform(args.Performer).Coordinates);
        _throwing.TryThrow(feather, args.Target, baseThrowSpeed: 13f, user: args.Performer, recoil: false, doSpin: false);
        if (!HasComp<ThrownItemComponent>(feather))
        {
            QueueDel(feather);
            return;
        }
        args.Handled = true;
    }

    private bool SpitAdhesive(EntityUid user, EntityUid target)
    {
        if (_mobs.IsDead(target))
            return false;

        var direction = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(user);
        if (direction.LengthSquared() < 0.0001f)
            return false;

        var glob = Spawn("SlimefolkStickySpit", Transform(user).Coordinates);
        _guns.ShootProjectile(glob, direction, Vector2.Zero, user, user, 10f);
        return true;
    }

    private void OnStickySpitHit(Entity<StickySpitProjectileComponent> ent, ref ProjectileHitEvent args)
    {
        if (!HasComp<MobStateComponent>(args.Target) || _mobs.IsDead(args.Target)
            || !_movement.TryUpdateMovementSpeedModDuration(args.Target, "StatusEffectSlimeAdhesive", TimeSpan.FromSeconds(5), 0.65f))
            return;

        if (args.Shooter is { } shooter)
            Notify(shooter, "racial-slime-spit-hit");
    }

    private void Notify(EntityUid user, string message) => _popup.PopupEntity(Loc.GetString(message), user, user);
}
