using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public enum ItemPipeVisuals : byte
{
    ConnectedDirections,
}

[Serializable, NetSerializable]
public enum ItemPipeVisualLayers : byte
{
    Hub,
    North,
    West,
    South,
    East,
}
