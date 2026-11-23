using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Shared._Eclipse.Industrial;

public static class ItemPipeConnectionHelper
{
    /// <summary>Masks point from the pipe towards the machine; input/output refer to the machine.</summary>
    public static (PipeDirection Inputs, PipeDirection Outputs) GetMachineDirections(
        EntityUid pipe, IEntityManager ent, SharedMapSystem map)
    {
        var inputs = PipeDirection.None;
        var outputs = PipeDirection.None;
        if (!ent.TryGetComponent(pipe, out TransformComponent? xform) || !xform.Anchored ||
            xform.GridUid is not { } gridUid || !ent.TryGetComponent(gridUid, out MapGridComponent? grid))
            return (inputs, outputs);
        var pos = map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        foreach (var direction in CardinalDirections)
        {
            var enumerator = map.GetAnchoredEntitiesEnumerator(gridUid, grid, pos.Offset(direction));
            while (enumerator.MoveNext(out var uid) && uid != null)
            {
                if (!ent.TryGetComponent(uid, out IndustrialProcessorComponent? processor) ||
                    !IsProcessorConnectedToPipe(uid.Value, pipe, direction.GetOpposite(), ent))
                    continue;
                if (processor.GetItemPortMode(direction.GetOpposite()) == PortMode.Input)
                    inputs |= direction.ToPipeDirection();
                else
                    outputs |= direction.ToPipeDirection();
            }
        }
        return (inputs, outputs);
    }

    public static readonly Direction[] CardinalDirections =
        [Direction.North, Direction.South, Direction.East, Direction.West];

    public static PipeDirection GetCurrentMask(EntityUid uid, ItemPipeComponent pipe, TransformComponent xform, IEntityManager ent)
    {
        if (ItemPipeVisualHelper.UsesWireConnect(uid, ent))
            return PipeDirection.Fourway;

        return pipe.OriginalPipeDirection.RotatePipeDirection(xform.LocalRotation);
    }

    public static ItemPipeLayer GetLayer(EntityUid uid, IEntityManager ent)
    {
        if (ent.TryGetComponent<ItemPipeLayersComponent>(uid, out var layers))
            return layers.CurrentPipeLayer;

        return ItemPipeLayer.Primary;
    }

    public static bool MasksOverlap(PipeDirection a, PipeDirection b)
    {
        return (a & b) != PipeDirection.None;
    }

    public static bool ArePipesConnected(
        EntityUid pipeA,
        EntityUid pipeB,
        Direction directionFromAToB,
        IEntityManager ent,
        SharedMapSystem map)
    {
        if (!ent.TryGetComponent(pipeA, out ItemPipeComponent? compA) ||
            !ent.TryGetComponent(pipeB, out ItemPipeComponent? compB) ||
            !ent.TryGetComponent(pipeA, out TransformComponent? xformA) ||
            !ent.TryGetComponent(pipeB, out TransformComponent? xformB))
        {
            return false;
        }

        if (!xformA.Anchored || !xformB.Anchored ||
            ent.GetComponent<MetaDataComponent>(pipeA).EntityLifeStage >= EntityLifeStage.Terminating ||
            ent.GetComponent<MetaDataComponent>(pipeB).EntityLifeStage >= EntityLifeStage.Terminating)
            return false;

        if (GetLayer(pipeA, ent) != GetLayer(pipeB, ent))
            return false;

        if (compA.Tier != compB.Tier)
            return false;

        if (ItemPipeVisualHelper.UsesWireConnect(pipeA, ent) && ItemPipeVisualHelper.UsesWireConnect(pipeB, ent))
            return true;

        var maskA = GetCurrentMask(pipeA, compA, xformA, ent);
        var maskB = GetCurrentMask(pipeB, compB, xformB, ent);
        var dirMask = directionFromAToB.ToPipeDirection();
        var oppositeMask = directionFromAToB.GetOpposite().ToPipeDirection();

        return maskA.HasDirection(dirMask) && maskB.HasDirection(oppositeMask);
    }

    public static bool IsProcessorConnectedToPipe(
        EntityUid processor,
        EntityUid pipe,
        Direction directionFromProcessorToPipe,
        IEntityManager ent)
    {
        if (!ent.TryGetComponent(processor, out IndustrialProcessorComponent? processorComp) ||
            !ent.TryGetComponent(processor, out TransformComponent? procXform) ||
            !ent.TryGetComponent(pipe, out ItemPipeComponent? pipeComp) ||
            !ent.TryGetComponent(pipe, out TransformComponent? pipeXform))
        {
            return false;
        }

        if (!procXform.Anchored || !pipeXform.Anchored ||
            ent.GetComponent<MetaDataComponent>(processor).EntityLifeStage >= EntityLifeStage.Terminating ||
            ent.GetComponent<MetaDataComponent>(pipe).EntityLifeStage >= EntityLifeStage.Terminating)
            return false;

        if (processorComp.GetItemPortMode(directionFromProcessorToPipe) == PortMode.Disabled)
            return false;

        if (ItemPipeVisualHelper.UsesWireConnect(pipe, ent))
            return true;

        var pipeMask = GetCurrentMask(pipe, pipeComp, pipeXform, ent);
        var towardProcessor = directionFromProcessorToPipe.GetOpposite().ToPipeDirection();
        return pipeMask.HasDirection(towardProcessor);
    }

    public static PipeDirection GetConnectedDirections(
        EntityUid pipe,
        IEntityManager ent,
        SharedMapSystem map)
    {
        if (!ent.TryGetComponent(pipe, out ItemPipeComponent? comp) ||
            !ent.TryGetComponent(pipe, out TransformComponent? xform) ||
            !xform.Anchored ||
            xform.GridUid is not EntityUid gridUid ||
            !ent.TryGetComponent(gridUid, out MapGridComponent? grid))
        {
            return PipeDirection.None;
        }

        var wireConnect = ItemPipeVisualHelper.UsesWireConnect(pipe, ent);
        var mask = GetCurrentMask(pipe, comp, xform, ent);
        var result = PipeDirection.None;
        var pos = map.TileIndicesFor(gridUid, grid, xform.Coordinates);

        foreach (var direction in CardinalDirections)
        {
            var dirMask = direction.ToPipeDirection();
            if (!wireConnect && !mask.HasDirection(dirMask))
                continue;

            var connected = false;
            var enumerator = map.GetAnchoredEntitiesEnumerator(gridUid, grid, pos.Offset(direction));
            while (enumerator.MoveNext(out var entity) && entity != null)
            {
                if (entity == pipe)
                    continue;

                if (ent.HasComponent<ItemPipeComponent>(entity.Value) &&
                    ArePipesConnected(pipe, entity.Value, direction, ent, map))
                {
                    connected = true;
                    break;
                }

                if (ent.HasComponent<IndustrialProcessorComponent>(entity.Value))
                {
                    var face = direction.GetOpposite();
                    if (IsProcessorConnectedToPipe(entity.Value, pipe, face, ent))
                    {
                        connected = true;
                        break;
                    }
                }
            }

            if (connected)
                result |= dirMask;
        }

        return result;
    }

    public static PipeDirection GetRotatedMask(EntityUid pipe, IEntityManager ent)
    {
        if (!ent.TryGetComponent(pipe, out ItemPipeComponent? comp) ||
            !ent.TryGetComponent(pipe, out TransformComponent? xform))
        {
            return PipeDirection.None;
        }

        return GetCurrentMask(pipe, comp, xform, ent);
    }
}
