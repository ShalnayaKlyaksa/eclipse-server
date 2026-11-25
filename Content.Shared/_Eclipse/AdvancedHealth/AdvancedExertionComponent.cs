using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.AdvancedHealth;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class AdvancedExertionComponent : Component
{
    // Small high-frequency network projection. AdvancedHealth remains the authoritative physiology.
    [DataField, AutoNetworkedField] public float NetworkHeartRate = 75f;
    [DataField, AutoNetworkedField] public float Reserve = 100f;
    [DataField, AutoNetworkedField] public float RecoveryLoad;
    [DataField, AutoNetworkedField] public float SleepDebt;
    [DataField, AutoNetworkedField] public float Effort;
    [DataField, AutoNetworkedField] public bool Exhausted;
    [DataField, AutoNetworkedField] public bool SprintRequested;
    [DataField, AutoNetworkedField] public bool CalmWalking;
    [DataField] public float SprintCost = 3f;
    [DataField, AutoNetworkedField] public float HeavyWorkCostMultiplier = 1f;
    [DataField] public float WalkingRecovery = 0.12f;
    [DataField] public float RestRecovery = 0.7f;
    public float RecoveryDelay;
    [DataField, AutoNetworkedField] public float ReserveCapacity = 100f;
    [DataField] public float AwakeFatigueRate = 0.003f;
    [DataField] public float SleepRecoveryRate = 0.5f;
    public float WorkEffort;
    public TimeSpan WorkUntil;
    public byte AlertFlags;
    public float LastSentSleepDebt;
    public TimeSpan LastMotionSample;
    public float RunningSeconds;
}

[Serializable, NetSerializable]
public enum PhysicalWorkClass : byte { Light, Medium, Heavy }

/// <summary>Optional defaults for operations performed with this item. Hands holding the tool count as working hands.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PhysicalWorkComponent : Component
{
    [DataField, AutoNetworkedField] public PhysicalWorkClass Difficulty = PhysicalWorkClass.Medium;
    [DataField, AutoNetworkedField] public int WorkingHands = 1;
    [DataField, AutoNetworkedField] public int FreeHands;
}
