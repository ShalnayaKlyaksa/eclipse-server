using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Power;

[Serializable, NetSerializable]
public enum NuclearReactorUiKey : byte { Key }

[Serializable, NetSerializable]
public enum NuclearPartKind : byte { Missing, Shield, Fuel, Graphite, Control, Inlet, Outlet, Console, Core, Conflict, HeatSink, Outside }

[Serializable, NetSerializable]
public enum NuclearCommand : byte { Scram, Reset, Rods, AssignGroup }

[Serializable, NetSerializable]
public enum NuclearReactorVisuals : byte { Active, Rods, ShieldEdge, RodInsertion, Fuel }

[Serializable, NetSerializable]
public enum NuclearFuelKind : byte { None, Uranium, Mox, Thorium, Pulse }

[Serializable, NetSerializable]
public sealed class NuclearReactorCommandMessage(NuclearCommand command, float value = 0, int group = -1,
    int cellX = int.MinValue, int cellY = int.MinValue) : BoundUserInterfaceMessage
{
    public NuclearCommand Command = command;
    public float Value = value;
    public int Group = group;
    public int CellX = cellX;
    public int CellY = cellY;
}

[Serializable, NetSerializable]
public sealed class NuclearCellState(NuclearPartKind kind, bool valid, float fuel, int rodGroup = -1,
    NuclearFuelKind fuelKind = NuclearFuelKind.None)
{
    public NuclearPartKind Kind = kind;
    public bool Valid = valid;
    public float Fuel = fuel;
    public int RodGroup = rodGroup;
    public NuclearFuelKind FuelKind = fuelKind;
    public int FuelNeighbours;
}

[Serializable, NetSerializable]
public readonly record struct NuclearRodGroupState(float Position, float Target, int Count)
{
    public const int GroupCount = 4;
}

[Serializable, NetSerializable]
public sealed class NuclearReactorState : BoundUserInterfaceState
{
    public NuclearCellState[] Cells = [];
    public int Left;
    public int Top;
    public int Width;
    public int Height;
    public bool HullClosed;
    public int FuelHatches;
    public int GraphiteSections;
    public int ControlSections;
    public int LiveCells;
    public bool PortsReady;
    public float HeatFraction;
    public string Status = "nuclear-status-no-core";
    public bool Powered;
    public bool Complete;
    public bool Scrammed;
    public float Temperature;
    public float Power;
    public float Cooling;
    public float Decay;
    public float Pressure;
    public float Flow;
    public bool WaterCooling;
    public bool SteamCooling;
    public float SteamFlow;
    public float SteamWaterLevel;
    public float HeavyWaterLevel;
    public float RequiredHeavyWater;
    public int MoxCells;
    public float MoxHeat;
    public bool Conditioner;
    public bool MoxBoost;
    public float AuxiliaryCooling;
    public int CoolingContacts;
    public float Oxidizer;
    public float Solvent;
    public float InletTemperature;
    public float OutletTemperature;
    public NuclearRodGroupState[] RodGroups = [];
    public bool CanAssignGroups;
    public bool CanReset;
    public float Reactivity;
    public float Xenon;
    public int Faults;
}
