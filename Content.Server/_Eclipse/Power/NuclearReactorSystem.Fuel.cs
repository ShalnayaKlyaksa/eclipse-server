using Content.Server.Power.Generation.Teg;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._Eclipse.Power;
using Content.Shared.Atmos;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;

namespace Content.Server._Eclipse.Power;

public sealed partial class NuclearReactorSystem
{
    public const string FuelSlot = "reactor_fuel";
    private const float MoxLimit = 780;
    private const float MoxBoostStart = .75f;
    private const float MoxPeak = .98f;
    private const float MoxTrip = .99f;
    private readonly List<NuclearFuelCellComponent> _burningFuel = new();
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private void InitializeFuel(Entity<NuclearReactorPartComponent> channel, ref MapInitEvent args)
    {
        if (channel.Comp.Kind != NuclearPartKind.Fuel || channel.Comp.FuelInitialized)
            return;

        // Older maps stored the uranium charge directly on the channel.
        if (_slots.GetItemOrNull(channel.Owner, FuelSlot) == null)
        {
            var item = Spawn("NuclearUraniumCell", Transform(channel).Coordinates);
            var cell = Comp<NuclearFuelCellComponent>(item);
            cell.InitialEnergy = channel.Comp.InitialFuelEnergy;
            cell.Energy = Math.Clamp(channel.Comp.FuelEnergy, 0, Math.Max(0, cell.InitialEnergy));
            if (!_slots.TryInsert(channel.Owner, FuelSlot, item, null))
            {
                QueueDel(item);
                return;
            }
        }

        channel.Comp.FuelInitialized = true;
    }

    private NuclearFuelCellComponent? ReadFuel(EntityUid uid) =>
        TryComp<NuclearFuelCellComponent>(_slots.GetItemOrNull(uid, FuelSlot), out var fuel) ? fuel : null;

    private static float FuelRemaining(NuclearFuelCellComponent cell) =>
        (float) Math.Clamp(cell.Energy / Math.Max(1, cell.InitialEnergy), 0, 1);

    // A steam boiler can settle at its boiling temperature after shutdown.
    private static float FuelServiceTemperature(NuclearReactorComponent core) => core.SteamCooling ? PhaseTemperature + 10 : 350;

    private bool CanServiceFuel(NuclearReactorPartComponent channel) =>
        !TryComp<NuclearReactorComponent>(channel.Core, out var reactor) ||
        RodsSecured(reactor) && reactor.Temperature <= FuelServiceTemperature(reactor);

    private string? FuelServiceBlock(NuclearReactorPartComponent channel)
    {
        if (!TryComp<NuclearReactorComponent>(channel.Core, out var reactor))
            return null;
        if (!reactor.Scrammed)
            return Loc.GetString("nuclear-fuel-hatch-scram");
        if (!RodsSecured(reactor))
            return Loc.GetString("nuclear-fuel-hatch-rods");
        var limit = FuelServiceTemperature(reactor);
        return reactor.Temperature > limit ? Loc.GetString("nuclear-fuel-hatch-hot", ("limit", limit)) : null;
    }

    private void OnFuelHatchInteract(Entity<NuclearReactorPartComponent> channel, ref InteractHandEvent args)
    {
        if (args.Handled || channel.Comp.Kind != NuclearPartKind.Fuel ||
            FuelServiceBlock(channel.Comp) is not { } message)
            return;

        _popup.PopupEntity(message, channel.Owner, args.User);
        args.Handled = true;
    }

    private void OnFuelInsert(Entity<NuclearReactorPartComponent> channel, ref ItemSlotInsertAttemptEvent args)
    {
        if (channel.Comp.Kind == NuclearPartKind.Fuel && !CanServiceFuel(channel.Comp))
            args.Cancelled = true;
    }

    private void OnFuelEject(Entity<NuclearReactorPartComponent> channel, ref ItemSlotEjectAttemptEvent args)
    {
        if (channel.Comp.Kind == NuclearPartKind.Fuel && !CanServiceFuel(channel.Comp))
            args.Cancelled = true;
    }

    private void OnFuelExamined(Entity<NuclearFuelCellComponent> item, ref ExaminedEvent args) =>
        args.PushMarkup(Loc.GetString("nuclear-fuel-examine", ("percent", MathF.Round(100 * FuelRemaining(item.Comp)))));

    private (float Energy, float MoxShare, float FuelFactor) ReadFuelLoad(NuclearReactorComponent core)
    {
        _burningFuel.Clear();
        core.MoxCells = 0;
        core.CoolingContacts = 0;
        var energy = 0d;
        var liveMox = 0;
        var factor = 0f;
        foreach (var (position, uid) in core.Parts)
        {
            if (!TryComp<NuclearReactorPartComponent>(uid, out var part) ||
                part.Kind != NuclearPartKind.Fuel || ReadFuel(uid) is not { } cell)
                continue;

            if (cell.Kind == NuclearFuelKind.Mox) core.MoxCells++;
            if (cell.Energy <= 0) continue;
            if (cell.Kind == NuclearFuelKind.Mox) liveMox++;
            factor += FuelFactor(cell.Kind,
                cell.Kind == NuclearFuelKind.Pulse ? CountLiveFuelNeighbours(core, position) : 0);
            foreach (var (x, y) in Neighbours)
            {
                if (core.Parts.TryGetValue((position.X + x, position.Y + y), out var neighbour) &&
                    TryComp<NuclearReactorPartComponent>(neighbour, out var adjacent) && adjacent.Kind == NuclearPartKind.HeatSink)
                    core.CoolingContacts++;
            }
            _burningFuel.Add(cell);
            energy += cell.Energy;
        }

        core.LiveCells = _burningFuel.Count;
        var count = Math.Max(1, core.LiveCells);
        return ((float) energy, (float) liveMox / count, factor / count);
    }

