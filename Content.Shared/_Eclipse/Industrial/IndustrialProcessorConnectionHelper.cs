using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Shared._Eclipse.Industrial;

public static class IndustrialProcessorConnectionHelper
{
    public static bool TryFindItemPipeOnFace(
        EntityUid processor,
        Direction faceFromProcessorToPipe,
        IEntityManager ent,
        SharedMapSystem map,
        out EntityUid pipe)
    {
        pipe = default;

        if (!ent.TryGetComponent(processor, out TransformComponent? procXform) ||
            !procXform.Anchored ||
            procXform.GridUid is not EntityUid gridUid ||
            !ent.TryGetComponent(gridUid, out MapGridComponent? grid))
        {
            return false;
        }

        var origin = map.TileIndicesFor(gridUid, grid, procXform.Coordinates);
        var neighbor = origin.Offset(faceFromProcessorToPipe);

        var enumerator = map.GetAnchoredEntitiesEnumerator(gridUid, grid, neighbor);
        while (enumerator.MoveNext(out var entity) && entity != null)
        {
            if (!ent.HasComponent<ItemPipeComponent>(entity.Value))
                continue;

            if (!IsPipeOnProcessorFace(processor, entity.Value, faceFromProcessorToPipe, ent, map))
                continue;

            pipe = entity.Value;
            return true;
        }

        return false;
    }

    /// <summary>
    /// True when the pipe sits on the single grid tile directly adjacent to the processor face.
    /// </summary>
    public static bool IsPipeOnProcessorFace(
        EntityUid processor,
        EntityUid pipe,
        Direction faceFromProcessorToPipe,
        IEntityManager ent,
        SharedMapSystem map)
    {
        if (!ent.TryGetComponent(processor, out TransformComponent? procXform) ||
            !ent.TryGetComponent(pipe, out TransformComponent? pipeXform) ||
            !procXform.Anchored || !pipeXform.Anchored ||
            procXform.GridUid is not EntityUid gridUid ||
            pipeXform.GridUid != gridUid ||
            !ent.TryGetComponent(gridUid, out MapGridComponent? grid))
        {
            return false;
        }

        var procPos = map.TileIndicesFor(gridUid, grid, procXform.Coordinates);
        var pipePos = map.TileIndicesFor(gridUid, grid, pipeXform.Coordinates);

        return pipePos == procPos.Offset(faceFromProcessorToPipe);
    }

    /// <summary>
    /// GregTech-style: when a pipe is placed against a machine face, auto-enable item input on that face.
    /// </summary>
    public static bool TryAutoBindItemPort(
        EntityUid processor,
        EntityUid pipe,
        Direction faceFromProcessorToPipe,
        IEntityManager ent,
        SharedMapSystem map)
    {
        if (!ent.TryGetComponent(processor, out IndustrialProcessorComponent? processorComp))
            return false;

        if (!IsPipeOnProcessorFace(processor, pipe, faceFromProcessorToPipe, ent, map))
            return false;

        if (processorComp.GetItemPortMode(faceFromProcessorToPipe) != PortMode.Disabled)
            return false;

        processorComp.SetFacePort(faceFromProcessorToPipe, FacePortState.ItemInput);
        ent.Dirty(processor, processorComp);
        return true;
    }

    public static bool TryFindHeatBufferOnFace(
        EntityUid processor,
        Direction faceFromProcessorToBuffer,
        IEntityManager ent,
        SharedMapSystem map,
        out EntityUid buffer)
    {
        buffer = default;

        if (!ent.TryGetComponent(processor, out TransformComponent? procXform) ||
            !procXform.Anchored ||
            procXform.GridUid is not EntityUid gridUid ||
            !ent.TryGetComponent(gridUid, out MapGridComponent? grid))
        {
            return false;
        }

        var origin = map.TileIndicesFor(gridUid, grid, procXform.Coordinates);
        var neighbor = origin.Offset(faceFromProcessorToBuffer);

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

    public static bool IsBufferOnProcessorFace(
        EntityUid processor,
        EntityUid buffer,
        Direction faceFromProcessorToBuffer,
        IEntityManager ent,
        SharedMapSystem map)
    {
        if (!ent.TryGetComponent(processor, out TransformComponent? procXform) ||
            !ent.TryGetComponent(buffer, out TransformComponent? bufferXform) ||
            !procXform.Anchored || !bufferXform.Anchored ||
            procXform.GridUid is not EntityUid gridUid ||
            bufferXform.GridUid != gridUid ||
            !ent.TryGetComponent(gridUid, out MapGridComponent? grid))
        {
            return false;
        }

        var procPos = map.TileIndicesFor(gridUid, grid, procXform.Coordinates);
        var bufferPos = map.TileIndicesFor(gridUid, grid, bufferXform.Coordinates);

        return bufferPos == procPos.Offset(faceFromProcessorToBuffer);
    }

    /// <summary>
    /// Auto-enables heat input on machine faces adjacent to a heat exchange buffer.
    /// </summary>
    public static bool TryAutoBindHeatPort(
        EntityUid processor,
        EntityUid buffer,
        Direction faceFromProcessorToBuffer,
        IEntityManager ent,
        SharedMapSystem map)
    {
        if (!ent.TryGetComponent(processor, out IndustrialProcessorComponent? processorComp) ||
            !ent.HasComponent<IndustrialHeatPoweredComponent>(processor))
        {
            return false;
        }

        if (!IsBufferOnProcessorFace(processor, buffer, faceFromProcessorToBuffer, ent, map))
            return false;

        if (processorComp.GetFacePort(faceFromProcessorToBuffer) != FacePortState.Disabled)
            return false;

        processorComp.SetFacePort(faceFromProcessorToBuffer, FacePortState.HeatInput);
        ent.Dirty(processor, processorComp);
        return true;
    }
}
