using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public enum IndustrialProcessorType : byte
{
    Crusher,
    ThermalCentrifuge,
    Smelter,
}
