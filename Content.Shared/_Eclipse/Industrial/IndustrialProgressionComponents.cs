using Content.Shared.Research.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.DoAfter;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>Reusable physical copy of completed research, separate from random lathe recipe disks.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class IndustrialDataDiskComponent : Component
{
    public const int Capacity = 3;

    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<TechnologyPrototype>> Technologies = new();
}

/// <summary>Installed research survives disconnection from the science server.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class IndustrialMachineKnowledgeComponent : Component
{
    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<TechnologyPrototype>> Technologies = new();
}

[RegisterComponent]
public sealed partial class IndustrialManualProcessorComponent : Component
{
    public bool Busy;
}

[RegisterComponent]
public sealed partial class IndustrialBlueprintComponent : Component
{
    [DataField] public EntProtoId Frame = "IndustrialChassisConstruction";
}

/// <summary>General room cleanliness reading, also usable by medical equipment.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RoomSanitationComponent : Component
{
    [DataField, AutoNetworkedField] public int Grade;
    [DataField, AutoNetworkedField] public float Cleanliness;
}

[RegisterComponent]
public sealed partial class CleanroomControllerComponent : Component
{
    [DataField] public int Grade = 1;
    [DataField] public int MaximumTiles = 64;
    [DataField] public float PurificationPerSecond = 2f;
    public float Cleanliness;
}

[RegisterComponent]
public sealed partial class IndustrialResourceDepositComponent : Component
{
    [DataField] public EntProtoId Resource = "SteelOre1";
    [DataField] public int Remaining = 40;
}

[RegisterComponent]
public sealed partial class IndustrialExtractorComponent : Component
{
    [DataField] public float Interval = 5;
    [DataField] public int Amount = 1;
    [DataField] public ProtoId<TechnologyPrototype>? RequiredTechnology;
    public float Elapsed;
    public IndustrialProcessorState State;
}

[Serializable, NetSerializable]
public enum IndustrialDiskUiKey : byte
{
    Write,
}

[Serializable, NetSerializable]
public sealed class IndustrialOpenDiskWriterMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class IndustrialDiskWriterState(NetEntity console, ProtoId<TechnologyPrototype>[] available,
    ProtoId<TechnologyPrototype>[] recorded) : BoundUserInterfaceState
{
    public NetEntity Console = console;
    public ProtoId<TechnologyPrototype>[] Available = available;
    public ProtoId<TechnologyPrototype>[] Recorded = recorded;
}

[Serializable, NetSerializable]
public sealed class IndustrialWriteDiskMessage(ProtoId<TechnologyPrototype>[] technologies) : BoundUserInterfaceMessage
{
    public ProtoId<TechnologyPrototype>[] Technologies = technologies;
}

[Serializable, NetSerializable]
public sealed class IndustrialManualOperateMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed partial class IndustrialManualDoAfterEvent : SimpleDoAfterEvent
{
    public string Recipe;
    public IndustrialManualDoAfterEvent(string recipe) => Recipe = recipe;
}

[Serializable, NetSerializable]
public sealed partial class IndustrialDepositMineEvent : SimpleDoAfterEvent;

[RegisterComponent]
public sealed partial class IndustrialResearchSampleComponent : Component
{
    [DataField] public int Points = 750;
    [DataField] public float StudySeconds = 15;
    [DataField] public int MaximumStudies = 4;
}

[Serializable, NetSerializable]
public sealed partial class IndustrialAnalyzeSampleEvent : SimpleDoAfterEvent;
