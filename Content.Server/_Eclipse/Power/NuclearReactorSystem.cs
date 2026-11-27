using Content.Server.Atmos.EntitySystems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.Generation.Teg;
using Content.Shared.Radiation.Systems;
using Content.Shared._Eclipse.Power;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Construction.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.Power;

/// <summary>
/// Graphite-moderated multiblock with interchangeable gas and water collectors.
/// A bounded tile scan validates the core. Heat is carried away by the coolant.
/// </summary>
public sealed partial class NuclearReactorSystem : EntitySystem
{
    private static readonly (int X, int Y)[] Neighbours = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly ProtoId<ReagentPrototype> HeavyWater = "HeavyWater";
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly NodeContainerSystem _nodes = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly SharedRadiationSystem _radiation = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPointLightSystem _light = default!;
    [Dependency] private readonly SharedDeviceLinkSystem _links = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    private float _elapsed;

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(PowerNetSystem));
        SubscribeLocalEvent<NuclearReactorPartComponent, BeforeActivatableUIOpenEvent>(OnOpen);
        SubscribeLocalEvent<NuclearReactorPartComponent, NuclearReactorCommandMessage>(OnCommand);
        SubscribeLocalEvent<NuclearReactorPartComponent, UnanchorAttemptEvent>(OnUnanchor);
        SubscribeLocalEvent<NuclearReactorPartComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<NuclearReactorPartComponent, LinkAttemptEvent>(OnLinkAttempt);
        SubscribeLocalEvent<NuclearReactorComponent, UnanchorAttemptEvent>(OnCoreUnanchor);
        SubscribeLocalEvent<NuclearReactorComponent, EntityTerminatingEvent>(OnCoreDestroyed);
        SubscribeLocalEvent<NuclearReactorPartComponent, MapInitEvent>(InitializeFuel);
        SubscribeLocalEvent<NuclearReactorPartComponent, ItemSlotInsertAttemptEvent>(OnFuelInsert);
        SubscribeLocalEvent<NuclearReactorPartComponent, ItemSlotEjectAttemptEvent>(OnFuelEject);
        SubscribeLocalEvent<NuclearFuelCellComponent, ExaminedEvent>(OnFuelExamined);
        SubscribeLocalEvent<NuclearReactorPartComponent, InteractHandEvent>(OnFuelHatchInteract);
        SubscribeLocalEvent<NuclearSteamTurbineComponent, ExaminedEvent>(OnSteamTurbineExamined);
    }

    public static NuclearPartKind RequiredPart(int x, int y)
    {
        if (x == 0 && y == 0) return NuclearPartKind.Core;
        if (x == -3 && y == 0) return NuclearPartKind.Inlet;
        if (x == 3 && y == 0) return NuclearPartKind.Outlet;
        if (Math.Abs(x) == 3 || Math.Abs(y) == 3) return NuclearPartKind.Shield;
        // Interior positions are configurable; at least four independent control channels are required.
        return NuclearPartKind.Fuel;
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < .25f) return;
        var dt = MathF.Min(_elapsed, 1);
        _elapsed = 0;
        var query = EntityQueryEnumerator<NuclearReactorComponent>();
        while (query.MoveNext(out var uid, out var core))
        {
            if (Paused(uid) || TerminatingOrDeleted(uid)) continue;
            core.LayoutAge += dt;
            if (core.LayoutAge >= 1 || core.Cells.Length == 0) ScanLayout((uid, core));
            var console = FindConsole(uid);
            if (core.Console != console && core.Console is { } previous && !TerminatingOrDeleted(previous))
                _ui.SetUiState(previous, NuclearReactorUiKey.Key, new NuclearReactorState());
            core.Console = console;
            var powered = ConsolePowered(console);
            ReadCoreCharge(uid, core);
            core.SteamFlow = 0;
            core.CoolingPower = (core.SteamCooling ? CoolWithSteam(core, dt) :
                core.WaterCooling ? CoolWithWater(core, dt) : Cool(core, dt)) / dt;
            var (fuelEnergy, moxShare, fuelFactor) = ReadFuelLoad(core);
            core.CoolingPower += UpdateConditioner(core, dt, moxShare);
            if (!core.Complete) Scram(core, "nuclear-status-incomplete");
            else if (!powered) Scram(core, "nuclear-status-power-loss");
            else if (core.HeavyWaterLevel < NuclearReactorComponent.RequiredHeavyWater) Scram(core, "nuclear-status-heavy-water");
            else if (!core.Scrammed && (core.WaterCooling
                ? core.NoWaterFlowTime >= 2
                : core.Pressure < core.MinimumPressure || core.Flow < .01f)) Scram(core, "nuclear-status-no-flow");
            else if (core.SteamCooling ? core.Pressure >= core.MaximumPressure :
                     !core.WaterCooling && core.Pressure > core.MaximumPressure) Scram(core, "nuclear-status-pressure");
            else if (core.LiveCells == 0) Scram(core, "nuclear-status-no-fuel");
            else if (AtTripTemperature(core)) Scram(core, "nuclear-status-overheat");
            var moderators = 0;
            var channels = 0;
            foreach (var partUid in core.Parts.Values)
            {
                if (!TryComp<NuclearReactorPartComponent>(partUid, out var part)) continue;
                if (part.Kind == NuclearPartKind.Fuel) channels++;
                if (part.Kind == NuclearPartKind.Graphite) moderators++;
            }
            var contacts = 0;
            foreach (var (position, partUid) in core.Parts)
            {
                if (!TryComp<NuclearReactorPartComponent>(partUid, out var part) || part.Kind != NuclearPartKind.Fuel) continue;
                foreach (var offset in Neighbours)
                    if (core.Parts.TryGetValue((position.X + offset.X, position.Y + offset.Y), out var neighbour) &&
                        TryComp<NuclearReactorPartComponent>(neighbour, out var moderator) && moderator.Kind == NuclearPartKind.Graphite) contacts++;
            }
            var moderation = Math.Clamp(moderators / 8f, 0, 1) * Math.Clamp(contacts / MathF.Max(1, channels * 2), 0, 1);
            var burned = StepPhysics(core, dt, fuelEnergy, moderation, moxShare, fuelFactor);
            var active = core.Complete && core.ThermalPower > 10000;
            _appearance.SetData(uid, NuclearReactorVisuals.Active, active);
            _light.SetEnabled(uid, active);
            if (active)
                _light.SetEnergy(uid, .7f + 3.3f * Math.Clamp(core.ThermalPower / core.MaximumPower, 0, 1));
            _radiation.SetIntensity(uid, 60 * (core.ThermalPower + core.DecayPower) / core.MaximumPower);
            BurnFuel(burned);
            if (!core.Failed && core.Temperature >= HeatLimit(core))
            {
                core.Failed = true;
                Scram(core, "nuclear-status-failed");
                _explosion.TriggerExplosive(uid, totalIntensity: 15000, radius: 8);
            }
            else if (!core.Scrammed && AtTripTemperature(core))
                Scram(core, "nuclear-status-overheat");
            else if (!core.Scrammed && core.MoxCells > 0 && HeatFraction(core) >= .98f)
                core.Status = "nuclear-status-mox-edge";
            if (console is { } terminal && _ui.IsUiOpen(terminal, NuclearReactorUiKey.Key))
                Publish(terminal, core, powered);
            foreach (var partUid in core.Parts.Values)
            {
                if (!TryComp<NuclearReactorPartComponent>(partUid, out var part))
                    continue;
                if (part.Kind == NuclearPartKind.Fuel)
                {
                    var cell = ReadFuel(partUid);
                    _appearance.SetData(partUid, NuclearReactorVisuals.Active, active && cell is { Energy: > .1 });
                    _appearance.SetData(partUid, NuclearReactorVisuals.Fuel, cell?.Kind ?? NuclearFuelKind.None);
                }
                else if (part.Kind == NuclearPartKind.Control)
                {
                    var insertion = core.RodGroups[Math.Clamp(part.RodGroup, 0, core.RodGroups.Length - 1)].Position;
                    _appearance.SetData(partUid, NuclearReactorVisuals.RodInsertion, (int) MathF.Round(insertion * 100));
                    _appearance.SetData(partUid, NuclearReactorVisuals.Rods, insertion >= .95f ? "in" : insertion <= .05f ? "out" : "part");
                }
            }
        }
        UpdateWaterTurbines(dt);
        UpdateSteamEquipment(dt);
        var debris = EntityQueryEnumerator<NuclearCoriumComponent>();
        while (debris.MoveNext(out var uid, out var corium))
        {
            if (Paused(uid) || TerminatingOrDeleted(uid)) continue;
            var decay = corium.DecayEnergy * (1 - MathF.Exp(-dt / 180));
            corium.DecayEnergy -= decay;
            corium.Temperature += decay / corium.HeatCapacity;
            var air = _atmosphere.GetContainingMixture(uid, false, true);
            if (air is { TotalMoles: > 0 })
            {
                var capacity = _atmosphere.GetHeatCapacity(air, true);
                if (capacity > 0)
                {
                    var heat = Math.Clamp((corium.Temperature - air.Temperature) / (1 / corium.HeatCapacity + 1 / capacity), 0, 50000 * dt);
                    corium.Temperature -= heat / corium.HeatCapacity;
                    air.Temperature += heat / capacity;
                }
            }
            _radiation.SetIntensity(uid, 4 + MathF.Min(20, decay / dt / 10000));
        }
    }

    private void ReadCoreCharge(EntityUid uid, NuclearReactorComponent core)
    {
        core.HeavyWaterLevel = 0;
        if (_solutions.TryGetSolution(uid, "charge", out _, out var charge) &&
            charge.GetTotalPrototypeQuantity(HeavyWater) == charge.Volume)
            core.HeavyWaterLevel = charge.Volume.Float();
    }

    /// <summary>Accelerated game kinetics. Fuel energy becomes prompt heat or a finite decay reservoir.</summary>
    public static float StepPhysics(NuclearReactorComponent core, float dt, float fuelEnergy, float moderation,
        float moxShare = 0, float fuelFactor = 1)
    {
        if (!float.IsFinite(dt) || dt <= 0) return 0;
        dt = MathF.Min(dt, 1);
        var scram = core.Scrammed || core.Failed;
        var travel = dt / (scram ? 2 : 20);
        var inserted = 0f;
        var rods = 0;
        for (var i = 0; i < core.RodGroups.Length; i++)
        {
            var group = core.RodGroups[i];
            var target = scram ? 1 : group.Target;
            var position = group.Position + Math.Clamp(target - group.Position, -travel, travel);
            core.RodGroups[i] = group with { Position = position, Target = target };
            inserted += position * group.Count;
            rods += group.Count;
        }
        var absorption = inserted / Math.Max(4, rods);
        var heatFeedback = .00012f * (1 - .35f * moxShare);
        core.Reactivity = .08f * moderation - .16f * absorption -
            heatFeedback * MathF.Max(0, core.Temperature - 293.15f) - .04f * core.Xenon;
        if (core.Failed || fuelEnergy <= 0) core.NeutronPower = 0;
        else core.NeutronPower = Math.Clamp((core.NeutronPower + 10 * dt) * MathF.Exp(core.Reactivity * 8 * dt), 0, core.MaximumPower * 2);
        var multiplier = fuelFactor + (MoxPowerFactor(HeatFraction(core), core.MoxBoost && !scram) - 1) * moxShare;
        var burned = MathF.Min(MathF.Max(0, fuelEnergy), core.NeutronPower * multiplier * dt);
        core.ThermalPower = burned / dt;
        core.DecayEnergy += burned * .06f;
        var decay = core.DecayEnergy * (1 - MathF.Exp(-dt / 180));
        core.DecayEnergy -= decay;
        core.DecayPower = decay / dt;
        core.Temperature += (burned * .94f + decay) / core.HeatCapacity;
        // Deliberately compressed iodine/xenon time scales make shutdown poisoning visible within a round.
        var relativePower = core.ThermalPower / core.MaximumPower;
        var iodineLoss = core.Iodine * (1 - MathF.Exp(-dt / 180));
        core.Iodine = MathF.Max(0, core.Iodine + relativePower * dt / 180 - iodineLoss);
        core.Xenon = MathF.Max(0, core.Xenon + iodineLoss - core.Xenon * (1 - MathF.Exp(-dt * (1 / 360f + relativePower / 120))));
        if (!core.Scrammed) core.Status = core.ThermalPower > 10000 ? "nuclear-status-running" : "nuclear-status-starting";
        return burned;
    }

    private float Cool(NuclearReactorComponent core, float dt)
    {
        core.Flow = 0;
        core.Pressure = 0;
        if (core.Inlet is not { } input || core.Outlet is not { } output ||
            !Exists(input) || !Exists(output) || !Transform(input).Anchored || !Transform(output).Anchored ||
            !_nodes.TryGetNode(input, "coolant", out PipeNode? inlet) || !_nodes.TryGetNode(output, "coolant", out PipeNode? outlet)) return 0;
        core.Pressure = MathF.Max(inlet.Air.Pressure, outlet.Air.Pressure);
        var (parcel, _) = TegSystem.GetCirculatorAirTransfer(inlet.Air, outlet.Air);
        core.Flow = parcel.TotalMoles / dt;
        var capacity = _atmosphere.GetHeatCapacity(parcel, true);
        if (parcel.TotalMoles <= 0 || capacity <= 0) return 0;
        var heat = ExchangeHeat(core, parcel.Temperature, capacity, dt);
        parcel.Temperature += heat / capacity;
        _atmosphere.Merge(outlet.Air, parcel);
        return heat;
    }

    public static float ExchangeHeat(NuclearReactorComponent core, float temperature, float capacity, float dt, float? maximumPower = null)
    {
        if (capacity <= 0 || dt <= 0) return 0;
        var heat = (core.Temperature - temperature) / (1 / core.HeatCapacity + 1 / capacity);
        var limit = (maximumPower ?? core.MaximumHeatTransfer) * dt;
        heat = Math.Clamp(heat, -limit, limit);
        core.Temperature -= heat / core.HeatCapacity;
        return heat;
    }

    private void OnUnanchor(Entity<NuclearReactorPartComponent> ent, ref UnanchorAttemptEvent args)
    {
        if (ent.Comp.Core is { } owner && TryComp<NuclearReactorComponent>(owner, out var core) &&
            (core.Temperature > 350 || !RodsSecured(core))) args.Cancel();
    }

    private void OnCoreUnanchor(Entity<NuclearReactorComponent> ent, ref UnanchorAttemptEvent args)
    {
        if (ent.Comp.Temperature > 350 || !RodsSecured(ent.Comp)) args.Cancel();
    }

    private void OnCoreDestroyed(Entity<NuclearReactorComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Temperature <= 350 && ent.Comp.DecayEnergy <= 1000) return;
        var xform = Transform(ent);
        if (xform.GridUid is not { } grid || TerminatingOrDeleted(grid)) return;
        var debris = Spawn("NuclearReactorCorium", xform.Coordinates);
        var corium = Comp<NuclearCoriumComponent>(debris);
        corium.Temperature = ent.Comp.Temperature;
        corium.HeatCapacity = ent.Comp.HeatCapacity;
        corium.DecayEnergy = ent.Comp.DecayEnergy;
    }

    private void OnExamined(Entity<NuclearReactorPartComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("nuclear-part-examine", ("kind", Loc.GetString($"nuclear-part-{ent.Comp.Kind.ToString().ToLowerInvariant()}"))));
        if (ent.Comp.Kind == NuclearPartKind.Fuel)
        {
            args.PushMarkup(Loc.GetString("nuclear-fuel-examine", ("percent", Math.Round(100 * (ReadFuel(ent.Owner) is { } cell ? FuelRemaining(cell) : 0)))));
            args.PushMarkup(FuelServiceBlock(ent.Comp) ?? Loc.GetString("nuclear-fuel-hatch-ready"));
        }
    }
}
