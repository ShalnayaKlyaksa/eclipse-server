using System.Linq;
using Content.Server.Power.Components;
using Content.Shared._Eclipse.Power;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.UserInterface;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.Power;

public sealed partial class NuclearReactorSystem
{
    private static readonly ProtoId<SourcePortPrototype> ControlPort = "NuclearReactorControl";

    private bool ValidConsole(EntityUid console, EntityUid core)
    {
        if (TerminatingOrDeleted(console) || TerminatingOrDeleted(core) ||
            !TryComp<NuclearReactorPartComponent>(console, out var part) || part.Kind != NuclearPartKind.Console ||
            !TryComp<DeviceLinkSourceComponent>(console, out var source))
            return false;

        var consoleXform = Transform(console);
        var coreXform = Transform(core);
        if (!consoleXform.Anchored || !coreXform.Anchored ||
            coreXform.GridUid is not { } gridUid || gridUid != consoleXform.GridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid) ||
            !_transform.GetMapCoordinates(console).InRange(_transform.GetMapCoordinates(core), source.Range))
            return false;

        if (!TryComp<NuclearReactorComponent>(core, out var reactor))
            return false;

        var centre = _map.LocalToTile(gridUid, grid, consoleXform.Coordinates);
        var coreTile = _map.LocalToTile(gridUid, grid, coreXform.Coordinates);
        for (var x = -1; x <= 1; x++)
        {
            var tile = centre + new Vector2i(x, 0);
            var offset = tile - coreTile;
            if (reactor.Footprint.Contains((offset.X, offset.Y)) ||
                !_map.TryGetTileRef(gridUid, grid, tile, out var floor) || floor.Tile.IsEmpty)
                return false;
        }
        return true;
    }

    private EntityUid? FindConsole(EntityUid core)
    {
        if (!TryComp<DeviceLinkSinkComponent>(core, out var sink))
            return null;

        EntityUid? result = null;
        foreach (var console in sink.LinkedSources)
        {
            if (!ValidConsole(console, core))
                continue;
            var targets = _links.GetLinkedSinks(console, ControlPort);
            if (targets.Count != 1 || !targets.Contains(core))
                continue;
            if (result != null)
                return null;
            result = console;
        }

        return result;
    }

    private bool TryConsoleCore(Entity<NuclearReactorPartComponent> console, out Entity<NuclearReactorComponent> core)
    {
        core = default;
        if (console.Comp.Kind != NuclearPartKind.Console || !HasComp<DeviceLinkSourceComponent>(console))
            return false;

        var targets = _links.GetLinkedSinks(console.Owner, ControlPort);
        foreach (var uid in targets)
        {
            if (targets.Count != 1 || !TryComp<NuclearReactorComponent>(uid, out var reactor) ||
                FindConsole(uid) != console.Owner)
                return false;

            core = (uid, reactor);
            return true;
        }

        return false;
    }

    private void OnLinkAttempt(Entity<NuclearReactorPartComponent> console, ref LinkAttemptEvent args)
    {
        if (console.Comp.Kind != NuclearPartKind.Console)
            return;

        var target = args.Sink;
        if (TryComp<NuclearReactorComponent>(target, out var reactor))
            ScanLayout((target, reactor));
        if (!ValidConsole(console, target) ||
            _links.GetLinkedSinks(console.Owner, ControlPort).Any(uid => uid != target) ||
            FindConsole(target) is { } existing && existing != console.Owner)
            args.Cancel();
    }

    private bool ConsolePowered(EntityUid? console) =>
        TryComp<ApcPowerReceiverComponent>(console, out var power) && power.Powered;

    private static bool RodsSecured(NuclearReactorComponent core) =>
        core.Scrammed && core.RodGroups.All(group => group.Position >= .999f);

    private static void Scram(NuclearReactorComponent core, string status)
    {
        core.Scrammed = true;
        for (var i = 0; i < core.RodGroups.Length; i++)
            core.RodGroups[i] = core.RodGroups[i] with { Target = 1 };
        core.Status = core.Failed ? "nuclear-status-failed" : status;
    }

    private static bool CanReset(NuclearReactorComponent core) =>
        core.Complete && !core.Failed && RodsSecured(core) && core.Temperature < 600 && core.LiveCells > 0 &&
        core.HeavyWaterLevel >= NuclearReactorComponent.RequiredHeavyWater &&
        (core.SteamCooling ? core.SteamWaterLevel >= 1 && core.NoWaterFlowTime <= 0 : core.Flow >= .01f) &&
        (core.SteamCooling ? core.Pressure < core.MaximumPressure :
            core.WaterCooling || core.Pressure >= core.MinimumPressure && core.Pressure <= core.MaximumPressure);

    private void OnOpen(Entity<NuclearReactorPartComponent> console, ref BeforeActivatableUIOpenEvent args)
    {
        if (TryConsoleCore(console, out var core))
        {
            ScanLayout(core);
            ReadCoreCharge(core.Owner, core.Comp);
            Publish(console, core.Comp, ConsolePowered(console));
        }
        else if (console.Comp.Kind == NuclearPartKind.Console)
            _ui.SetUiState(console.Owner, NuclearReactorUiKey.Key, new NuclearReactorState());
    }

    private void OnCommand(Entity<NuclearReactorPartComponent> console, ref NuclearReactorCommandMessage args)
    {
        if (!TryConsoleCore(console, out var core))
            return;

        ScanLayout(core);
        if (!ValidConsole(console, core.Owner))
            return;
        var reactor = core.Comp;
        ReadCoreCharge(core.Owner, reactor);
        var powered = ConsolePowered(console);
        if (args.Command == NuclearCommand.Scram)
            Scram(reactor, "nuclear-status-scram");
        else if (powered && !reactor.Failed)
        {
            if (args.Command == NuclearCommand.Reset && CanReset(reactor))
            {
                reactor.Scrammed = false;
                reactor.Status = "nuclear-status-starting";
            }
            else if ((uint) args.Group < reactor.RodGroups.Length)
            {
                var group = reactor.RodGroups[args.Group];
                if (args.Command == NuclearCommand.Rods && reactor.Complete && !reactor.Scrammed &&
                    group.Count > 0 && float.IsFinite(args.Value))
                    reactor.RodGroups[args.Group] = group with { Target = Math.Clamp(args.Value, 0, 1) };
                else if (args.Command == NuclearCommand.AssignGroup && RodsSecured(reactor) &&
                         reactor.Parts.TryGetValue((args.CellX, args.CellY), out var rod) &&
                         TryComp<NuclearReactorPartComponent>(rod, out var part) && part.Kind == NuclearPartKind.Control)
                {
                    part.RodGroup = args.Group;
                    ScanLayout(core);
                }
            }
        }

        Publish(console, reactor, powered);
    }

    private void Publish(EntityUid console, NuclearReactorComponent core, bool powered)
    {
        _ui.SetUiState(console, NuclearReactorUiKey.Key, new NuclearReactorState
        {
            Cells = core.Cells,
            Left = core.Left,
            Top = core.Top,
            Width = core.Width,
            Height = core.Height,
            HullClosed = core.HullClosed,
            FuelHatches = core.FuelHatches,
            GraphiteSections = core.GraphiteSections,
            ControlSections = core.ControlSections,
            LiveCells = core.LiveCells,
            PortsReady = core.PortsReady,
            HeatFraction = HeatFraction(core),
            Status = core.Status,
            Powered = powered,
            Complete = core.Complete,
            Scrammed = core.Scrammed,
            Temperature = core.Temperature,
            Power = core.ThermalPower,
            Cooling = core.CoolingPower,
            Decay = core.DecayPower,
            Pressure = core.Pressure,
            Flow = core.Flow,
            WaterCooling = core.WaterCooling,
            SteamCooling = core.SteamCooling,
            SteamFlow = core.SteamFlow,
            SteamWaterLevel = core.SteamWaterLevel,
            HeavyWaterLevel = core.HeavyWaterLevel,
            RequiredHeavyWater = NuclearReactorComponent.RequiredHeavyWater,
            MoxCells = core.MoxCells,
            MoxHeat = core.MoxCells > 0 ? HeatFraction(core) : 0,
            Conditioner = core.Conditioner != null,
            MoxBoost = core.MoxBoost,
            AuxiliaryCooling = core.AuxiliaryCooling,
            CoolingContacts = core.CoolingContacts,
            Oxidizer = core.Oxidizer,
            Solvent = core.Solvent,
            InletTemperature = core.InletTemperature,
            OutletTemperature = core.OutletTemperature,
            RodGroups = (NuclearRodGroupState[]) core.RodGroups.Clone(),
            CanAssignGroups = powered && !core.Failed && RodsSecured(core),
            CanReset = powered && CanReset(core),
            Reactivity = core.Reactivity,
            Xenon = core.Xenon,
            Faults = core.Faults,
        });
    }
}
