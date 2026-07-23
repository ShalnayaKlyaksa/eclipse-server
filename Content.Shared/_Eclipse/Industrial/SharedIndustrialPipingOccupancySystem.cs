using Content.Shared.Construction.Components;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>
/// Prevents anchoring structures on tiles occupied by industrial piping,
/// and prevents piping from sharing tiles with other structures.
/// </summary>
public sealed class SharedIndustrialPipingOccupancySystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AnchorableComponent, AnchorAttemptEvent>(OnAnchorAttempt);
        SubscribeLocalEvent<LiquidPipeComponent, AnchorAttemptEvent>(OnLiquidPipeAnchorAttempt);
    }

    private void OnAnchorAttempt(Entity<AnchorableComponent> ent, ref AnchorAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (HasComp<IndustrialConfiguratorComponent>(args.Tool))
        {
            args.Cancel();
            return;
        }

        if (HasComp<ItemPipeComponent>(ent) || HasComp<LiquidPipeComponent>(ent))
            return;

        if (!TryGetTileIndices(ent, out var gridUid, out var grid, out var indices) || grid == null)
            return;

        if (!IndustrialPipingOccupancyHelper.TileContainsPiping(gridUid, grid, indices, EntityManager, _map))
            return;

        _popup.PopupClient(Loc.GetString("industrial-piping-tile-occupied"), ent, args.User);
        args.Cancel();
    }

    private void OnLiquidPipeAnchorAttempt(Entity<LiquidPipeComponent> ent, ref AnchorAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryGetTileIndices(ent, out var gridUid, out var grid, out var indices) || grid == null)
            return;

        if (!IndustrialPipingOccupancyHelper.TileContainsPiping(gridUid, grid, indices, EntityManager, _map, ent))
            return;

        _popup.PopupClient(Loc.GetString("industrial-piping-tile-occupied"), ent, args.User);
        args.Cancel();
    }

    private bool TryGetTileIndices(
        EntityUid uid,
        out EntityUid gridUid,
        out MapGridComponent? grid,
        out Vector2i indices)
    {
        gridUid = default;
        grid = null;
        indices = default;

        var xform = Transform(uid);
        if (xform.GridUid is not { } gridEnt || !TryComp(gridEnt, out grid))
            return false;

        gridUid = gridEnt;
        indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        return true;
    }
}
