using Content.Shared._Eclipse.Power;

namespace Content.Server._Eclipse.Power;

/// <summary>One physical graphite core. Energy figures are game balance values.</summary>
[RegisterComponent]
public sealed partial class NuclearReactorComponent : Component
{
    public const float RequiredHeavyWater = 160;
    [DataField] public float HeatCapacity = 600000;
    [DataField] public float MaximumPower = 3000000;
    [DataField] public float MaximumHeatTransfer = 4000000;
    [DataField] public float Temperature = 293.15f;
    [DataField] public float TripTemperature = 1000;
    [DataField] public float FailureTemperature = 1500;
    [DataField] public float MinimumPressure = 100;
    [DataField] public float MaximumPressure = 3000;
    public readonly Dictionary<(int X, int Y), EntityUid> Parts = new();
    public readonly HashSet<(int X, int Y)> Footprint = new();
    public readonly HashSet<EntityUid> AssociatedSections = new();
    public EntityUid? Inlet;
    public EntityUid? Outlet;
    public int Left;
    public int Top;
    public int Width;
    public int Height;
    public bool HullClosed;
    public bool PortsReady;
    public int FuelHatches;
    public int GraphiteSections;
    public int ControlSections;
    public NuclearCellState[] Cells = [];
    public bool Complete;
    public int Faults;
    public bool Scrammed = true;
    public bool Failed;
    public string Status = "nuclear-status-incomplete";
    public readonly NuclearRodGroupState[] RodGroups = [new(1, 1, 0), new(1, 1, 0), new(1, 1, 0), new(1, 1, 0)];
    public EntityUid? Console;
    public float NeutronPower = 100;
    public float ThermalPower;
    public float DecayEnergy;
    public float DecayPower;
    public float CoolingPower;
    public float HeavyWaterLevel;
    public float Pressure;
    public float Flow;
    public bool WaterCooling;
    public bool SteamCooling;
    public float SteamFlow;
    public float SteamWaterLevel;
    public float NoWaterFlowTime;
    public float InletTemperature;
    public float OutletTemperature;
    public float Reactivity;
    public float Iodine;
    public float Xenon;
    public float LayoutAge;
    public int MoxCells;
    public int LiveCells;
    public EntityUid? Conditioner;
    public bool MoxBoost;
    public float AuxiliaryCooling;
    public int CoolingContacts;
    public float Oxidizer;
    public float Solvent;
}

[RegisterComponent]
public sealed partial class NuclearReactorPartComponent : Component
{
    [DataField(required: true)] public NuclearPartKind Kind;
    [DataField] public double FuelEnergy = 2000000000;
    [DataField] public double InitialFuelEnergy = 2000000000;
    [DataField] public bool FuelInitialized;
    [DataField] public int RodGroup;
    // Membership is reassessed on every layout scan, never inferred from a shared pipe network.
    public EntityUid? Core;
}

[RegisterComponent]
public sealed partial class NuclearFuelCellComponent : Component
{
    [DataField] public NuclearFuelKind Kind = NuclearFuelKind.Uranium;
    [DataField] public double Energy = 2000000000;
    [DataField] public double InitialEnergy = 2000000000;
}

[RegisterComponent]
public sealed partial class NuclearReactorConditionerComponent : Component
{
    [DataField] public float MaximumHeatTransfer = 5000000;
}

/// <summary>Retains stored heat and decay energy when a hot core is physically destroyed.</summary>
[RegisterComponent]
public sealed partial class NuclearCoriumComponent : Component
{
    public float Temperature = 293.15f;
    public float HeatCapacity = 600000;
    public float DecayEnergy;
}

[RegisterComponent]
public sealed partial class NuclearWaterCollectorComponent : Component;

[RegisterComponent]
public sealed partial class NuclearWaterTurbineComponent : Component
{
    [DataField] public float NominalPower = 600000;
    [DataField] public float Efficiency = .25f;
    [DataField] public float MaximumHeatTransfer = 3200000;
    [DataField] public float FlowRate = 12;
}

[RegisterComponent]
public sealed partial class NuclearSteamOutletComponent : Component;

[RegisterComponent]
public sealed partial class NuclearSteamTurbineComponent : Component
{
    [DataField] public float NominalPower = 600000;
    [DataField] public float Efficiency = .7f;
    [DataField] public float SteamConductance = .15f;
}

[RegisterComponent]
public sealed partial class NuclearSteamCondenserComponent : Component
{
    [DataField] public float MaximumHeatTransfer = 4500000;
}
