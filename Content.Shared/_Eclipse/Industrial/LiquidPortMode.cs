using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public enum LiquidPortMode : byte
{
  Disabled,
  Input,
  Output,
}