    private int CountLiveFuelNeighbours(NuclearReactorComponent core, (int X, int Y) position)
    {
        var count = 0;
        foreach (var (x, y) in Neighbours)
        {
            if (core.Parts.TryGetValue((position.X + x, position.Y + y), out var neighbour) &&
                ReadFuel(neighbour) is { Energy: > 0 })
                count++;
        }
        return count;
    }

    public static float FuelFactor(NuclearFuelKind kind, int neighbours) => kind switch
    {
        NuclearFuelKind.Thorium => .68f,
        NuclearFuelKind.Pulse => .8f + .4f * Math.Clamp(neighbours, 0, 4),
        _ => 1,
    };

    public static float CoolingContactFactor(int contacts) => 1 + .1f * Math.Clamp(contacts, 0, 6);

    private void BurnFuel(float burned)
    {
        var channels = _burningFuel.Count;
        while (burned > 0 && channels > 0)
        {
            var share = burned / channels;
            var spent = 0d;
            channels = 0;
            foreach (var cell in _burningFuel)
            {
                var use = Math.Min(cell.Energy, share);
                cell.Energy -= use;
                spent += use;
                if (cell.Energy > 0) channels++;
            }
            if (spent <= 0) break;
            burned = MathF.Max(0, burned - (float) spent);
        }
    }

    private static float HeatLimit(NuclearReactorComponent core) =>
        core.MoxCells > 0 ? MathF.Min(MoxLimit, core.FailureTemperature) : core.FailureTemperature;

    private static float HeatFraction(NuclearReactorComponent core) =>
        (core.Temperature - Atmospherics.T20C) / (HeatLimit(core) - Atmospherics.T20C);

    private static bool AtTripTemperature(NuclearReactorComponent core) =>
        core.Temperature >= core.TripTemperature || core.MoxCells > 0 && HeatFraction(core) >= MoxTrip;

    public static float MoxPowerFactor(float heat, bool conditioned)
    {
        // Gameplay curve. Also cap the curve above its peak while the reactor enters the trip band.
        var baseFactor = .55f + .45f * Math.Clamp(heat / MoxPeak, 0, 1);
        var boost = conditioned ? 2 * Math.Clamp((heat - MoxBoostStart) / (MoxPeak - MoxBoostStart), 0, 1) : 0;
        return baseFactor + boost;
    }

    private float UpdateConditioner(NuclearReactorComponent core, float dt, float moxShare)
    {
        core.MoxBoost = false;
        core.Oxidizer = core.Solvent = 0;
        if (core.Conditioner is not { } uid || TerminatingOrDeleted(uid) ||
            !Transform(uid).Anchored || !TryComp<NuclearReactorConditionerComponent>(uid, out var module))
        {
            core.AuxiliaryCooling = 0;
            return 0;
        }

        // The auxiliary circuit still carries decay heat after a trip or when reagents run out.
        var cooling = 0f;
        if (TryGetGasPorts(uid, "coolant_in", "coolant_out", out var inlet, out var outlet) &&
            _nodes.TryGetNode(uid, "coolant_in", out PipeNode? intake) && intake.NodeGroup is { Nodes.Count: > 1 } &&
            _nodes.TryGetNode(uid, "coolant_out", out PipeNode? exhaust) && exhaust.NodeGroup is { Nodes.Count: > 1 })
        {
            var (gas, _) = TegSystem.GetCirculatorAirTransfer(inlet, outlet);
            if (gas.TotalMoles > 0)
            {
                var capacity = _atmosphere.GetHeatCapacity(gas, true);
                var heat = ExchangeHeat(core, gas.Temperature, capacity, dt,
                    module.MaximumHeatTransfer * CoolingContactFactor(core.CoolingContacts));
                gas.Temperature += heat / capacity;
                _atmosphere.Merge(outlet, gas);
                cooling = heat / dt;
            }
            // Atmospheric pumps deliver in pulses, between reactor ticks.
            core.AuxiliaryCooling += (cooling - core.AuxiliaryCooling) * (1 - MathF.Exp(-dt / 1.5f));
        }
        else
            core.AuxiliaryCooling = 0;

        if (!_solutions.TryGetSolution(uid, "conditioning", out var tank, out var solution))
            return cooling;

        const string oxidizer = "IndustrialOxidizer";
        const string solvent = "IndustrialSolvent";
        core.Oxidizer = solution.GetTotalPrototypeQuantity(oxidizer).Float();
        core.Solvent = solution.GetTotalPrototypeQuantity(solvent).Float();
        var oxidizerDraw = FixedPoint2.New(1.6f * dt);
        var solventDraw = FixedPoint2.New(.8f * dt);
        if (core.Scrammed || AtTripTemperature(core) || moxShare <= 0 || HeatFraction(core) <= MoxBoostStart ||
            core.AuxiliaryCooling < 150000 || core.Oxidizer < oxidizerDraw.Float() || core.Solvent < solventDraw.Float())
            return cooling;

        core.MoxBoost = true;
        core.Oxidizer -= _solutions.RemoveReagent(tank.Value, oxidizer, oxidizerDraw).Float();
        core.Solvent -= _solutions.RemoveReagent(tank.Value, solvent, solventDraw).Float();
        return cooling;
    }
}
