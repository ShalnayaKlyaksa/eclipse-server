using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public enum ItemPipeLayer : byte
{
    Primary,
    Secondary,
    Tertiary,
}
