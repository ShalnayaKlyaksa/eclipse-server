using Content.Shared.Containers.ItemSlots;

namespace Content.Server._Eclipse.ProtoCore.Components;

[RegisterComponent, Access(typeof(ProtoCoreSystem))]
public sealed partial class ProtoCoreConsoleComponent : Component
{
    [DataField]
    public ItemSlot ActivationKeySlot = new();

    [DataField]
    public ItemSlot HackingDeviceSlot = new();

    [DataField]
    public float MaxCoreDistance = 4f;

    [DataField]
    public float HackTime = 12f;

    [DataField]
    public float InstallDeviceTime = 5f;

    [DataField]
    public float CutDeviceTime = 3f;

    [DataField]
    public float StartTime = 8f;

    [DataField]
    public float StabilizeTime = 20f;

    [DataField]
    public float PhysicalOverrideTime = 10f;

    [DataField]
    public bool DeviceConnected;

    [DataField]
    public bool HackInProgress;

    [DataField]
    public float HackRemainingTime;

    [DataField]
    public bool ForceDeviceEjectInProgress;

    public bool InstallingHackingDevice;
}
