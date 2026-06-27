using Content.Shared._Eclipse.Industrial;

namespace Content.Server._Eclipse.Industrial;

public sealed class ItemPipeLayersSystem : SharedItemPipeLayersSystem
{
    [Dependency] private readonly ItemPipeSystem _pipes = default!;

    protected override void OnLayerChanged(Entity<ItemPipeLayersComponent> ent)
    {
        if (!TryComp<ItemPipeComponent>(ent, out var pipe))
            return;

        _pipes.UpdateConnections((ent, pipe));
        _pipes.UpdateAdjacentConnections((ent, pipe));
    }
}
