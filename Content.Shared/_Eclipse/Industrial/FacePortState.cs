using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public enum FacePortState : byte
{
    Disabled,
    ItemInput,
    ItemOutput,
    LiquidInput,
    LiquidOutput,
    HeatInput,
}
