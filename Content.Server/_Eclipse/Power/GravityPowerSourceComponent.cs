namespace Content.Server._Eclipse.Power;

/// <summary>Paid mechanical output of a mass-fed gravity well, shared by nearby kinetic generators.</summary>
[RegisterComponent]
public sealed partial class GravityPowerSourceComponent : Component
{
    [DataField] public float HarvestRange = 7;
    [DataField] public float MinimumHarvestRange = 3;
    [DataField] public float StablePull = 2;
    [DataField] public float UnstablePull = 35;
    public float PendingPower;
    public float AvailablePower;
    public float HarvestedPower;
    public int Collectors;
    public float ResidualActivity;
}

[RegisterComponent]
public sealed partial class GravitationalKineticGeneratorComponent : Component
{
    [DataField] public float RatedPower = 750000;
    public float RotorSpeed;
    public float AvailablePower;
    public EntityUid? Source;
}

/// <summary>Short-lived gravity left behind when an active containment machine is destroyed. No electrical output.</summary>
[RegisterComponent]
public sealed partial class ResidualMicroBlackHoleComponent : Component
{
    public float Activity = 1;
}
