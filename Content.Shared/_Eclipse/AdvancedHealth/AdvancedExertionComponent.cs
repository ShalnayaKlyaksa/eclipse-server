using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.AdvancedHealth;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class AdvancedExertionComponent : Component
{
    [DataField, AutoNetworkedField] public float Reserve = 100f;
    [DataField, AutoNetworkedField] public float RecoveryLoad;
    [DataField, AutoNetworkedField] public float SleepDebt;
    [DataField, AutoNetworkedField] public float Effort;
    [DataField, AutoNetworkedField] public bool Exhausted;
    [DataField, AutoNetworkedField] public bool SprintRequested;
    [DataField, AutoNetworkedField] public bool CalmWalking;
    [DataField] public float SprintCost = 3f;
    [DataField] public float WalkingRecovery = 5f;
    [DataField] public float RestRecovery = 8f;
    [DataField] public float AwakeFatigueRate = 0.003f;
    [DataField] public float SleepRecoveryRate = 0.2f;
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

[Serializable, NetSerializable]
public sealed class ToggleCalmWalkingEvent : EntityEventArgs;
