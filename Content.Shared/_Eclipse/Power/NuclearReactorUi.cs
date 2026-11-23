using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Power;

[Serializable, NetSerializable]
public enum NuclearReactorUiKey : byte { Key }

[Serializable, NetSerializable]
public enum NuclearPartKind : byte { Missing, Shield, Fuel, Graphite, Control, Inlet, Outlet, Console, Core, Conflict }

[Serializable, NetSerializable]
public enum NuclearCommand : byte { Scram, Reset, Rods }

[Serializable, NetSerializable]
public sealed class NuclearReactorCommandMessage(NuclearCommand command, float value = 0) : BoundUserInterfaceMessage
{
    public NuclearCommand Command = command;
    public float Value = value;
}

[Serializable, NetSerializable]
public sealed class NuclearCellState(NuclearPartKind kind, bool valid, float fuel)
{
    public NuclearPartKind Kind = kind;
    public bool Valid = valid;
    public float Fuel = fuel;
}

[Serializable, NetSerializable]
public sealed class NuclearReactorState : BoundUserInterfaceState
{
    public NuclearCellState[] Cells = [];
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
    public float RodPosition = 1;
    public float RodTarget = 1;
    public float Reactivity;
    public float Xenon;
    public int Faults;
}
