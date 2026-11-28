using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public enum IndustrialProcessorType : byte
{
    Crusher,
    ThermalCentrifuge,
    Smelter,
    Assembler,
    Buffer,
    OreWasher,
    Separator,
    Mixer,
    ChemicalReactor,
    FilterPress,
    Dryer,
    Distillation,
    Crystallizer,
    BlastFurnace,
    VacuumFurnace,
    Cutter,
    Grinder,
    ChemicalBath,
    Coater,
    LaserEngraver,
    Electrolyzer,
    WireMill,
    CircuitTester,
    FluidTank,
    Extruder,
    PipeFabricator,
    Extractor,
    FuelPress,
    FuelCartridgeAssembler,
    RollingMill,
    StampingPress,
}
