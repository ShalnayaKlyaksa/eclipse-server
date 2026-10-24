using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.Nodes;
using Content.Shared.NodeContainer;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.ProtoCore;

[DataDefinition]
public sealed partial class ProtoCoreCableNode : CableNode, ICableConnectionFilter
{
    [DataField]
    public HashSet<EntProtoId> AllowedCablePrototypes = new()
    {
        "ProtoCoreCable",
        "ProtoCoreCableUncuttable",
    };

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

        var terminalDirs = 0;
        List<(Direction, Node)> nodeDirs = new();

        foreach (var (dir, node) in NodeHelpers.GetCardinalNeighborNodes(nodeQuery, gridEnt, gridIndex, mapSystem))
        {
            if (node is CableNode)
            {
                if (node != this && IsAllowedCable(node.Owner, entMan))
                    nodeDirs.Add((dir, node));
            }

            if (node is IDirectionalCableNode directionalNode
                && dir != Direction.Invalid
                && directionalNode.ConnectsToCable(Owner, dir.GetOpposite(), xformQuery.GetComponent(node.Owner), entMan))
            {
                nodeDirs.Add((dir, node));
            }

            if (node is CableDeviceNode && dir == Direction.Invalid)
            {
                if (node is ICableConnectionFilter filter && !filter.AllowsCable(Owner, entMan))
                    continue;

                nodeDirs.Add((Direction.Invalid, node));
            }

            if (node is CableTerminalNode)
            {
                if (dir == Direction.Invalid)
                {
                    terminalDirs |= 1 << (int) xformQuery.GetComponent(node.Owner).LocalRotation.GetCardinalDir();
                }
                else
                {
                    var terminalDir = xformQuery.GetComponent(node.Owner).LocalRotation.GetCardinalDir();
                    if (terminalDir.GetOpposite() == dir)
                        terminalDirs |= 1 << (int) dir;
                }
            }
        }

        foreach (var (dir, node) in nodeDirs)
        {
            if (dir != Direction.Invalid && (terminalDirs & (1 << (int) dir)) != 0)
                continue;

            yield return node;
        }
    }

    public bool AllowsCable(EntityUid cable, IEntityManager entMan)
    {
        return IsAllowedCable(cable, entMan);
    }

    private bool IsAllowedCable(EntityUid cable, IEntityManager entMan)
    {
        if (!entMan.TryGetComponent<CableComponent>(cable, out _))
            return false;

        var prototype = entMan.GetComponent<MetaDataComponent>(cable).EntityPrototype?.ID;
        return prototype != null && AllowedCablePrototypes.Contains(prototype);
    }
}
