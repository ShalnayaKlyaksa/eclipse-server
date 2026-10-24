using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public enum PipeTransferMode : byte
{
    Transit,
    Extract,
    Insert,
}
