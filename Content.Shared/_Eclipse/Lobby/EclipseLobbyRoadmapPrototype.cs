using Robust.Shared.Prototypes;

namespace Content.Shared._Eclipse.Lobby;

[Prototype]
public sealed partial class EclipseLobbyRoadmapPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public List<LobbyRoadmapMilestone> Milestones = new();
}

[DataDefinition]
public sealed partial class LobbyRoadmapMilestone
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField(required: true)]
    public LobbyRoadmapStatus Status;

    [DataField]
    public string? Previous;

    [DataField]
    public bool LongTerm;
}

public enum LobbyRoadmapStatus
{
    Available,
    InProgress,
    Planned,
}
