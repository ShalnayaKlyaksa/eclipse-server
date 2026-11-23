using Content.Server.Atmos.EntitySystems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.Generation.Teg;
using Content.Shared.Radiation.Systems;
using Content.Shared._Eclipse.Power;
using Content.Shared.Construction.Components;
using Content.Shared.Examine;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Eclipse.Power;

/// <summary>
/// Graphite moderated, gas cooled multiblock. A bounded tile scan validates the physical core;
/// heat leaves only in real gas parcels. The core itself never supplies electricity.
/// </summary>
public sealed class NuclearReactorSystem : EntitySystem
{
    private static readonly (int X, int Y)[] Neighbours = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly NodeContainerSystem _nodes = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly SharedRadiationSystem _radiation = default!;
    private float _elapsed;

    public override void Initialize()
    {
        SubscribeLocalEvent<NuclearReactorPartComponent, BeforeActivatableUIOpenEvent>(OnOpen);
        SubscribeLocalEvent<NuclearReactorPartComponent, NuclearReactorCommandMessage>(OnCommand);
        SubscribeLocalEvent<NuclearReactorPartComponent, UnanchorAttemptEvent>(OnUnanchor);
        SubscribeLocalEvent<NuclearReactorPartComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<NuclearReactorComponent, UnanchorAttemptEvent>(OnCoreUnanchor);
        SubscribeLocalEvent<NuclearReactorComponent, EntityTerminatingEvent>(OnCoreDestroyed);
    }

    public static NuclearPartKind RequiredPart(int x, int y)
    {
        if (x == 0 && y == 0) return NuclearPartKind.Core;
        if (x == -3 && y == 0) return NuclearPartKind.Inlet;
        if (x == 3 && y == 0) return NuclearPartKind.Outlet;
        if (x == 0 && y == 3) return NuclearPartKind.Console;
        if (Math.Abs(x) == 3 || Math.Abs(y) == 3) return NuclearPartKind.Shield;
        // Interior positions are configurable; at least four independent control channels are required.
        return NuclearPartKind.Fuel;
    }

