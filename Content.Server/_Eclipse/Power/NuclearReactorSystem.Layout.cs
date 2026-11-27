using System.Linq;
using Content.Shared._Eclipse.Power;
using Robust.Shared.Map.Components;
using Offset = (int X, int Y);

namespace Content.Server._Eclipse.Power;

public sealed partial class NuclearReactorSystem
{
    private const int LayoutRadius = 6;
    private static readonly Offset[] Corners = [(1, 1), (1, -1), (-1, 1), (-1, -1)];

    public void ScanLayout(Entity<NuclearReactorComponent> ent)
    {
        var core = ent.Comp;
        foreach (var uid in core.AssociatedSections)
            if (TryComp<NuclearReactorPartComponent>(uid, out var old) && old.Core == ent.Owner)
                old.Core = null;

        core.AssociatedSections.Clear();
        core.Parts.Clear();
        core.Footprint.Clear();
        core.Inlet = core.Outlet = core.Conditioner = null;
        core.WaterCooling = core.SteamCooling = core.Complete = core.HullClosed = core.PortsReady = false;
        core.MoxCells = core.LiveCells = core.FuelHatches = core.GraphiteSections = core.ControlSections = 0;
        core.Left = core.Top = core.Width = core.Height = 0;
        core.LayoutAge = 0;
        core.Faults = 0;
        core.Cells = [];
        for (var group = 0; group < core.RodGroups.Length; group++)
            core.RodGroups[group] = core.RodGroups[group] with { Count = 0 };

        var xform = Transform(ent);
        if (!xform.Anchored || xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            core.Faults = 1;
            return;
        }

        var origin = _map.LocalToTile(gridUid, grid, xform.Coordinates);
        var sections = new Dictionary<Offset, (EntityUid Uid, NuclearPartKind Kind)>();
        var visited = new HashSet<Offset>();
        var pending = new Queue<Offset>();
        pending.Enqueue((0, 0));
        var oversized = false;
        while (pending.TryDequeue(out var tile))
        {
            if (!visited.Add(tile))
                continue;
            var section = SectionAt(gridUid, grid, origin + new Vector2i(tile.X, tile.Y));
            if (section is not { } found)
                continue;
            if (Math.Abs(tile.X) > LayoutRadius || Math.Abs(tile.Y) > LayoutRadius)
            {
                oversized = true;
                continue;
            }
            sections.Add(tile, found);
            foreach (var offset in Neighbours)
                pending.Enqueue((tile.X + offset.X, tile.Y + offset.Y));
        }

        if (sections.Count == 0)
        {
            core.Faults = 1;
            return;
        }

        var left = sections.Keys.Min(tile => tile.X);
        var right = sections.Keys.Max(tile => tile.X);
        var bottom = sections.Keys.Min(tile => tile.Y);
        var top = sections.Keys.Max(tile => tile.Y);
        var outside = new HashSet<Offset>();
        pending.Enqueue((left - 1, bottom - 1));
        while (pending.TryDequeue(out var tile))
        {
            if (tile.X < left - 1 || tile.X > right + 1 || tile.Y < bottom - 1 || tile.Y > top + 1 ||
                sections.TryGetValue(tile, out var part) && IsHull(part.Kind) || !outside.Add(tile))
                continue;
            foreach (var offset in Neighbours)
                pending.Enqueue((tile.X + offset.X, tile.Y + offset.Y));
        }

        bool Interior(Offset tile) => !outside.Contains(tile) &&
            (!sections.TryGetValue(tile, out var part) || !IsHull(part.Kind));
        var exposedCorners = new HashSet<Offset>();
        for (var y = bottom; y <= top; y++)
        for (var x = left; x <= right; x++)
        {
            if (!Interior((x, y)))
                continue;
            foreach (var (dx, dy) in Corners)
                if (outside.Contains((x + dx, y + dy)))
                    exposedCorners.Add((x + dx, y + dy));
        }

        core.HullClosed = Interior((0, 0)) && exposedCorners.Count == 0;
        var singleCore = sections.Values.Count(section => section.Kind == NuclearPartKind.Core) == 1;
        if (!core.HullClosed || oversized || !singleCore)
            core.Faults++;
        core.Left = left;
        core.Top = top;
        core.Width = right - left + 1;
        core.Height = top - bottom + 1;
        core.Cells = new NuclearCellState[core.Width * core.Height];
        var inletCount = 0;
        var outletCount = 0;
        var portsAligned = true;
        EntityUid? conditioner = null;
        var conditioners = 0;
        for (var y = top; y >= bottom; y--)
        for (var x = left; x <= right; x++)
        {
            var tile = (x, y);
            var interior = Interior(tile);
            var hasPart = sections.TryGetValue(tile, out var part);
            var kind = hasPart ? part.Kind : interior || exposedCorners.Contains(tile)
                ? NuclearPartKind.Missing : NuclearPartKind.Outside;
            var fuelKind = NuclearFuelKind.None;
            var fuel = 0f;
            var rodGroup = -1;
            if (kind != NuclearPartKind.Outside)
                core.Footprint.Add(tile);

            // Keep the installed coolant ports available for decay cooling even after a breach.
            var valid = kind switch
            {
                NuclearPartKind.Core => interior && part.Uid == ent.Owner,
                NuclearPartKind.Fuel or NuclearPartKind.Graphite or NuclearPartKind.Control or NuclearPartKind.HeatSink => interior,
                NuclearPartKind.Inlet => Interior((x + 1, y)) && outside.Contains((x - 1, y)),
                NuclearPartKind.Outlet => Interior((x - 1, y)) && outside.Contains((x + 1, y)),
                NuclearPartKind.Shield or NuclearPartKind.Outside => true,
                _ => false,
            };
            switch (kind)
            {
                case NuclearPartKind.Inlet:
                    core.Inlet = ++inletCount == 1 ? part.Uid : null;
                    portsAligned &= valid;
                    break;
                case NuclearPartKind.Outlet:
                    core.Outlet = ++outletCount == 1 ? part.Uid : null;
                    portsAligned &= valid;
                    break;
                case NuclearPartKind.Shield:
                    if (outside.Contains((x, y - 1)) && (Interior((x, y + 1)) || !core.HullClosed))
                    {
                        foreach (var module in _map.GetAnchoredEntities(gridUid, grid, origin + new Vector2i(x, y - 1)))
                            if (HasComp<NuclearReactorConditionerComponent>(module))
                            {
                                conditioner = module;
                                conditioners++;
                            }
                    }
                    break;
            }

            if (hasPart && TryComp<NuclearReactorPartComponent>(part.Uid, out var component) &&
                kind != NuclearPartKind.Conflict)
            {
                if (singleCore && component.Core == null)
                {
                    component.Core = ent.Owner;
                    core.AssociatedSections.Add(part.Uid);
                }
                if (component.Core == ent.Owner)
                    core.Parts[tile] = part.Uid;
                else
                    valid = false;
                if (kind == NuclearPartKind.Fuel)
                {
                    core.FuelHatches++;
                    if (ReadFuel(part.Uid) is { } cell)
                    {
                        fuel = FuelRemaining(cell);
                        fuelKind = cell.Kind;
                        if (cell.Kind == NuclearFuelKind.Mox) core.MoxCells++;
                        if (cell.Energy > 0) core.LiveCells++;
                    }
                }
                if (kind == NuclearPartKind.Graphite) core.GraphiteSections++;
                if (kind == NuclearPartKind.Control)
                {
                    core.ControlSections++;
                    rodGroup = Math.Clamp(component.RodGroup, 0, core.RodGroups.Length - 1);
                    var bank = core.RodGroups[rodGroup];
                    core.RodGroups[rodGroup] = bank with { Count = bank.Count + 1 };
                }
            }

            if (!valid)
                core.Faults++;
            core.Cells[CellIndex(core, tile)] = new(kind, valid, fuel, rodGroup, fuelKind);
        }

        foreach (var tile in core.Parts.Keys)
        {
            var cell = core.Cells[CellIndex(core, tile)];
            if (cell.FuelKind == NuclearFuelKind.Pulse)
                cell.FuelNeighbours = CountLiveFuelNeighbours(core, tile);
        }

        if (core.FuelHatches < 4) core.Faults++;
        if (core.GraphiteSections < 4) core.Faults++;
        if (core.ControlSections < 4) core.Faults++;
        if (inletCount != 1) core.Faults++;
        if (outletCount != 1) core.Faults++;
        if (conditioners == 1)
            core.Conditioner = conditioner;
        var waterIn = HasComp<NuclearWaterCollectorComponent>(core.Inlet);
        var waterOut = HasComp<NuclearWaterCollectorComponent>(core.Outlet);
        core.PortsReady = portsAligned && inletCount == 1 && outletCount == 1 && waterIn == waterOut;
        core.WaterCooling = waterIn && waterOut;
        core.SteamCooling = core.WaterCooling && HasComp<NuclearSteamOutletComponent>(core.Outlet);
        if (waterIn != waterOut)
        {
            core.Faults++;
            foreach (var (tile, part) in sections)
                if (part.Kind is NuclearPartKind.Inlet or NuclearPartKind.Outlet)
                    core.Cells[CellIndex(core, tile)].Valid = false;
        }
        core.Complete = core.Faults == 0;
    }

    private (EntityUid Uid, NuclearPartKind Kind)? SectionAt(EntityUid gridUid, MapGridComponent grid, Vector2i position)
    {
        (EntityUid Uid, NuclearPartKind Kind)? found = null;
        foreach (var uid in _map.GetAnchoredEntities(gridUid, grid, position))
        {
            if (TerminatingOrDeleted(uid))
                continue;
            var kind = HasComp<NuclearReactorComponent>(uid) ? NuclearPartKind.Core
                : TryComp<NuclearReactorPartComponent>(uid, out var part) ? part.Kind : NuclearPartKind.Outside;
            if (kind is NuclearPartKind.Outside or NuclearPartKind.Console)
                continue;
            if (found != null)
                return (uid, NuclearPartKind.Conflict);
            found = (uid, kind);
        }
        return found;
    }

    private static bool IsHull(NuclearPartKind kind) =>
        kind is NuclearPartKind.Shield or NuclearPartKind.Inlet or NuclearPartKind.Outlet;

    private static int CellIndex(NuclearReactorComponent core, Offset tile) =>
        (core.Top - tile.Y) * core.Width + tile.X - core.Left;
}
