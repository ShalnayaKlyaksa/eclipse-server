using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Beekeeping;

[RegisterComponent]
public sealed partial class BeeSpecimenComponent : Component
{
    [DataField] public BeeCaste Caste;
    [DataField] public BeeGenome Genome = new();
    [DataField] public bool Analysed;
}

[RegisterComponent]
public sealed partial class ApiaryComponent : Component
{
    [DataField] public BeeGenome? Mother;
    [DataField] public BeeGenome? Father;
    [DataField] public float Progress;
    [DataField] public float CycleSeconds;
    [DataField] public float Nectar;
    [DataField] public float Water;
    [DataField] public int Minerals;
    [DataField] public int FrameDurability;
    [DataField] public float SmokeSeconds;
    [DataField] public bool Enabled;
    [DataField] public int CompletedCycles;
    [DataField] public bool ControlledClimate;
    [DataField] public string FrameKind = "Production";
    [DataField] public Dictionary<EntProtoId, int> ForageSamples = new();
    public Container Input = default!;
    public Container Output = default!;
    public string Status = "apiary-status-idle";
    public float Temperature = 293.15f;
    public float Pressure;
}

[RegisterComponent] public sealed partial class BeekeeperProtectionComponent : Component;
[RegisterComponent] public sealed partial class BeeAnalyzerComponent : Component;
[RegisterComponent] public sealed partial class BeeNetComponent : Component;
[RegisterComponent] public sealed partial class ApiaryFrameComponent : Component
{
    [DataField] public string Kind = "Production";
    [DataField] public int Durability = 12;
}
[RegisterComponent] public sealed partial class BeeSmokerComponent : Component
{
    [DataField] public int Charges = 10;
}

[Serializable, NetSerializable] public enum ApiaryUiKey : byte { Key }
[Serializable, NetSerializable] public enum ApiaryAction : byte { Toggle, Harvest, Eject, Analyse, InsertHeld, RemoveFrame }
[Serializable, NetSerializable]
public sealed class ApiaryActionMessage(ApiaryAction action) : BoundUserInterfaceMessage
{
    public ApiaryAction Action = action;
}

[Serializable, NetSerializable]
public sealed class ApiaryState(string status, string queen, string parents, string outputs, string mutations,
    float progress, float nectar, float water, int minerals, int frame, int cycles, bool enabled) : BoundUserInterfaceState
{
    public string Status = status;
    public string Queen = queen;
    public string Parents = parents;
    public string Outputs = outputs;
    public string Mutations = mutations;
    public float Progress = progress;
    public float Nectar = nectar;
    public float Water = water;
    public int Minerals = minerals;
    public int Frame = frame;
    public int Cycles = cycles;
    public bool Enabled = enabled;
    public WorkshopItemState[] ParentSlots = [];
    public WorkshopItemState[] OutputSlots = [];
    public ApiarySpeciesState[] Species = [];
    public ApiaryMutationState[] Crosses = [];
    public float SecondsRemaining;
    public float SmokeSeconds;
    public bool ActiveQueen;
    public string FrameKind = "Production";
    public int Generation;
    public float LineageMinutes;
    public float Temperature;
    public float Pressure;
    public string? ForageComb;
}

[Serializable, NetSerializable]
public sealed class ApiarySlotMessage(bool output, int index) : BoundUserInterfaceMessage
{
    public bool Output = output;
    public int Index = index;
}

[Serializable, NetSerializable]
public sealed class ApiarySpeciesState(string id, string name, int tier, float seconds, string comb, string combName,
    string? rareProduct, string? rareName, float rareChance, int generation, float minutes, bool minerals)
{
    public string Id = id;
    public string Name = name;
    public int Tier = tier;
    public float Seconds = seconds;
    public string Comb = comb;
    public string CombName = combName;
    public string? RareProduct = rareProduct;
    public string? RareName = rareName;
    public float RareChance = rareChance;
    public int Generation = generation;
    public float Minutes = minutes;
    public bool Minerals = minerals;
    public string Branch = "Natural";
    public float MinTemperature;
    public float MaxTemperature;
    public string? Byproduct;
    public string? ByproductName;
    public float ByproductChance;
    public string? Separation;
    public float FoodMultiplier = 1f;
    public float WaterMultiplier = 1f;
    public int CombBonus;
    public float StingChance = 0.35f;
}

[Serializable, NetSerializable]
public sealed class ApiaryMutationState(string result, string first, string second, float chance, int generation, float minutes, bool minerals)
{
    public string Result = result;
    public string First = first;
    public string Second = second;
    public float Chance = chance;
    public int Generation = generation;
    public float Minutes = minutes;
    public bool Minerals = minerals;
}
