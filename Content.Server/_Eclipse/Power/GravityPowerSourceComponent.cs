namespace Content.Server._Eclipse.Power;

/// <summary>Paid mechanical output of a mass-fed gravity well, shared by nearby kinetic generators.</summary>
[RegisterComponent]
public sealed partial class GravityPowerSourceComponent : Component
{
    [DataField] public float HarvestRange = 21;
    [DataField] public float MinimumHarvestRange = 3;
    public float PendingPower;
    public float FuelPower;
    public float AvailablePower;
    public float HarvestedPower;
    public int Collectors;
    public float ResidualActivity;
    [ViewVariables] public float Mass;
    [ViewVariables] public float StoredEnergy;
    [ViewVariables] public float FieldLimit = 2.5f;
    [ViewVariables] public int StaticProjectors;
    public float StarvingTime;
}

[RegisterComponent]
public sealed partial class GravitationalKineticGeneratorComponent : Component
{
    [DataField] public float RatedPower = 750000;
    public float RotorSpeed;
    public float AvailablePower;
    public float LocalPull;
    public EntityUid? Source;
}

/// <summary>A gravity well that has escaped the generating plant.</summary>
[RegisterComponent]
public sealed partial class ResidualMicroBlackHoleComponent : Component
{
    public float Activity = 1;
    [ViewVariables] public float Mass = 1;
    public float StarvingTime;
}

[RegisterComponent]
public sealed partial class GravityStaticChargeProjectorComponent : Component;

[RegisterComponent]
public sealed partial class GravitySuppressionEmitterComponent : Component;

[RegisterComponent]
public sealed partial class GravityRecoveryLockerComponent : Component;

[RegisterComponent]
public sealed partial class GravityBreachDispatchedComponent : Component;
