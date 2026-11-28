using Content.Server.NodeContainer.EntitySystems;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Atmos;
using Robust.Server.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.Server._Eclipse.Industrial;

public sealed partial class IndustrialHeatPipeSystem : EntitySystem
{
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedIndustrialHeatConnectSystem _connections = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<IndustrialHeatPipeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<IndustrialHeatPipeComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<IndustrialHeatPipeComponent, NodeGroupsRebuilt>(OnTopologyChanged);
    }

    private void OnMapInit(Entity<IndustrialHeatPipeComponent> ent, ref MapInitEvent args)
    {
        _connections.TryAutoBindAdjacentProcessors(ent);
        UpdateAppearance(ent);
    }

    private void OnAnchorChanged(Entity<IndustrialHeatPipeComponent> ent, ref AnchorStateChangedEvent args)
    {
        _connections.TryAutoBindAdjacentProcessors(ent);
        UpdateAppearance(ent);
        UpdateNeighbors(ent);
    }

    private void OnTopologyChanged(Entity<IndustrialHeatPipeComponent> ent, ref NodeGroupsRebuilt args)
        => UpdateAppearance(ent);

    public void UpdateNeighbors(EntityUid source)
    {
        var xform = Transform(source);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var origin = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        foreach (var direction in SharedIndustrialProcessorSystem.CardinalDirections)
        {
            var neighbors = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, origin.Offset(direction));
            while (neighbors.MoveNext(out var neighbor))
            {
                if (neighbor is { } pipe && HasComp<IndustrialHeatPipeComponent>(pipe))
                    UpdateAppearance(pipe);
            }
        }
    }

    private void UpdateAppearance(EntityUid pipe)
    {
        var directions = PipeDirection.None;
        var xform = Transform(pipe);
        if (xform.Anchored && xform.GridUid is { } gridUid && TryComp<MapGridComponent>(gridUid, out var grid))
        {
            var origin = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            foreach (var direction in SharedIndustrialProcessorSystem.CardinalDirections)
            {
                var neighbors = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, origin.Offset(direction));
                while (neighbors.MoveNext(out var neighbor))
                {
                    if (TerminatingOrDeleted(neighbor))
                        continue;

                    if (!HasComp<IndustrialHeatBufferComponent>(neighbor) &&
                        !HasComp<IndustrialHeatPipeComponent>(neighbor) &&
                        !(TryComp<IndustrialProcessorComponent>(neighbor, out var processor) &&
                          processor.GetFacePort(direction.GetOpposite()) == FacePortState.HeatInput))
                    {
                        continue;
                    }

                    directions |= direction.ToPipeDirection();
                    break;
                }
            }
        }

        _appearance.SetData(pipe, IndustrialHeatPipeVisuals.Connections, (int) directions);
    }
}