    public void ScanLayout(Entity<NuclearReactorComponent> ent)
    {
        var core = ent.Comp;
        foreach (var part in core.Parts.Values)
            if (TryComp<NuclearReactorPartComponent>(part, out var old) && old.Core == ent.Owner) old.Core = null;
        core.Parts.Clear();
        core.Cells = new NuclearCellState[49];
        core.Faults = 0;
        var xform = Transform(ent);
        if (!xform.Anchored || !TryComp<MapGridComponent>(xform.GridUid, out var grid))
        {
            core.Complete = false;
            core.Faults = 49;
            for (var i = 0; i < 49; i++) core.Cells[i] = new(NuclearPartKind.Missing, false, 0);
            return;
        }
        var origin = _map.LocalToTile(xform.GridUid.Value, grid, xform.Coordinates);
        var fuels = 0;
        var graphite = 0;
        var controls = 0;
        for (var y = -3; y <= 3; y++)
        for (var x = -3; x <= 3; x++)
        {
            var expected = RequiredPart(x, y);
            EntityUid? found = null;
            var kind = NuclearPartKind.Missing;
            var fuel = 0f;
            foreach (var candidate in _map.GetAnchoredEntities(xform.GridUid.Value, grid, origin + new Vector2i(x, y)))
            {
                NuclearPartKind candidateKind;
                if (HasComp<NuclearReactorComponent>(candidate)) candidateKind = NuclearPartKind.Core;
                else if (TryComp<NuclearReactorPartComponent>(candidate, out var part)) candidateKind = part.Kind;
                else continue;
                if (found != null) { kind = NuclearPartKind.Conflict; break; }
                found = candidate;
                kind = candidateKind;
            }
            var interior = Math.Abs(x) < 3 && Math.Abs(y) < 3 && (x != 0 || y != 0);
            var valid = interior
                ? kind is NuclearPartKind.Fuel or NuclearPartKind.Graphite or NuclearPartKind.Control
                : kind == expected && (kind != NuclearPartKind.Core || found == ent.Owner);
            if (valid && found != null)
            {
                core.Parts[(x, y)] = found.Value;
                if (TryComp<NuclearReactorPartComponent>(found, out var part))
                {
                    part.Core = ent.Owner;
                    if (kind == NuclearPartKind.Fuel)
                    {
                        fuels++;
                        fuel = (float) Math.Clamp(part.FuelEnergy / Math.Max(1, part.InitialFuelEnergy), 0, 1);
                    }
                    if (kind == NuclearPartKind.Graphite) graphite++;
                    if (kind == NuclearPartKind.Control) controls++;
                }
            }
            else core.Faults++;
            core.Cells[(3 - y) * 7 + x + 3] = new(kind, valid, fuel);
        }
        if (fuels < 4) core.Faults++;
        if (graphite < 4) core.Faults++;
        if (controls < 4) core.Faults++;
        core.Complete = core.Faults == 0;
        core.LayoutAge = 0;
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
            var powered = ConsolePowered(core);
            core.CoolingPower = Cool(core, dt) / dt;
            if (!core.Complete) Scram(core, "nuclear-status-incomplete");
            else if (!powered) Scram(core, "nuclear-status-power-loss");
            else if (!core.Scrammed && (core.Pressure < core.MinimumPressure || core.Flow < .01f)) Scram(core, "nuclear-status-no-flow");
            else if (core.Pressure > core.MaximumPressure) Scram(core, "nuclear-status-pressure");
            else if (core.Temperature >= core.TripTemperature) Scram(core, "nuclear-status-overheat");
            var fuel = 0f;
            var moderators = 0;
            var channels = 0;
            var controls = 0;
            foreach (var partUid in core.Parts.Values)
            {
                if (!TryComp<NuclearReactorPartComponent>(partUid, out var part)) continue;
                if (part.Kind == NuclearPartKind.Fuel) { fuel += (float) Math.Max(0, part.FuelEnergy); channels++; }
                if (part.Kind == NuclearPartKind.Graphite) moderators++;
                if (part.Kind == NuclearPartKind.Control) controls++;
            }
            core.ControlAvailability = Math.Clamp(controls / 4f, 0, 1);
            var contacts = 0;
            foreach (var (position, partUid) in core.Parts)
            {
                if (!TryComp<NuclearReactorPartComponent>(partUid, out var part) || part.Kind != NuclearPartKind.Fuel) continue;
                foreach (var offset in Neighbours)
                    if (core.Parts.TryGetValue((position.X + offset.X, position.Y + offset.Y), out var neighbour) &&
                        TryComp<NuclearReactorPartComponent>(neighbour, out var moderator) && moderator.Kind == NuclearPartKind.Graphite) contacts++;
            }
            var moderation = Math.Clamp(moderators / 8f, 0, 1) * Math.Clamp(contacts / MathF.Max(1, channels * 2), 0, 1);
            var burned = StepPhysics(core, dt, fuel, moderation);
            _radiation.SetIntensity(uid, 60 * (core.ThermalPower + core.DecayPower) / core.MaximumPower);
            // Equal sharing accounts for every joule even when one assembly depletes this step.
            while (burned > .1f && channels > 0)
            {
                var share = burned / channels;
                var spent = 0f;
                channels = 0;
                foreach (var partUid in core.Parts.Values)
                {
                    if (!TryComp<NuclearReactorPartComponent>(partUid, out var part) || part.Kind != NuclearPartKind.Fuel) continue;
                    var use = (float) Math.Min(Math.Max(0, part.FuelEnergy), share);
                    part.FuelEnergy -= use;
                    spent += use;
                    if (part.FuelEnergy > .1f) channels++;
                }
                if (spent <= 0) break;
                burned -= spent;
            }
            if (!core.Failed && core.Temperature >= core.FailureTemperature)
            {
                core.Failed = true;
                Scram(core, "nuclear-status-failed");
                _explosion.TriggerExplosive(uid, totalIntensity: 15000, radius: 8);
            }
            Publish(core, powered);
        }
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

