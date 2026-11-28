using Content.Shared.Radio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Research.Components;

[RegisterComponent]
public sealed partial class ResearchConsoleComponent : Component
{
    // Crew sessions belong to the physical terminal. Staff sessions belong to the player using it.
    public string? Username;
    public EntityUid? AccountServer;
    public (EntityUid Actor, ICommonSession Player)? AdminSession;
    public int Address;
    public readonly List<string> TerminalLines = new();
    public TimeSpan NextAuthentication;
    public TimeSpan NextCommand;
    public TimeSpan NextAnalysis;
    public ResearchAnalysis? Analysis;
    public readonly List<string> PendingResearch = new();
    public EntityUid? QueueActor;
    public bool QueueProcessing;

    /// <summary>
    /// The radio channel that the unlock announcements are broadcast to.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<RadioChannelPrototype> AnnouncementChannel = "Science";
}

