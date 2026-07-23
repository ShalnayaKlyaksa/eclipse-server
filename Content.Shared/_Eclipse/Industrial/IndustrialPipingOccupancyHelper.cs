using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._Eclipse.Industrial;

public static class IndustrialPipingOccupancyHelper
{
    public static bool TileContainsPiping(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i indices,
        IEntityManager ent,
        SharedMapSystem map,
        EntityUid? ignore = null)
    {
        var enumerator = map.GetAnchoredEntitiesEnumerator(gridUid, grid, indices);
        while (enumerator.MoveNext(out var entity))
        {
            if (entity == ignore)
                continue;

            if (ent.HasComponent<IndustrialPipingOccupantComponent>(entity))
                return true;
        }

        return false;
    }

    public static bool TileContainsBlockingNonPipe(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i indices,
        IEntityManager ent,
        SharedMapSystem map,
        EntityUid? ignore = null)
    {
        var enumerator = map.GetAnchoredEntitiesEnumerator(gridUid, grid, indices);
        while (enumerator.MoveNext(out var entity))
        {
            if (entity == ignore)
                continue;

            if (ent.HasComponent<ItemPipeComponent>(entity) || ent.HasComponent<LiquidPipeComponent>(entity))
                continue;

            return true;
        }

        return false;
    }

    public static bool TileBlocksItemPipePlacement(
        EntityUid pipe,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i indices,
        IEntityManager ent,
        SharedMapSystem map)
    {
        var enumerator = map.GetAnchoredEntitiesEnumerator(gridUid, grid, indices);
        while (enumerator.MoveNext(out var other))
        {
            if (other == pipe)
                continue;

            if (ent.HasComponent<ItemPipeComponent>(other))
                continue;

            return true;
        }

        return false;
    }

    public static bool TileContainsIndustrialProcessor(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i indices,
        IEntityManager ent,
        SharedMapSystem map,
        EntityUid? ignore = null)
    {
        var enumerator = map.GetAnchoredEntitiesEnumerator(gridUid, grid, indices);
        while (enumerator.MoveNext(out var entity))
        {
            if (entity == ignore)
                continue;

            if (ent.HasComponent<IndustrialProcessorComponent>(entity))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Pipes must sit on adjacent tiles only — never inside the machine body footprint.
    /// </summary>
    public static bool PipeOverlapsProcessorInterior(
        EntityUid pipe,
        EntityUid processor,
        IEntityManager ent,
        SharedTransformSystem transform)
    {
        if (!ent.TryGetComponent(pipe, out TransformComponent? pipeXform) ||
            !ent.TryGetComponent(processor, out TransformComponent? _))
        {
            return false;
        }

        var pipeMap = transform.ToMapCoordinates(pipeXform.Coordinates).Position;
        var local = Vector2.Transform(pipeMap, transform.GetInvWorldMatrix(processor));
        var half = IndustrialPortClickZones.GetHalfExtents(processor, ent);

        return MathF.Abs(local.X) < half.X && MathF.Abs(local.Y) < half.Y;
    }

    public static bool IsPipeBlockedByProcessor(
        EntityUid pipe,
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i indices,
        IEntityManager ent,
        SharedMapSystem map,
        SharedTransformSystem transform)
    {
        if (TileContainsIndustrialProcessor(gridUid, grid, indices, ent, map, pipe))
            return true;

        foreach (var direction in ItemPipeConnectionHelper.CardinalDirections)
        {
            var enumerator = map.GetAnchoredEntitiesEnumerator(gridUid, grid, indices.Offset(direction));
            while (enumerator.MoveNext(out var entity) && entity != null)
            {
                if (!ent.HasComponent<IndustrialProcessorComponent>(entity.Value))
                    continue;

                if (PipeOverlapsProcessorInterior(pipe, entity.Value, ent, transform))
                    return true;
            }
        }

        return false;
    }
}
