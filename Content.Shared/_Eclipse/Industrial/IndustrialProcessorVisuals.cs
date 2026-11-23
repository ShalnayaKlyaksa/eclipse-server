using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public enum IndustrialProcessorVisuals : byte
{
    State,
    Powered,
}

[Serializable, NetSerializable]
public enum IndustrialProcessorVisualLayers : byte
{
    Base,
}
