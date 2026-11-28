using Content.Shared.DoAfter;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Botany;

[Serializable, NetSerializable]
public sealed partial class TillSoilDoAfterEvent : DoAfterEvent
{
    public readonly NetEntity Grid;
    public readonly Vector2i Indices;

    public TillSoilDoAfterEvent(NetEntity grid, Vector2i indices)
    {
        Grid = grid;
        Indices = indices;
    }

    public override DoAfterEvent Clone() => new TillSoilDoAfterEvent(Grid, Indices);
}
