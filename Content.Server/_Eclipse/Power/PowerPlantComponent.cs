using Content.Shared._Eclipse.Power;
namespace Content.Server._Eclipse.Power;

/// <summary>Finite fuel heat, real gas-loop cooling and physical operating limits for a native fuel generator.</summary>
[RegisterComponent]
public sealed partial class PowerPlantComponent : Component
{
    [DataField] public float HeatCapacity = 30000;
    [DataField] public float Temperature = 293.15f;
    [DataField] public float HeatPerFuel = 150000;
    [DataField] public float ShutdownTemperature = 850;
    [DataField] public float FailureTemperature = 1300;
    [DataField] public float PassiveCoolingPower = 500;
    [DataField] public float MaximumHeatTransfer = 300000;
    [DataField] public float WarmupSeconds = 15;
    [DataField] public string CoolantInlet = "coolant_in";
    [DataField] public string CoolantOutlet = "coolant_out";
    [DataField] public bool HeatSource;
    [DataField] public float DecayFraction;
    [DataField] public float DecayTime = 90;
    [DataField] public int RequiredFields;
    [DataField] public int FieldRadius = 1;
    public float Warmup;
    public float LastStepTime;
    public float GeneratedHeatPower;
    public float StoredDecayEnergy;
    public float CoolingPower;
    public float DecayPower;
    public int FieldCount;
    public bool Tripped;
    public bool Failed;
    public PowerPlantCondition Condition;
}

[RegisterComponent]
public sealed partial class PowerPlantFieldStabilizerComponent : Component;

[RegisterComponent]
public sealed partial class FusionReactorComponent : Component
{
    [DataField] public float MinimumFlow = 3;
    [DataField] public float NominalFlow = 18;
    [DataField] public float BoostFlow = 60;
    public float MeasuredFlow;
}
