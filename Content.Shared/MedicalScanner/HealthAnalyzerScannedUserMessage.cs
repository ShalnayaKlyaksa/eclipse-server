using Robust.Shared.Serialization;
using Content.Shared._Eclipse.AdvancedHealth;

namespace Content.Shared.MedicalScanner;

/// <summary>
/// On interacting with an entity retrieves the entity UID for use with getting the current damage of the mob.
/// </summary>
[Serializable, NetSerializable]
public sealed class HealthAnalyzerScannedUserMessage : BoundUserInterfaceMessage
{
    public HealthAnalyzerUiState State;

    public HealthAnalyzerScannedUserMessage(HealthAnalyzerUiState state)
    {
        State = state;
    }
}

/// <summary>
/// Sent once when a deliberate scan completes (the scan do-after), as opposed to the periodic state
/// updates. The advanced-health client uses this to re-open its menu after a manual dismissal.
/// </summary>
[Serializable, NetSerializable]
public sealed class HealthAnalyzerScanStartedMessage : BoundUserInterfaceMessage
{
}

/// <summary>
/// Contains the current state of a health analyzer control. Used for the health analyzer and cryo pod.
/// </summary>
[Serializable, NetSerializable]
public struct HealthAnalyzerUiState
{
    public readonly NetEntity? TargetEntity;
    public float Temperature;
    public float BloodLevel;
    public bool? ScanMode;
    public bool? Bleeding;
    public bool? Unrevivable;
    public BodyPartUiState[]? AdvancedBodyParts;
    public float? AdvancedBloodVolume;
    public float? Oxygenation;
    public float? Pain;
    public float? Shock;
    public float? TraumaLoad;
    public string? AdvancedBodyFluid;
    public string? AdvancedBloodType;
    public Color? AdvancedFluidColor;
    public float? AdvancedOxygenCarryingCapacity;

    public HealthAnalyzerUiState() {}

    public HealthAnalyzerUiState(NetEntity? targetEntity, float temperature, float bloodLevel, bool? scanMode, bool? bleeding, bool? unrevivable)
    {
        TargetEntity = targetEntity;
        Temperature = temperature;
        BloodLevel = bloodLevel;
        ScanMode = scanMode;
        Bleeding = bleeding;
        Unrevivable = unrevivable;
    }
}
