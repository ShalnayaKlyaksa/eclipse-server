using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.ProtoCore;

[Serializable, NetSerializable]
public sealed partial class ProtoCoreHackDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class ProtoCoreInstallDeviceDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class ProtoCoreCutDeviceDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class ProtoCoreStartDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class ProtoCoreStabilizeDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class ProtoCorePhysicalOverrideDoAfterEvent : SimpleDoAfterEvent;