    /// <summary>Accelerated game kinetics. Fuel energy becomes prompt heat or a finite decay reservoir.</summary>
    public static float StepPhysics(NuclearReactorComponent core, float dt, float fuelEnergy, float moderation)
    {
        if (!float.IsFinite(dt) || dt <= 0) return 0;
        dt = MathF.Min(dt, 1);
        var target = core.Scrammed || core.Failed ? 1 : core.RodTarget;
        var travel = dt / (core.Scrammed ? 2 : 20);
        core.RodPosition += Math.Clamp(target - core.RodPosition, -travel, travel);
        core.Reactivity = .08f * moderation - .16f * core.RodPosition * core.ControlAvailability -
            .00012f * MathF.Max(0, core.Temperature - 293.15f) - .04f * core.Xenon;
        if (core.Failed || fuelEnergy <= 0) core.NeutronPower = 0;
        else core.NeutronPower = Math.Clamp((core.NeutronPower + 10 * dt) * MathF.Exp(core.Reactivity * 8 * dt), 0, core.MaximumPower * 2);
        var burned = MathF.Min(MathF.Max(0, fuelEnergy), core.NeutronPower * dt);
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
        if (!core.Parts.TryGetValue((-3, 0), out var input) || !core.Parts.TryGetValue((3, 0), out var output) ||
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

    public static float ExchangeHeat(NuclearReactorComponent core, float temperature, float capacity, float dt)
    {
        if (capacity <= 0 || dt <= 0) return 0;
        var heat = (core.Temperature - temperature) / (1 / core.HeatCapacity + 1 / capacity);
        heat = Math.Clamp(heat, -core.MaximumHeatTransfer * dt, core.MaximumHeatTransfer * dt);
        core.Temperature -= heat / core.HeatCapacity;
        return heat;
    }

    private bool ConsolePowered(NuclearReactorComponent core) =>
        core.Parts.TryGetValue((0, 3), out var console) && Exists(console) && Transform(console).Anchored &&
        TryComp<ApcPowerReceiverComponent>(console, out var power) && power.Powered;

    private static void Scram(NuclearReactorComponent core, string status)
    {
        core.Scrammed = true;
        core.RodTarget = 1;
        if (!core.Failed) core.Status = status;
        else core.Status = "nuclear-status-failed";
    }

    private bool TryConsoleCore(Entity<NuclearReactorPartComponent> ent, out Entity<NuclearReactorComponent> result)
    {
        result = default;
        if (ent.Comp.Kind != NuclearPartKind.Console || !Transform(ent).Anchored) return false;
        var xform = Transform(ent);
        if (!TryComp<MapGridComponent>(xform.GridUid, out var grid)) return false;
        var tile = _map.LocalToTile(xform.GridUid.Value, grid, xform.Coordinates) + new Vector2i(0, -3);
        foreach (var candidate in _map.GetAnchoredEntities(xform.GridUid.Value, grid, tile))
            if (TryComp<NuclearReactorComponent>(candidate, out var core))
            {
                result = (candidate, core);
                ScanLayout(result);
                return true;
            }
        return false;
    }

    private void OnOpen(Entity<NuclearReactorPartComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        if (TryConsoleCore(ent, out var core)) Publish(core.Comp, ConsolePowered(core.Comp));
        else if (ent.Comp.Kind == NuclearPartKind.Console) _ui.SetUiState(ent.Owner, NuclearReactorUiKey.Key, new NuclearReactorState());
    }

    private void OnCommand(Entity<NuclearReactorPartComponent> ent, ref NuclearReactorCommandMessage args)
    {
        if (!TryConsoleCore(ent, out var core)) return;
        // Mechanical emergency insertion remains available without control power.
        if (args.Command == NuclearCommand.Scram) Scram(core.Comp, "nuclear-status-scram");
        else if (ConsolePowered(core.Comp) && !core.Comp.Failed)
        {
            if (args.Command == NuclearCommand.Reset && core.Comp.Complete && core.Comp.Temperature < 600 &&
                core.Comp.Pressure >= core.Comp.MinimumPressure && core.Comp.Pressure <= core.Comp.MaximumPressure && core.Comp.Flow >= .01f)
            { core.Comp.Scrammed = false; core.Comp.Status = "nuclear-status-starting"; }
            else if (args.Command == NuclearCommand.Rods && !core.Comp.Scrammed && float.IsFinite(args.Value))
                core.Comp.RodTarget = Math.Clamp(args.Value, 0, 1);
        }
        Publish(core.Comp, ConsolePowered(core.Comp));
    }

    private void Publish(NuclearReactorComponent core, bool powered)
    {
        if (!core.Parts.TryGetValue((0, 3), out var console) || !Exists(console)) return;
        _ui.SetUiState(console, NuclearReactorUiKey.Key, new NuclearReactorState
        {
            Cells = core.Cells, Status = core.Status, Powered = powered, Complete = core.Complete,
            Scrammed = core.Scrammed, Temperature = core.Temperature, Power = core.ThermalPower,
            Cooling = core.CoolingPower, Decay = core.DecayPower, Pressure = core.Pressure,
            Flow = core.Flow, RodPosition = core.RodPosition, RodTarget = core.RodTarget,
            Reactivity = core.Reactivity, Xenon = core.Xenon, Faults = core.Faults,
        });
    }

    private void OnUnanchor(Entity<NuclearReactorPartComponent> ent, ref UnanchorAttemptEvent args)
    {
        if (ent.Comp.Core is { } owner && TryComp<NuclearReactorComponent>(owner, out var core) &&
            (core.Temperature > 350 || !core.Scrammed || core.RodPosition < .999f)) args.Cancel();
    }

    private void OnCoreUnanchor(Entity<NuclearReactorComponent> ent, ref UnanchorAttemptEvent args)
    {
        if (ent.Comp.Temperature > 350 || !ent.Comp.Scrammed || ent.Comp.RodPosition < .999f) args.Cancel();
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
            args.PushMarkup(Loc.GetString("nuclear-fuel-examine", ("percent", Math.Round(100 * ent.Comp.FuelEnergy / Math.Max(1, ent.Comp.InitialFuelEnergy)))));
    }
}
