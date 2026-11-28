using Content.Server.Atmos.EntitySystems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.Generation.Teg;
using Content.Server.Power.Generator;
using Content.Server._Eclipse.Industrial;
using Content.Shared._Eclipse.Power;
using Content.Shared.Examine;
using Robust.Shared.Map;
using Content.Shared.Popups;
using Content.Shared.Power.Generator;
using Robust.Shared.Map.Components;

namespace Content.Server._Eclipse.Power;

public sealed class PowerPlantSystem : EntitySystem
{
    [Dependency] private readonly GeneratorSystem _generators = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly NodeContainerSystem _nodes = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IndustrialPowerSurgeSystem _surges = default!;
    [Dependency] private readonly ExplosionSystem _explosions = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;
    private float _elapsed;

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(GeneratorSystem));
        SubscribeLocalEvent<PowerPlantComponent, GeneratorOutputLimitEvent>(LimitOutput);
        SubscribeLocalEvent<PowerPlantComponent, GeneratorUseFuel>(OnFuelUsed);
        SubscribeLocalEvent<PowerPlantComponent, GeneratorTelemetryEvent>(GetTelemetry);
        SubscribeLocalEvent<PowerPlantComponent, PowerPlantResetMessage>(Reset);
        SubscribeLocalEvent<PowerPlantComponent, ExaminedEvent>(OnExamined);
    }

    private void LimitOutput(Entity<PowerPlantComponent> ent, ref GeneratorOutputLimitEvent args)
    {
        var plant = ent.Comp;
        plant.LastStepTime = args.DeltaTime;
        if (plant.Tripped || plant.Failed) { args.Power = 0; return; }
        var fusion = TryComp<FusionReactorComponent>(ent, out var reactor);
        if (fusion && GetFusionInterlock(ent, reactor!) is { } reason)
        {
            if (plant.Warmup > 0)
                Trip(ent, reason);
            else
                _generators.SetFuelGeneratorOn(ent, false);
            plant.Condition = reason;
            args.Power = 0;
            return;
        }
        if (plant.RequiredFields > plant.FieldCount)
        {
            // Before ignition this is a start interlock. Only loss of an established field is a trip.
            if (plant.Warmup > 0) Trip(ent, PowerPlantCondition.MissingField);
            else plant.Condition = PowerPlantCondition.MissingField;
            args.Power = 0;
            return;
        }
        plant.Warmup = Math.Clamp(plant.Warmup + args.DeltaTime / MathF.Max(.1f, plant.WarmupSeconds), 0, 1);
        var derate = Math.Clamp((plant.ShutdownTemperature - plant.Temperature) / (plant.ShutdownTemperature * .15f), 0, 1);
        if (fusion)
        {
            LimitFusionOutput(ent, reactor!, derate, ref args);
            return;
        }
        args.Power *= plant.Warmup * derate;
        if (plant.HeatSource || HasComp<GravityPowerSourceComponent>(ent)) args.ElectricalFraction = 0;
        plant.Condition = plant.Warmup < 1 ? PowerPlantCondition.Warming : PowerPlantCondition.Running;
    }

    private PowerPlantCondition? GetFusionInterlock(EntityUid uid, FusionReactorComponent reactor)
    {
        var auxiliary = Comp<PowerConsumerComponent>(uid);
        if (auxiliary.Net is not { IsConnectedNetwork: true } || auxiliary.ReceivedPower < auxiliary.DrawRate * .99f)
            return PowerPlantCondition.NoAuxiliaryPower;

        return reactor.MeasuredFlow < reactor.MinimumFlow ? PowerPlantCondition.NoCoolant : null;
    }

    private void LimitFusionOutput(Entity<PowerPlantComponent> ent, FusionReactorComponent reactor, float derate,
        ref GeneratorOutputLimitEvent args)
    {
        var plant = ent.Comp;
        if (plant.Warmup < 1)
        {
            args.Power = 0;
            plant.Condition = PowerPlantCondition.Warming;
            return;
        }

        var generator = Comp<FuelGeneratorComponent>(ent);
        var nominal = generator.OptimalPower;
        var boost = Math.Clamp((reactor.MeasuredFlow - reactor.NominalFlow) / (reactor.BoostFlow - reactor.NominalFlow), 0, 1);
        var flowLimit = nominal * MathF.Min(reactor.MeasuredFlow / reactor.NominalFlow, 1) +
                        (generator.MaxTargetPower - nominal) * boost;
        var requested = args.Power;
        args.Power = MathF.Min(requested, flowLimit) * derate;
        plant.Condition = args.Power < requested * .99f ? PowerPlantCondition.Limited : PowerPlantCondition.Running;
    }

    private void OnFuelUsed(Entity<PowerPlantComponent> ent, ref GeneratorUseFuel args)
    {
        var heat = args.FuelUsed * ent.Comp.HeatPerFuel;
        ent.Comp.GeneratedHeatPower = ent.Comp.LastStepTime > 0 ? heat / ent.Comp.LastStepTime : 0;
        var delayed = heat * Math.Clamp(ent.Comp.DecayFraction, 0, 1);
        ent.Comp.StoredDecayEnergy += delayed;
        ent.Comp.Temperature += (heat - delayed) / ent.Comp.HeatCapacity;
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < .5f) return;
        var elapsed = MathF.Min(_elapsed, 1);
        _elapsed = 0;
        var query = EntityQueryEnumerator<PowerPlantComponent>();
        while (query.MoveNext(out var uid, out var plant))
        {
            if (Paused(uid) || TerminatingOrDeleted(uid)) continue;
            plant.FieldCount = plant.RequiredFields > 0 ? CountFields(uid) : 0;
            var decay = plant.StoredDecayEnergy * (1 - MathF.Exp(-elapsed / MathF.Max(1, plant.DecayTime)));
            plant.StoredDecayEnergy -= decay;
            plant.Temperature += decay / plant.HeatCapacity;
            plant.DecayPower = decay / elapsed;
            plant.CoolingPower = ExchangeCoolant(uid, plant, elapsed, out var flowRate) / elapsed;
            CoolIntoEnvironment(uid, plant, elapsed);
            TryComp<FusionReactorComponent>(uid, out var reactor);
            if (reactor != null)
            {
                // Atmos devices update in batches. Smooth short gaps between pump strokes.
                reactor.MeasuredFlow += (flowRate - reactor.MeasuredFlow) * (1 - MathF.Exp(-elapsed));
            }
            if (!_generators.IsRunning(uid))
            {
                plant.Warmup = 0;
                plant.GeneratedHeatPower = 0;
                if (!plant.Tripped && !plant.Failed)
                    plant.Condition = reactor != null
                        ? GetFusionInterlock(uid, reactor) ??
                          (plant.Temperature > 360 ? PowerPlantCondition.Cooling : PowerPlantCondition.Stopped)
                        : plant.StoredDecayEnergy > 1000 ? PowerPlantCondition.Cooling : PowerPlantCondition.Stopped;
            }
            if (plant.Temperature >= plant.ShutdownTemperature && !plant.Tripped)
                Trip((uid, plant), PowerPlantCondition.Overheated);
            if (plant.Temperature >= plant.FailureTemperature && !plant.Failed)
            {
                plant.Failed = true;
                plant.Condition = PowerPlantCondition.Failed;
                _generators.SetFuelGeneratorOn(uid, false);
                _explosions.TriggerExplosive(uid, totalIntensity: 300, radius: 3);
            }
            if (reactor != null)
            {
                var generator = Comp<FuelGeneratorComponent>(uid);
                var output = Comp<PowerSupplierComponent>(uid).MaxSupply;
                var status = plant.Tripped ? "trip" :
                    !generator.On ? "off" :
                    plant.Condition == PowerPlantCondition.Warming ? "ignition" :
                    plant.Condition == PowerPlantCondition.Limited ? "limited" :
                    output > generator.OptimalPower * 1.01f ? "boost" : "running";
                _appearance.SetData(uid, FusionReactorVisuals.Status, status);
                _lights.SetEnabled(uid, status != "off");
                _lights.SetColor(uid, status switch
                {
                    "boost" => Color.FromHex("#BA86FF"),
                    "limited" or "trip" => Color.FromHex("#FF884F"),
                    _ => Color.FromHex("#54E8FF"),
                });
            }
            if (!HasComp<GravityPowerSourceComponent>(uid) && TryComp<MicroBlackHoleVisualComponent>(uid, out var visual))
            {
                var activity = _generators.IsRunning(uid) && !plant.Tripped ? MathF.Max(.1f, plant.Warmup) : 0;
                var instability = Math.Clamp((plant.Temperature - plant.ShutdownTemperature * .8f) / (plant.ShutdownTemperature * .2f), 0, 1);
                if (MathF.Abs(visual.Activity - activity) > .01f || MathF.Abs(visual.Instability - instability) > .01f)
                {
                    visual.Activity = activity;
                    visual.Instability = instability;
                    Dirty(uid, visual);
                }
            }
        }
    }

    private float ExchangeCoolant(EntityUid uid, PowerPlantComponent plant, float elapsed, out float flowRate)
    {
        flowRate = 0;
        if (!Transform(uid).Anchored || !_nodes.TryGetNode(uid, plant.CoolantInlet, out PipeNode? inlet) ||
            !_nodes.TryGetNode(uid, plant.CoolantOutlet, out PipeNode? outlet) || ReferenceEquals(inlet.Air, outlet.Air)) return 0;
        var (flow, _) = TegSystem.GetCirculatorAirTransfer(inlet.Air, outlet.Air);
        if (flow.TotalMoles <= 0) return 0;
        flowRate = flow.TotalMoles / elapsed;
        var capacity = _atmosphere.GetHeatCapacity(flow, true);
        var heat = ExchangeCoolantHeat(plant, flow.Temperature, capacity, elapsed);
        flow.Temperature += heat / capacity;
        _atmosphere.Merge(outlet.Air, flow);
        return heat;
    }

    public static float ExchangeCoolantHeat(PowerPlantComponent plant, float temperature, float capacity, float elapsed)
    {
        if (capacity <= 0 || elapsed <= 0 || plant.HeatCapacity <= 0) return 0;
        var heat = (plant.Temperature - temperature) / (1 / plant.HeatCapacity + 1 / capacity);
        heat = Math.Clamp(heat, -plant.MaximumHeatTransfer * elapsed, plant.MaximumHeatTransfer * elapsed);
        plant.Temperature -= heat / plant.HeatCapacity;
        return heat;
    }

    private void CoolIntoEnvironment(EntityUid uid, PowerPlantComponent plant, float elapsed)
    {
        var air = _atmosphere.GetContainingMixture(uid, false, true);
        var ambient = air?.Temperature ?? 2.7f;
        var heat = MathF.Min(plant.PassiveCoolingPower * elapsed, MathF.Max(0, (plant.Temperature - ambient) * plant.HeatCapacity));
        plant.Temperature -= heat / plant.HeatCapacity;
        if (air != null && air.TotalMoles > 0) air.Temperature += heat / _atmosphere.GetHeatCapacity(air, true);
    }

    private int CountFields(EntityUid uid)
    {
        var transform = Transform(uid);
        if (transform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid)) return 0;
        var tile = _map.TileIndicesFor(gridUid, grid, transform.Coordinates);
        if (TryComp<GravityPowerSourceComponent>(uid, out var gravity))
        {
            var fields = 0;
            var projectors = 0;
            var limit = GravityPowerSystem.MaximumHorizon;
            foreach (var direction in FieldDirections)
            {
                var fieldDistance = 0;
                var hasProjector = false;
                for (var distance = 2; distance <= 12; distance++)
                {
                    var entities = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile + direction * distance);
                    while (entities.MoveNext(out var entity))
                    {
                        if (!TryComp<PowerConsumerComponent>(entity, out var power) || !GravityPowerSystem.IsPowered(power))
                            continue;

                        if (fieldDistance == 0 && HasComp<PowerPlantFieldStabilizerComponent>(entity))
                            fieldDistance = distance;
                        if (HasComp<GravityStaticChargeProjectorComponent>(entity))
                            hasProjector = true;
                    }

                    if (fieldDistance > 0 && hasProjector)
                        break;
                }

                if (hasProjector)
                    projectors++;
                if (fieldDistance == 0)
                    continue;
                fields++;
                limit = MathF.Min(limit, fieldDistance - .5f);
            }

            gravity.StaticProjectors = projectors;
            if (fields == 4)
                gravity.FieldLimit = limit;
            return fields;
        }

        var count = 0;
        var radius = Math.Clamp(Comp<PowerPlantComponent>(uid).FieldRadius, 1, 8);
        for (var x = -radius; x <= radius; x++)
        for (var y = -radius; y <= radius; y++)
        {
            if (x == 0 && y == 0) continue;
            if (radius > 1 && !((x == 0 && Math.Abs(y) == radius) || (y == 0 && Math.Abs(x) == radius))) continue;
            var entities = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile + new Vector2i(x, y));
            while (entities.MoveNext(out var entity))
                if (HasComp<PowerPlantFieldStabilizerComponent>(entity) && TryComp<PowerConsumerComponent>(entity, out var power) &&
                    power.NetworkLoad.Enabled && power.DrawRate > 0 && power.ReceivedPower >= power.DrawRate * .99f)
                { count++; break; }
        }
        return count;
    }

    private static readonly Vector2i[] FieldDirections = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    internal void Trip(Entity<PowerPlantComponent> ent, PowerPlantCondition reason)
    {
        if (ent.Comp.Tripped) return;
        ent.Comp.Tripped = true;
        ent.Comp.Condition = reason;
        ent.Comp.Warmup = 0;
        _generators.SetFuelGeneratorOn(ent, false);
        if (reason == PowerPlantCondition.MissingField)
        {
            if (HasComp<GravityPowerSourceComponent>(ent))
            {
                var collectors = EntityQueryEnumerator<GravitationalKineticGeneratorComponent>();
                while (collectors.MoveNext(out var uid, out var collector))
                    if (collector.Source == ent.Owner) _surges.Inject(uid, 1.6f, 3, 50000);
            }
            else _surges.Inject(ent, 1.6f, 3, 50000);
        }
        _popup.PopupEntity(Loc.GetString("power-plant-trip-" + reason.ToString().ToLowerInvariant()), ent);
    }

    private void Reset(Entity<PowerPlantComponent> ent, ref PowerPlantResetMessage args)
    {
        if (ent.Comp.Failed || ent.Comp.Temperature >= ent.Comp.ShutdownTemperature * .8f ||
            ent.Comp.FieldCount < ent.Comp.RequiredFields) return;
        if (TryComp<FusionReactorComponent>(ent, out var reactor) && GetFusionInterlock(ent, reactor) != null)
            return;
        ent.Comp.Tripped = false;
        ent.Comp.Condition = PowerPlantCondition.Stopped;
    }

    private void GetTelemetry(Entity<PowerPlantComponent> ent, ref GeneratorTelemetryEvent args)
    {
        TryComp<GravityPowerSourceComponent>(ent, out var gravity);
        TryComp<FusionReactorComponent>(ent, out var reactor);
        var supplier = Comp<PowerSupplierComponent>(ent);
        args.State = new PowerPlantOperatingState
        {
            Temperature = ent.Comp.Temperature, ShutdownTemperature = ent.Comp.ShutdownTemperature,
            Warmup = ent.Comp.Warmup, AvailablePower = supplier.Enabled ? supplier.MaxSupply : 0,
            CoolingPower = ent.Comp.CoolingPower, GeneratedHeatPower = ent.Comp.GeneratedHeatPower, DecayPower = ent.Comp.DecayPower, HeatSource = ent.Comp.HeatSource,
            Tripped = ent.Comp.Tripped, RequiredFields = ent.Comp.RequiredFields, FieldCount = ent.Comp.FieldCount,
            Condition = ent.Comp.Condition,
            GravitySource = gravity != null, GravityPower = gravity?.AvailablePower ?? 0,
            HarvestedPower = gravity?.HarvestedPower ?? 0, KineticGenerators = gravity?.Collectors ?? 0,
            GravityHorizon = gravity == null ? 0 : GravityPowerSystem.ActiveHorizon(gravity.Mass),
            GravityFieldLimit = gravity?.FieldLimit ?? 0,
            GravityStaticProjectors = gravity?.StaticProjectors ?? 0,
            FusionNominalPower = reactor == null ? null : Comp<FuelGeneratorComponent>(ent).OptimalPower,
            CoolantFlow = reactor?.MeasuredFlow ?? 0,
            AuxiliaryPower = reactor == null ? 0 : Comp<PowerConsumerComponent>(ent).ReceivedPower,
            DeliveredPower = supplier.Enabled && supplier.Net is { IsConnectedNetwork: true } ? supplier.CurrentSupply : 0,
            FusionReady = reactor != null && GetFusionInterlock(ent, reactor) == null,
        };
    }

    private void OnExamined(Entity<PowerPlantComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("power-plant-examine", ("temperature", (int) ent.Comp.Temperature),
            ("limit", (int) ent.Comp.ShutdownTemperature), ("cooling", (int) (ent.Comp.CoolingPower / 1000))));
    }
}
