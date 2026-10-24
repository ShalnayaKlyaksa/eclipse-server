using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public enum PipeTier : byte
{
    Basic,
    Industrial,
    Perfect,
}
