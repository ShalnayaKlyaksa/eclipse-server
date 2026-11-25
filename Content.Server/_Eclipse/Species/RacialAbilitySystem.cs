using System.Linq;
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
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.Temperature.Components;
using Content.Shared.Throwing;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.Species;

[RegisterComponent]
public sealed partial class LavriteHeatComponent : Component;

[RegisterComponent]
public sealed partial class ShadowVeilComponent : Component;

public sealed partial class RacialAbilitySystem : EntitySystem
{
    private const float ReformHungerCost = 20f;
    private static readonly EntProtoId ShadowVeil = "StatusEffectShadowVeil";
    private static readonly DamageSpecifier ReformDose = new() { DamageDict = { ["Blunt"] = FixedPoint2.New(-12) } };

    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private MovementModStatusSystem _movement = default!;
    [Dependency] private SharedAdvancedMedicineSystem _medicine = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStaminaSystem _stamina = default!;
    [Dependency] private SharedStealthSystem _stealth = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private ThrowingSystem _throwing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<HumanoidProfileComponent, RacialInstantActionEvent>(OnInstant);
        SubscribeLocalEvent<HumanoidProfileComponent, RacialTargetActionEvent>(OnTarget);
        SubscribeLocalEvent<HumanoidProfileComponent, AvianThrowFeatherEvent>(OnFeather);
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
            RacialTargetAbility.LavriteCoolTouch => CoolTouch(user, target),
            RacialTargetAbility.SaurianTailSweep => TailSweep(user, target),
            RacialTargetAbility.ElirReadPulse => ReadPulse(user, target),
            RacialTargetAbility.SlimeAdhesive => !_mobs.IsDead(target) && _movement.TryUpdateMovementSpeedModDuration(target,
                "StatusEffectSlimeAdhesive", TimeSpan.FromSeconds(5), 0.65f),
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
        if (_atmos.GetContainingMixture(user, excite: true) is { } air && TransferToAir(user, air, 15000f, 323.15f))
            return true;

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

    private bool CoolTouch(EntityUid user, EntityUid target)
    {
        if (!TryComp<TemperatureComponent>(user, out var body) || !TryComp<TemperatureComponent>(target, out var sink))
            return false;

        var bodyCapacity = _temperature.GetHeatCapacity(user, body);
        var sinkCapacity = _temperature.GetHeatCapacity(target, sink);
        var difference = body.CurrentTemperature - sink.CurrentTemperature;
        var energy = Math.Min(12000f, Math.Min((body.CurrentTemperature - 320.15f) * bodyCapacity,
            difference * bodyCapacity * sinkCapacity / (bodyCapacity + sinkCapacity)));
        if (energy <= 0 || !float.IsFinite(energy))
        {
            Notify(user, "racial-lavrite-no-heat-sink");
            return false;
        }

        _temperature.ChangeHeat(user, -energy, ignoreHeatResistance: true, temperature: body);
        _temperature.ChangeHeat(target, energy, ignoreHeatResistance: true, temperature: sink);
        return true;
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

    private bool TailSweep(EntityUid user, EntityUid target)
    {
        if (_mobs.IsDead(target) || !HasComp<StaminaComponent>(target))
            return false;

        _stamina.TakeStaminaDamage(target, 28f, source: user);
        return true;
    }

    private bool Scent(EntityUid user)
    {
        var count = _lookup.GetEntitiesInRange(Transform(user).Coordinates, 6f, LookupFlags.Uncontained)
            .Count(entity => entity != user && HasComp<MobStateComponent>(entity) && !_mobs.IsDead(entity));
        _popup.PopupEntity(Loc.GetString("racial-canine-scent-result", ("count", count)), user, user);
        return true;
    }

    private bool ReadPulse(EntityUid user, EntityUid target)
    {
        if (!TryComp<AdvancedHealthComponent>(target, out var health))
            return false;

        var pulse = _mobs.IsDead(target) ? 0 : (int) MathF.Round(health.HeartRate);
        _popup.PopupEntity(Loc.GetString("racial-elir-pulse-result", ("target", target), ("pulse", pulse)), user, user);
        return true;
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
        if (!sealedWound && !health.BodyParts.Values.Any(part => !part.IsDestroyed && !part.IsSurgicalOpen
            && (part.SkinFraction < 1f || part.MuscleFraction < 1f)))
            return false;

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
        args.Handled = true;
    }

    private void Notify(EntityUid user, string message) => _popup.PopupEntity(Loc.GetString(message), user, user);
}
