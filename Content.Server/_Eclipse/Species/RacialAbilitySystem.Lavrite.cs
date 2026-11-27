using System.Linq;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Temperature.Systems;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.Species;
using Content.Shared.Atmos;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Temperature.Components;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Server._Eclipse.Species;

[RegisterComponent]
public sealed partial class LavriteHeatComponent : Component
{
    public bool OverheatNotified;
}

public sealed partial class RacialAbilitySystem
{
    private const float LavriteNormalTemperature = 323.15f;
    private const float LavriteWorkHeatingLimit = LavriteNormalTemperature + 18f;
    private const float BurstRange = 5f;
    private const float BurstHalfAngle = MathF.PI / 4f;

    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private TemperatureSystem _temperature = default!;

    private void InitializeLavrite()
    {
        SubscribeLocalEvent<HumanoidProfileComponent, LavriteThermalBurstEvent>(OnLavriteThermalBurst);
        SubscribeLocalEvent<LavriteHeatComponent, AtmosExposedUpdateEvent>(OnLavriteHeat);
        SubscribeLocalEvent<LavriteHeatComponent, PhysicalExertionEvent>(OnLavriteExertion);
    }

    private void OnLavriteExertion(Entity<LavriteHeatComponent> ent, ref PhysicalExertionEvent args)
    {
        if (_mobs.IsDead(ent) || !TryComp<TemperatureComponent>(ent, out var body))
            return;

        var rise = Math.Min(args.EffortSeconds * 1.8f, LavriteWorkHeatingLimit - body.CurrentTemperature);
        if (rise <= 0f)
            return;

        _temperature.ChangeHeat(ent, rise * _temperature.GetHeatCapacity(ent, body), ignoreHeatResistance: true, temperature: body);
        if (!ent.Comp.OverheatNotified && body.CurrentTemperature >= LavriteNormalTemperature + 6f)
        {
            ent.Comp.OverheatNotified = true;
            Notify(ent, "racial-lavrite-exertion-heat");
        }
    }

    private void OnLavriteHeat(Entity<LavriteHeatComponent> ent, ref AtmosExposedUpdateEvent args)
    {
        if (_mobs.IsDead(ent) || !TryComp<TemperatureComponent>(ent, out var body))
            return;

        TransferToAir((ent, body), args.GasMixture, 220f);
        if (body.CurrentTemperature < LavriteNormalTemperature + 2f)
            ent.Comp.OverheatNotified = false;
    }

    private bool VentHeat(EntityUid user)
    {
        if (!HasComp<LavriteHeatComponent>(user) || _mobs.IsDead(user) || !TryComp<TemperatureComponent>(user, out var body))
            return false;

        if (body.CurrentTemperature < LavriteNormalTemperature + 0.5f)
        {
            Notify(user, "racial-lavrite-not-overheated");
            return false;
        }

        if (_atmos.GetContainingMixture(user, excite: true) is not { } air || !TransferToAir((user, body), air, 45_000f))
        {
            Notify(user, "racial-lavrite-no-heat-sink");
            return false;
        }

        SendHeatWave(user, _transform.GetMapCoordinates(user), 0f, MathF.PI, 0.9f);
        Notify(user, "racial-lavrite-vent-success");
        return true;
    }

    private bool TransferToAir(Entity<TemperatureComponent> body, GasMixture air, float maxEnergy)
    {
        if (air.Immutable || air.TotalMoles < 0.1f || body.Comp.CurrentTemperature <= LavriteNormalTemperature)
            return false;

        var bodyCapacity = _temperature.GetHeatCapacity(body, body.Comp);
        var airCapacity = _atmos.GetHeatCapacity(air, applyScaling: false);
        var difference = body.Comp.CurrentTemperature - air.Temperature;
        var energy = Math.Min(maxEnergy, Math.Min((body.Comp.CurrentTemperature - LavriteNormalTemperature) * bodyCapacity,
            difference * bodyCapacity * airCapacity / (bodyCapacity + airCapacity)));
        if (energy <= 0f || !float.IsFinite(energy))
            return false;

        air.Temperature += energy / airCapacity;
        _temperature.ChangeHeat(body, -energy, ignoreHeatResistance: true, temperature: body.Comp);
        return true;
    }

    private void OnLavriteThermalBurst(Entity<HumanoidProfileComponent> ent, ref LavriteThermalBurstEvent args)
    {
        if (args.Handled)
            return;

        var user = args.Performer;
        var origin = _transform.GetMapCoordinates(user);
        var aim = _transform.ToMapCoordinates(args.Target);
        var delta = aim.Position - origin.Position;
        if (aim.MapId != origin.MapId || delta.LengthSquared() < 0.0001f)
            return;

        var direction = Vector2.Normalize(delta);
        SendHeatWave(user, origin, MathF.Atan2(direction.Y, direction.X), BurstHalfAngle, BurstRange);

        foreach (var target in _lookup.GetEntitiesInRange(Transform(user).Coordinates, BurstRange, LookupFlags.Uncontained))
        {
            if (target == user || !HasComp<MobStateComponent>(target) || _mobs.IsDead(target))
                continue;

            var offset = _transform.GetWorldPosition(target) - origin.Position;
            var distance = offset.Length();
            if (distance > BurstRange || Vector2.Dot(direction, offset) <= distance * MathF.Cos(BurstHalfAngle)
                || !_interaction.InRangeUnobstructed(user, target, range: BurstRange, overlapCheck: false))
                continue;

            _throwing.TryThrow(target, direction * 1.25f, baseThrowSpeed: 4f, recoil: false,
                animated: false, playSound: false, doSpin: false);
        }

        args.Handled = true;
    }

    private void SendHeatWave(EntityUid user, MapCoordinates origin, float heading, float halfAngle, float range)
    {
        var filter = Filter.Pvs(user);
        if (!filter.Recipients.Any())
            return;

        var rays = new Vector2[LavriteHeatWaveEvent.RayCount];
        for (var i = 0; i < rays.Length; i++)
        {
            var angle = heading + halfAngle * (2f * i / (rays.Length - 1) - 1f);
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var ray = new CollisionRay(origin.Position, direction, (int) SharedInteractionSystem.InRangeUnobstructedMask);
            var distance = range;
            foreach (var hit in _physics.IntersectRay(origin.MapId, ray, range, user, returnOnFirstHit: false))
                distance = Math.Min(distance, hit.Distance);

            rays[i] = direction * distance;
        }

        RaiseNetworkEvent(new LavriteHeatWaveEvent(origin, range, rays), filter);
    }
}
