using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public enum IndustrialProcessorVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum IndustrialProcessorVisualLayers : byte
{
    Base,
}
