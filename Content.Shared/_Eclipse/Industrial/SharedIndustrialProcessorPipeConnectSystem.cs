using Content.Shared.Atmos;
using Robust.Shared.Map.Components;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>
/// Auto-binds item ports when pipes are placed against industrial machines.
/// Machine sprites never show pipe textures — only adjacent pipe tiles do.
/// </summary>
public sealed class SharedIndustrialProcessorPipeConnectSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;

    /// <summary>
    /// Auto-enables item input ports on machine faces touched by this pipe. Returns true if any port was bound.
    /// </summary>
    public bool TryAutoBindAdjacentProcessors(Entity<ItemPipeComponent> pipe)
    {
        var xform = Transform(pipe);
        if (!xform.Anchored || xform.GridUid is not EntityUid gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return false;
        }

        var pos = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var bound = false;

        foreach (var direction in ItemPipeConnectionHelper.CardinalDirections)
        {
            var enumerator = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, pos.Offset(direction));
            while (enumerator.MoveNext(out var entity) && entity != null)
            {
                if (!TryComp<IndustrialProcessorComponent>(entity, out var processor))
                    continue;

                var face = direction.GetOpposite();
                if (!IndustrialProcessorConnectionHelper.TryAutoBindItemPort(
                        entity.Value, pipe, face, EntityManager, _map))
                {
                    continue;
                }

                bound = true;
            }
        }

        return bound;
    }

    public bool TryAutoBindPortsFromAdjacentPipes(Entity<IndustrialProcessorComponent> processor)
    {
        var bound = false;

        foreach (var direction in ItemPipeConnectionHelper.CardinalDirections)
        {
            if (!IndustrialProcessorConnectionHelper.TryFindItemPipeOnFace(
                    processor, direction, EntityManager, _map, out var pipe))
            {
                continue;
            }

            if (!IndustrialProcessorConnectionHelper.TryAutoBindItemPort(
                    processor, pipe, direction, EntityManager, _map))
            {
                continue;
            }

            bound = true;
        }

        return bound;
    }
}
