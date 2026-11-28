using Content.Server.Fluids.EntitySystems;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.Industrial;

[RegisterComponent]
public sealed partial class IndustrialTankSectionComponent : Component;

[RegisterComponent]
public sealed partial class IndustrialBulkTankComponent : Component
{
    [DataField(required: true)]
    public int Side;

    [DataField]
    public List<EntityUid> Supports = new();
}

[RegisterComponent]
public sealed partial class IndustrialTankSupportComponent : Component
{
    [DataField]
    public EntityUid? Tank;
}

public sealed partial class IndustrialBulkTankSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedToolSystem _tools = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly PuddleSystem _puddles = default!;

    private static readonly EntProtoId Section = "IndustrialTankSection";
    private static readonly EntProtoId Support = "IndustrialTankSupport";
    private static readonly EntProtoId Barrel = "IndustrialBulkBarrel";
    private static readonly EntProtoId Reservoir = "IndustrialBulkReservoir";
    private const string TankSolution = "tank";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IndustrialTankSectionComponent, GetVerbsEvent<AlternativeVerb>>(OnSectionVerbs);
        SubscribeLocalEvent<IndustrialBulkTankComponent, GetVerbsEvent<AlternativeVerb>>(OnTankVerbs);
        SubscribeLocalEvent<IndustrialBulkTankComponent, MapInitEvent>(OnTankMapInit);
        SubscribeLocalEvent<IndustrialBulkTankComponent, AnchorStateChangedEvent>(OnTankAnchorChanged);
        SubscribeLocalEvent<IndustrialBulkTankComponent, EntityTerminatingEvent>(OnTankTerminating);
        SubscribeLocalEvent<IndustrialTankSupportComponent, AnchorStateChangedEvent>(OnSupportAnchorChanged);
    }

    private void OnSectionVerbs(Entity<IndustrialTankSectionComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using is not { } tool || !_tools.HasQuality(tool, "Anchoring"))
            return;

        var corner = ent.Owner;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("industrial-tank-assemble-barrel"),
            Disabled = !TryCollectSections(corner, 2, out _),
            Act = () => TryAssemble(corner, 2),
        });
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("industrial-tank-assemble-reservoir"),
            Disabled = !TryCollectSections(corner, 3, out _),
            Act = () => TryAssemble(corner, 3),
        });
    }

    private void OnTankVerbs(Entity<IndustrialBulkTankComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using is not { } tool || !_tools.HasQuality(tool, "Anchoring"))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("industrial-tank-disassemble"),
            Act = () =>
            {
                if (!TryDisassemble(ent))
                    _popup.PopupEntity(Loc.GetString("industrial-tank-drain-first"), ent, user);
            },
        });
    }

    public bool TryAssemble(EntityUid corner, int side)
    {
        if (side is not (2 or 3) || !TryCollectSections(corner, side, out var sections))
            return false;

        Spawn(side == 2 ? Barrel : Reservoir, Transform(corner).Coordinates);
        foreach (var section in sections)
            Del(section);
        return true;
    }

    public bool TryDisassemble(Entity<IndustrialBulkTankComponent> ent)
    {
        if (EntityManager.IsQueuedForDeletion(ent) ||
            !_solutions.TryGetSolution(ent.Owner, TankSolution, out _, out var tank) || tank.Volume > 0 ||
            !TryGetOrigin(ent, out var grid, out var origin))
            return false;

        var side = ent.Comp.Side;
        Del(ent.Owner);
        foreach (var tile in Footprint(origin, side))
            Spawn(Section, _map.GridTileToLocal(grid.Owner, grid.Comp, tile));
        return true;
    }

    private bool TryCollectSections(EntityUid corner, int side, out List<EntityUid> sections)
    {
        sections = new List<EntityUid>(side * side);
        if (!HasComp<IndustrialTankSectionComponent>(corner) || !TryGetOrigin(corner, out var grid, out var origin))
            return false;

        foreach (var tile in Footprint(origin, side))
        {
            EntityUid? section = null;
            var entities = _map.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, tile);
            while (entities.MoveNext(out var entity) && entity is { } uid)
            {
                if (HasComp<IndustrialTankSectionComponent>(uid))
                {
                    if (section != null || TerminatingOrDeleted(uid))
                        return false;
                    section = uid;
                }
                else if (HasComp<IndustrialPipingOccupantComponent>(uid))
                {
                    return false;
                }
            }

            if (section is not { } required)
                return false;
            sections.Add(required);
        }
        return sections.Contains(corner);
    }

    private void OnTankMapInit(Entity<IndustrialBulkTankComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Supports.Count > 0 || !TryGetOrigin(ent, out var grid, out var origin))
            return;

        foreach (var tile in Footprint(origin, ent.Comp.Side))
        {
            if (tile == origin)
                continue;
            var support = Spawn(Support, _map.GridTileToLocal(grid.Owner, grid.Comp, tile));
            Comp<IndustrialTankSupportComponent>(support).Tank = ent.Owner;
            ent.Comp.Supports.Add(support);
        }
    }

    private void OnTankAnchorChanged(Entity<IndustrialBulkTankComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored && !args.Detaching)
            BreakTank(ent);
    }

    private void OnSupportAnchorChanged(Entity<IndustrialTankSupportComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored && !args.Detaching && ent.Comp.Tank is { } tank)
            BreakTank(tank);
    }

    private void BreakTank(EntityUid tank)
    {
        if (TerminatingOrDeleted(tank) || EntityManager.IsQueuedForDeletion(tank))
            return;
        if (_solutions.TryGetSolution(tank, TankSolution, out var solution, out var contents) && contents.Volume > 0)
            _puddles.TrySpillAt(tank, _solutions.SplitSolution(solution.Value, contents.Volume), out _);
        QueueDel(tank);
    }

    private void OnTankTerminating(Entity<IndustrialBulkTankComponent> ent, ref EntityTerminatingEvent args)
    {
        foreach (var support in ent.Comp.Supports)
            if (!TerminatingOrDeleted(support))
                Del(support);
    }

    private bool TryGetOrigin(EntityUid uid, out Entity<MapGridComponent> grid, out Vector2i origin)
    {
        grid = default;
        origin = default;
        var transform = Transform(uid);
        if (!transform.Anchored || transform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var component))
            return false;

        grid = (gridUid, component);
        origin = _map.TileIndicesFor(gridUid, component, transform.Coordinates);
        return true;
    }

    // The south-eastern tile is the origin so both pipes connect outside the footprint.
    private static IEnumerable<Vector2i> Footprint(Vector2i origin, int side)
    {
        for (var y = 0; y < side; y++)
        for (var x = 0; x < side; x++)
            yield return origin + new Vector2i(-x, y);
    }
}
