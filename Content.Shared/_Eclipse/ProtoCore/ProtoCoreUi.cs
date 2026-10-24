using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.ProtoCore;

public static class ProtoCoreConsoleUi
{
    public const string ActivationKeySlotId = "ProtoCoreActivationKey";
    public const string HackingDeviceSlotId = "ProtoCoreHackingDevice";
}

[Serializable, NetSerializable]
public enum ProtoCoreState : byte
{
    Idle,
    Hacked,
    Meltdown,
    Critical,
    Stabilized,
}

[Serializable, NetSerializable]
public enum ProtoCoreConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum ProtoCoreConsoleAction : byte
{
    Start,
    Stabilize,
    PhysicalOverride,
}

[Serializable, NetSerializable]
public enum ProtoCoreVisuals : byte
{
    HackingDeviceInstalled,
}

[Serializable, NetSerializable]
public enum ProtoCoreVisualLayers : byte
{
    HackingDevice,
}

[Serializable, NetSerializable]
public sealed class ProtoCoreConsoleBoundUserInterfaceState(
    ProtoCoreState state,
    string remainingTime,
    string powerOutput,
    string storedEnergy,
    bool canStart,
    bool canStabilize,
    bool noSmesRemaining) : BoundUserInterfaceState
{
    public ProtoCoreState State = state;
    public string RemainingTime = remainingTime;
    public string PowerOutput = powerOutput;
    public string StoredEnergy = storedEnergy;
    public bool CanStart = canStart;
    public bool CanStabilize = canStabilize;
    public bool NoSmesRemaining = noSmesRemaining;
}

[Serializable, NetSerializable]
public sealed class ProtoCoreConsoleActionMessage(ProtoCoreConsoleAction action) : BoundUserInterfaceMessage
{
    public ProtoCoreConsoleAction Action = action;
}
