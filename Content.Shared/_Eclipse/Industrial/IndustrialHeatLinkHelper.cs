using Content.Shared.Atmos;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._Eclipse.Industrial;

public static class IndustrialHeatLinkHelper
{
    public static bool HasHeatInputPort(IndustrialProcessorComponent processor, Direction faceFromProcessorToNeighbor)
    {
        return processor.GetFacePort(faceFromProcessorToNeighbor) == FacePortState.HeatInput;
    }

    public static bool TryGetHeatBufferNeighbor(
        EntityUid processor,
        Direction faceFromProcessorToNeighbor,
        IEntityManager ent,
        SharedMapSystem map,
        out EntityUid buffer)
    {
        buffer = default;

        if (!ent.TryGetComponent(processor, out TransformComponent? xform) ||
            !xform.Anchored ||
            xform.GridUid is not EntityUid gridUid ||
            !ent.TryGetComponent(gridUid, out MapGridComponent? grid))
        {
            return false;
        }

        var origin = map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var neighbor = origin.Offset(faceFromProcessorToNeighbor);

        var enumerator = map.GetAnchoredEntitiesEnumerator(gridUid, grid, neighbor);
        while (enumerator.MoveNext(out var entity) && entity != null)
        {
            if (!ent.HasComponent<IndustrialHeatBufferComponent>(entity.Value))
                continue;

            buffer = entity.Value;
            return true;
        }

        return false;
    }

    public static bool IsHeatLinked(
        EntityUid processor,
        EntityUid buffer,
        Direction faceFromProcessorToBuffer,
        IEntityManager ent,
        SharedMapSystem map)
    {
        if (!ent.TryGetComponent(processor, out IndustrialProcessorComponent? processorComp) ||
            !ent.TryGetComponent(processor, out TransformComponent? procXform) ||
            !ent.TryGetComponent(buffer, out TransformComponent? bufferXform) ||
            !procXform.Anchored || !bufferXform.Anchored ||
            procXform.GridUid is not EntityUid gridUid ||
            bufferXform.GridUid != gridUid ||
            !ent.TryGetComponent(gridUid, out MapGridComponent? grid))
        {
            return false;
        }

        if (!HasHeatInputPort(processorComp, faceFromProcessorToBuffer))
            return false;

        var procPos = map.TileIndicesFor(gridUid, grid, procXform.Coordinates);
        var bufferPos = map.TileIndicesFor(gridUid, grid, bufferXform.Coordinates);

        return bufferPos == procPos.Offset(faceFromProcessorToBuffer);
    }

    public static IEnumerable<(EntityUid Processor, Direction Face)> GetLinkedProcessors(
        EntityUid buffer,
        IEntityManager ent,
        SharedMapSystem map)
    {
        if (!ent.TryGetComponent(buffer, out TransformComponent? bufferXform) ||
            !bufferXform.Anchored ||
            bufferXform.GridUid is not EntityUid gridUid ||
            !ent.TryGetComponent(gridUid, out MapGridComponent? grid))
        {
            yield break;
        }

        var origin = map.TileIndicesFor(gridUid, grid, bufferXform.Coordinates);

        foreach (var direction in SharedIndustrialProcessorSystem.CardinalDirections)
        {
            var neighbor = origin.Offset(direction);
            var faceFromProcessor = direction.GetOpposite();

            var enumerator = map.GetAnchoredEntitiesEnumerator(gridUid, grid, neighbor);
            while (enumerator.MoveNext(out var entity) && entity != null)
            {
                if (!ent.TryGetComponent(entity.Value, out IndustrialProcessorComponent? processor))
                    continue;

                if (!HasHeatInputPort(processor, faceFromProcessor))
                    continue;

                yield return (entity.Value, faceFromProcessor);
            }
        }
    }
}
