using Robust.Shared.Serialization;
namespace Content.Shared._Eclipse.Power;

[Serializable, NetSerializable]
public enum PowerPlantCondition : byte { Stopped, Warming, Running, Overheated, MissingField, Cooling, Failed, NoAuxiliaryPower, NoCoolant, Limited }

[Serializable, NetSerializable]
public enum FusionReactorVisuals : byte { Status }

[Serializable, NetSerializable]
public sealed class PowerPlantOperatingState
{
    public float Temperature;
    public float ShutdownTemperature;
    public float Warmup;
    public float AvailablePower;
    public float CoolingPower;
    public float GeneratedHeatPower;
    public float DecayPower;
    public bool HeatSource;
    public bool GravitySource;
    public float GravityPower;
    public float HarvestedPower;
    public int KineticGenerators;
    public bool Tripped;
    public int FieldCount;
    public int RequiredFields;
    public PowerPlantCondition Condition;
    public float? FusionNominalPower;
    public float CoolantFlow;
    public float AuxiliaryPower;
    public float DeliveredPower;
    public bool FusionReady;
}

[Serializable, NetSerializable]
public sealed class PowerPlantResetMessage : BoundUserInterfaceMessage;
