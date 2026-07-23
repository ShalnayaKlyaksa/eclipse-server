using Content.Shared.Atmos;
using Robust.Shared.Map.Components;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>
/// Auto-binds heat-input ports when heat buffers are placed against heat-powered machines.
/// </summary>
public sealed class SharedIndustrialHeatConnectSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;

    public bool TryAutoBindAdjacentProcessors(Entity<IndustrialHeatBufferComponent> buffer)
    {
        var xform = Transform(buffer);
        if (!xform.Anchored || xform.GridUid is not EntityUid gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return false;
        }

        var pos = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var bound = false;

        foreach (var direction in SharedIndustrialProcessorSystem.CardinalDirections)
        {
            var enumerator = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, pos.Offset(direction));
            while (enumerator.MoveNext(out var entity) && entity != null)
            {
                if (!TryComp<IndustrialProcessorComponent>(entity, out _))
                    continue;

                var face = direction.GetOpposite();
                if (!IndustrialProcessorConnectionHelper.TryAutoBindHeatPort(
                        entity.Value, buffer, face, EntityManager, _map))
                {
                    continue;
                }

                bound = true;
            }
        }

        return bound;
    }

    public bool TryAutoBindPortsFromAdjacentBuffers(Entity<IndustrialProcessorComponent> processor)
    {
        if (!HasComp<IndustrialHeatPoweredComponent>(processor))
            return false;

        var bound = false;

        foreach (var direction in SharedIndustrialProcessorSystem.CardinalDirections)
        {
            if (!IndustrialProcessorConnectionHelper.TryFindHeatBufferOnFace(
                    processor, direction, EntityManager, _map, out var buffer))
            {
                continue;
            }

            if (!IndustrialProcessorConnectionHelper.TryAutoBindHeatPort(
                    processor, buffer, direction, EntityManager, _map))
            {
                continue;
            }

            bound = true;
        }

        return bound;
    }
}
