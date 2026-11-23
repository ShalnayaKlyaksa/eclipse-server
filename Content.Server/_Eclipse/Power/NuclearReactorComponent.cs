using Content.Shared._Eclipse.Power;

namespace Content.Server._Eclipse.Power;

/// <summary>One physical 7 by 7 core. All energy figures are game balance values, not a reactor design.</summary>
[RegisterComponent]
public sealed partial class NuclearReactorComponent : Component
{
    [DataField] public float HeatCapacity = 600000;
    [DataField] public float MaximumPower = 3000000;
    [DataField] public float MaximumHeatTransfer = 4000000;
    [DataField] public float Temperature = 293.15f;
    [DataField] public float TripTemperature = 1000;
    [DataField] public float FailureTemperature = 1500;
    [DataField] public float MinimumPressure = 100;
    [DataField] public float MaximumPressure = 3000;
    public readonly Dictionary<(int X, int Y), EntityUid> Parts = new();
    public NuclearCellState[] Cells = [];
    public bool Complete;
    public int Faults;
    public bool Scrammed = true;
    public bool Failed;
    public string Status = "nuclear-status-incomplete";
    public float RodTarget = 1;
    public float RodPosition = 1;
    public float ControlAvailability = 1;
    public float NeutronPower = 100;
    public float ThermalPower;
    public float DecayEnergy;
    public float DecayPower;
    public float CoolingPower;
    public float Pressure;
    public float Flow;
    public float Reactivity;
    public float Iodine;
    public float Xenon;
    public float LayoutAge;
}

[RegisterComponent]
public sealed partial class NuclearReactorPartComponent : Component
{
    [DataField(required: true)] public NuclearPartKind Kind;
    [DataField] public double FuelEnergy = 2000000000;
    [DataField] public double InitialFuelEnergy = 2000000000;
    // Membership is reassessed on every layout scan, never inferred from a shared pipe network.
    public EntityUid? Core;
}

/// <summary>Retains stored heat and decay energy when a hot core is physically destroyed.</summary>
[RegisterComponent]
public sealed partial class NuclearCoriumComponent : Component
{
    public float Temperature = 293.15f;
    public float HeatCapacity = 600000;
    public float DecayEnergy;
}
