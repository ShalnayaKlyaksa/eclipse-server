using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.Nodes;
using Content.Shared.NodeContainer;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.Power;

[DataDefinition]
[Virtual]
public partial class DirectionalCableDeviceNode : Node, IDirectionalCableNode
{
    [DataField]
    public Direction Direction = Direction.South;

    public bool ConnectsToCable(EntityUid cable, Direction direction, TransformComponent xform, IEntityManager entMan)
    {
        return GetWorldDirection(xform) == direction && IsCableAllowed(cable, entMan);
    }

    public override IEnumerable<Node> GetReachableNodes(
        Entity<TransformComponent> xform,
        EntityQuery<NodeContainerComponent> nodeQuery,
        EntityQuery<TransformComponent> xformQuery,
        Entity<MapGridComponent>? grid,
        IEntityManager entMan)
    {
        if (!xform.Comp.Anchored || grid is not { } gridEnt)
            yield break;

        var mapSystem = entMan.System<SharedMapSystem>();
        var gridIndex = mapSystem.TileIndicesFor(gridEnt, xform.Comp.Coordinates);
        var portDirection = GetWorldDirection(xform.Comp);
        var cableTile = gridIndex.Offset(portDirection);

        foreach (var node in NodeHelpers.GetNodesInTile(nodeQuery, gridEnt, cableTile, mapSystem))
        {
            if (node is not CableNode)
                continue;

            if (IsCableAllowed(node.Owner, entMan))
                yield return node;
        }
    }

    protected virtual bool IsCableAllowed(EntityUid cable, IEntityManager entMan)
    {
        return true;
    }

    private Direction GetWorldDirection(TransformComponent xform)
    {
        return new Angle(xform.LocalRotation.RotateVec(Direction.ToVec())).GetCardinalDir();
    }
}

[DataDefinition]
public sealed partial class FilteredCableDeviceNode : CableDeviceNode, ICableConnectionFilter
{
    [DataField]
    public int Radius = 1;

    [DataField]
    public HashSet<EntProtoId> AllowedCablePrototypes = new();

    public bool AllowsCable(EntityUid cable, IEntityManager entMan)
    {
        if (!entMan.TryGetComponent<CableComponent>(cable, out _))
            return false;

        var prototype = entMan.GetComponent<MetaDataComponent>(cable).EntityPrototype?.ID;
        return prototype != null && AllowedCablePrototypes.Contains(prototype);
    }

    public override IEnumerable<Node> GetReachableNodes(
        Entity<TransformComponent> xform,
        EntityQuery<NodeContainerComponent> nodeQuery,
        EntityQuery<TransformComponent> xformQuery,
        Entity<MapGridComponent>? grid,
        IEntityManager entMan)
    {
        if (!xform.Comp.Anchored || grid is not { } gridEnt)
            yield break;

        var mapSystem = entMan.System<SharedMapSystem>();
        var center = mapSystem.TileIndicesFor(gridEnt, xform.Comp.Coordinates);

        for (var x = -Radius; x <= Radius; x++)
        {
            for (var y = -Radius; y <= Radius; y++)
            {
                var tile = center + new Vector2i(x, y);
                foreach (var node in NodeHelpers.GetNodesInTile(nodeQuery, gridEnt, tile, mapSystem))
                {
                    if (node is CableNode && AllowsCable(node.Owner, entMan))
                        yield return node;
                }
            }
        }
    }
}

[DataDefinition]
public sealed partial class ProtoCoreDirectionalCableDeviceNode : DirectionalCableDeviceNode, ICableConnectionFilter
{
    [DataField]
    public HashSet<EntProtoId> AllowedCablePrototypes = new()
    {
        "ProtoCoreCable",
        "ProtoCoreCableUncuttable",
    };

    public bool AllowsCable(EntityUid cable, IEntityManager entMan)
    {
        if (!entMan.TryGetComponent<CableComponent>(cable, out _))
            return false;

        var prototype = entMan.GetComponent<MetaDataComponent>(cable).EntityPrototype?.ID;
        return prototype != null && AllowedCablePrototypes.Contains(prototype);
    }

    protected override bool IsCableAllowed(EntityUid cable, IEntityManager entMan)
    {
        return AllowsCable(cable, entMan);
    }
}
