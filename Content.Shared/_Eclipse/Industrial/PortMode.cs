using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public enum PortMode : byte
{
    Disabled,
    Input,
    Output,
}
