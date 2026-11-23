using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Server.Research.Components;

[RegisterComponent]
public sealed partial class ResearchConsoleComponent : Component
{
    // Session belongs to the physical terminal; closing its UI does not log it out.
    public string? Username;
    public EntityUid? AccountServer;
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

