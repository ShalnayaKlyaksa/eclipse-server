using Content.Shared.Atmos;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._Eclipse.Industrial;

public static class IndustrialHeatLinkHelper
{
    public static IEnumerable<(Entity<IndustrialProcessorComponent> Processor, Direction Face)> GetAdjacentProcessors(
        EntityUid source,
        IEntityManager ent,
        SharedMapSystem map)
    {
        if (!ent.TryGetComponent(source, out TransformComponent? xform) ||
            !xform.Anchored ||
            xform.GridUid is not EntityUid gridUid ||
            !ent.TryGetComponent(gridUid, out MapGridComponent? grid))
        {
            yield break;
        }

        var origin = map.TileIndicesFor(gridUid, grid, xform.Coordinates);

        foreach (var direction in SharedIndustrialProcessorSystem.CardinalDirections)
        {
            var neighbor = origin.Offset(direction);
            var faceFromProcessor = direction.GetOpposite();

            var enumerator = map.GetAnchoredEntitiesEnumerator(gridUid, grid, neighbor);
            while (enumerator.MoveNext(out var entity) && entity != null)
            {
                if (!ent.HasComponent<IndustrialHeatPoweredComponent>(entity.Value) ||
                    !ent.TryGetComponent(entity.Value, out IndustrialProcessorComponent? processor))
                    continue;

                yield return ((entity.Value, processor), faceFromProcessor);
            }
        }
    }
}
