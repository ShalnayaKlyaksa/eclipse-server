using Content.Shared.Atmos;
using Content.Shared.Wires;

namespace Content.Shared._Eclipse.Industrial;

public static class ItemPipeVisualHelper
{
    public static WireVisDirFlags ToWireVisMask(PipeDirection directions)
    {
        var mask = WireVisDirFlags.None;

        if (directions.HasDirection(PipeDirection.North))
            mask |= WireVisDirFlags.North;

        if (directions.HasDirection(PipeDirection.South))
            mask |= WireVisDirFlags.South;

        if (directions.HasDirection(PipeDirection.East))
            mask |= WireVisDirFlags.East;

        if (directions.HasDirection(PipeDirection.West))
            mask |= WireVisDirFlags.West;

        return mask;
    }

    public static bool UsesWireConnect(EntityUid uid, IEntityManager ent)
    {
        return ent.HasComponent<ItemPipeWireConnectComponent>(uid);
    }
}
