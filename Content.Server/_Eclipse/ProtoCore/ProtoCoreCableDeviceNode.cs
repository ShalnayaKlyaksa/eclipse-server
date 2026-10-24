using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.Nodes;
using Content.Shared.NodeContainer;
using Robust.Shared.Prototypes;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Server._Eclipse.ProtoCore;

[DataDefinition]
public sealed partial class ProtoCoreCableDeviceNode : CableDeviceNode, ICableConnectionFilter
{
    [DataField]
    public int Radius = 1;

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
        var center = mapSystem.TileIndicesFor(gridEnt, xform.Comp.Coordinates);

        for (var x = -Radius; x <= Radius; x++)
        {
            for (var y = -Radius; y <= Radius; y++)
            {
                var tile = center + new Vector2i(x, y);
                foreach (var node in NodeHelpers.GetNodesInTile(nodeQuery, gridEnt, tile, mapSystem))
                {
                    if (node is (CableNode or ProtoCoreCableNode) && AllowsCable(node.Owner, entMan))
                        yield return node;
                }
            }
        }
    }

    public bool AllowsCable(EntityUid uid, IEntityManager entMan)
    {
        if (!entMan.TryGetComponent<CableComponent>(uid, out _))
            return false;

        var prototype = entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
        return prototype != null && AllowedCablePrototypes.Contains(prototype);
    }
}
