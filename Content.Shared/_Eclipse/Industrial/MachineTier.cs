using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public enum MachineTier : byte
{
    Basic,
    Industrial,
    Perfect,
}
