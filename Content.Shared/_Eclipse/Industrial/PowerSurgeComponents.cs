using Robust.Shared.GameStates;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>Insulation damage from external transients, separate from normal network power demand.</summary>
[RegisterComponent]
public sealed partial class PowerSurgeCableComponent : Component
{
    [DataField] public float VoltageTolerance = 1.25f;
    [DataField] public float BurnThreshold = 3f;
    [DataField] public float RecoveryPerSecond = .05f;
    public float Stress;
    public float LastPeak = 1;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class IndustrialSurgeProtectorComponent : Component
{
    [DataField] public float Capacity = 200000;
    [DataField] public float RemainingCapacity = 200000;
}
