using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public enum IndustrialProcessorState : byte
{
    Idle,
    Working,
    Blocked,
    Unpowered,
    Unheated,
}
