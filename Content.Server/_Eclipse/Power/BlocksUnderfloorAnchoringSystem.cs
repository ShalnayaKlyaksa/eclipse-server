using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Shared.Construction.Components;
using Content.Shared.NodeContainer;
using Robust.Shared.Map.Components;

namespace Content.Server._Eclipse.Power;

public sealed partial class BlocksUnderfloorAnchoringSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _map = default!;

    private EntityQuery<BlocksUnderfloorAnchoringComponent> _blockerQuery;
    private EntityQuery<CableComponent> _cableQuery;
    private EntityQuery<NodeContainerComponent> _nodeQuery;

    public override void Initialize()
    {
        base.Initialize();

        _blockerQuery = GetEntityQuery<BlocksUnderfloorAnchoringComponent>();
        _cableQuery = GetEntityQuery<CableComponent>();
        _nodeQuery = GetEntityQuery<NodeContainerComponent>();

        SubscribeLocalEvent<BlocksUnderfloorAnchoringComponent, AnchorAttemptEvent>(OnBlockerAnchorAttempt);
        SubscribeLocalEvent<CableComponent, AnchorAttemptEvent>(OnCableAnchorAttempt);
        SubscribeLocalEvent<NodeContainerComponent, AnchorAttemptEvent>(OnNodeContainerAnchorAttempt);
    }

    private void OnBlockerAnchorAttempt(Entity<BlocksUnderfloorAnchoringComponent> ent, ref AnchorAttemptEvent args)
    {
        if (args.Cancelled || !TryGetTile(ent.Owner, out var grid, out var gridComp, out var tile))
            return;

        var enumerator = _map.GetAnchoredEntitiesEnumerator(grid, gridComp, tile);
        while (enumerator.MoveNext(out var other))
        {
            if (other is not { } otherUid || otherUid == ent.Owner)
                continue;

            if (IsUnderfloorBlocked(otherUid))
            {
                args.Cancel();
                return;
            }
        }
    }

    private void OnCableAnchorAttempt(Entity<CableComponent> ent, ref AnchorAttemptEvent args)
    {
        if (!args.Cancelled && HasBlockerOnTile(ent.Owner))
            args.Cancel();
    }

    private void OnNodeContainerAnchorAttempt(Entity<NodeContainerComponent> ent, ref AnchorAttemptEvent args)
    {
        if (!args.Cancelled && HasPipeNode(ent.Comp) && HasBlockerOnTile(ent.Owner))
            args.Cancel();
    }

    private bool HasBlockerOnTile(EntityUid uid)
    {
        if (!TryGetTile(uid, out var grid, out var gridComp, out var tile))
            return false;

        var enumerator = _map.GetAnchoredEntitiesEnumerator(grid, gridComp, tile);
        while (enumerator.MoveNext(out var other))
        {
            if (other is { } otherUid && otherUid != uid && _blockerQuery.HasComp(otherUid))
                return true;
        }

        return false;
    }

    private bool IsUnderfloorBlocked(EntityUid uid)
    {
        if (_cableQuery.HasComp(uid))
            return true;

        if (!_nodeQuery.TryComp(uid, out var nodeContainer))
            return false;

        foreach (var node in nodeContainer.Nodes.Values)
        {
            if (node is PipeNode)
                return true;
        }

        return false;
    }

    private static bool HasPipeNode(NodeContainerComponent nodeContainer)
    {
        foreach (var node in nodeContainer.Nodes.Values)
        {
            if (node is PipeNode)
                return true;
        }

        return false;
    }

    private bool TryGetTile(EntityUid uid, out EntityUid grid, out MapGridComponent gridComp, out Vector2i tile)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var foundGridComp))
        {
            grid = default;
            gridComp = default!;
            tile = default;
            return false;
        }

        grid = gridUid;
        gridComp = foundGridComp;
        tile = _map.TileIndicesFor(gridUid, gridComp, xform.Coordinates);
        return true;
    }
}
