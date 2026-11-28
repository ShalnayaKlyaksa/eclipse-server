using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Content.Shared.DoAfter;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public enum IndustrialProcessorUiKey : byte
{
    Key,
    Fluid,
}

[Serializable, NetSerializable]
public sealed class IndustrialProcessorSlotState(
    string? prototypeId,
    int count,
    string displayName)
{
    public string? PrototypeId = prototypeId;
    public int Count = count;
    public string DisplayName = displayName;
    public NetEntity? Item;
}

[Serializable, NetSerializable]
public sealed class IndustrialProcessorBoundUserInterfaceState(
    string machineName,
    string tierName,
    string stateKey,
    float progress,
    string? currentRecipeName,
    bool isPowered,
    bool usesHeat,
    bool hasSufficientHeat,
    int maxInputSlots,
    int maxOutputSlots,
    IndustrialProcessorSlotState[] inputSlots,
    IndustrialProcessorSlotState[] outputSlots,
    IndustrialProcessorSlotState processingSlot,
    FacePortState northFacePort,
    FacePortState southFacePort,
    FacePortState eastFacePort,
    FacePortState westFacePort) : BoundUserInterfaceState
{
    public bool UsesFluids;
    public IndustrialFluidSlotState[] FluidInput = [];
    public IndustrialFluidSlotState[] FluidOutput = [];
    public float FluidInputCapacity;
    public float FluidOutputCapacity;
    public bool IsBuffer;
    public bool IsManual;
    public bool ManualBusy;
    public bool IsExtractor;
    public string? ExtractionResource;
    public int ExtractionReserve;
    public bool Enabled = true;
    public string? SelectedRecipeId;
    public string? CurrentRecipeId;
    public float SecondsRemaining;
    public float Temperature;
    public float RequiredTemperature;
    public float PowerWatts;
    public float? SuppliedPowerWatts;
    public float SpeedMultiplier;
    public IndustrialRecipeUiState[] Recipes = [];
    public string MachineName = machineName;
    public string TierName = tierName;
    public string StateKey = stateKey;
    public float Progress = progress;
    public string? CurrentRecipeName = currentRecipeName;
    public bool IsPowered = isPowered;
    public bool UsesHeat = usesHeat;
    public bool HasSufficientHeat = hasSufficientHeat;
    public int MaxInputSlots = maxInputSlots;
    public int MaxOutputSlots = maxOutputSlots;
    public IndustrialProcessorSlotState[] InputSlots = inputSlots;
    public IndustrialProcessorSlotState[] OutputSlots = outputSlots;
    public IndustrialProcessorSlotState ProcessingSlot = processingSlot;
    public FacePortState NorthFacePort = northFacePort;
    public FacePortState SouthFacePort = southFacePort;
    public FacePortState EastFacePort = eastFacePort;
    public FacePortState WestFacePort = westFacePort;
}

[Serializable, NetSerializable]
public sealed class IndustrialProcessorSlotMessage(bool isInput, int slotIndex, NetEntity? item = null) : BoundUserInterfaceMessage
{
    public bool IsInput = isInput;
    public int SlotIndex = slotIndex;
    public NetEntity? Item = item;
}

[Serializable, NetSerializable]
public sealed class IndustrialRecipeUiState(string id, string name, float seconds,
    bool unlocked, IndustrialProcessorSlotState[] inputs, IndustrialProcessorSlotState[] outputs)
{
    public IndustrialFluidSlotState[] FluidInputs = [];
    public IndustrialFluidSlotState[] FluidOutputs = [];
    public IndustrialFluidSlotState[] Bath = [];
    public float HeatKilojoules;
    public string Id = id;
    public string Name = name;
    public float Seconds = seconds;
    public bool Unlocked = unlocked;
    public IndustrialProcessorSlotState[] Inputs = inputs;
    public IndustrialProcessorSlotState[] Outputs = outputs;
}

[Serializable, NetSerializable]
public sealed class IndustrialFluidSlotState(string id, string name, float amount, Color color)
{
    public string Id = id;
    public string Name = name;
    public float Amount = amount;
    public Color Color = color;
}

[Serializable, NetSerializable]
public sealed class IndustrialProcessorDrainMessage(bool input, string? reagent, NetEntity container) : BoundUserInterfaceMessage
{
    public bool Input = input;
    public string? Reagent = reagent;
    public NetEntity Container = container;
}

[Serializable, NetSerializable]
public sealed class IndustrialFluidTankState(bool input, float capacity, IndustrialFluidSlotState[] contents)
{
    public bool Input = input;
    public float Capacity = capacity;
    public IndustrialFluidSlotState[] Contents = contents;
}

[Serializable, NetSerializable]
public sealed class IndustrialFluidSelectionState(IndustrialFluidTankState[] tanks) : BoundUserInterfaceState
{
    public IndustrialFluidTankState[] Tanks = tanks;
}

[Serializable, NetSerializable]
public sealed partial class IndustrialFluidDrawDoAfterEvent : DoAfterEvent
{
    public bool Input;
    public string Reagent;
    public IndustrialFluidDrawDoAfterEvent(bool input, string reagent)
    {
        Input = input;
        Reagent = reagent;
    }
    public override DoAfterEvent Clone() => this;
}

[Serializable, NetSerializable]
public sealed class IndustrialProcessorRecipeMessage(string? recipeId) : BoundUserInterfaceMessage
{
    public string? RecipeId = recipeId;
}

[Serializable, NetSerializable]
public sealed class IndustrialProcessorToggleMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class IndustrialProcessorPortMessage(Robust.Shared.Maths.Direction face, FacePortState mode) : BoundUserInterfaceMessage
{
    public Robust.Shared.Maths.Direction Face = face;
    public FacePortState Mode = mode;
}
