using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public enum IndustrialProcessorUiKey : byte
{
    Key,
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
public sealed class IndustrialProcessorSlotMessage(bool isInput, int slotIndex) : BoundUserInterfaceMessage
{
    public bool IsInput = isInput;
    public int SlotIndex = slotIndex;
}
