using Content.Shared.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Server._Eclipse.GameTicking.Components;

[RegisterComponent, Access(typeof(EclipseScenarioRuleSystem))]
public sealed partial class EclipseScenarioRuleComponent : Component
{
    [DataField]
    public int DifficultyOneWeight = 45;

    [DataField]
    public int DifficultyTwoWeight = 35;

    [DataField]
    public int DifficultyThreeWeight = 20;

    [DataField(required: true)]
    public List<EntProtoId> DifficultyOneRules = new();

    [DataField(required: true)]
    public List<EntProtoId> DifficultyTwoRules = new();

    [DataField(required: true)]
    public List<EntProtoId> DifficultyThreeRules = new();

    [DataField]
    public LocId DifficultyOneAnnouncement = "eclipse-extended-difficulty-1";

    [DataField]
    public LocId DifficultyTwoAnnouncement = "eclipse-extended-difficulty-2";

    [DataField]
    public LocId DifficultyThreeAnnouncement = "eclipse-extended-difficulty-3";

    [DataField]
    public Color DifficultyOneColor = Color.FromHex("#35c94b");

    [DataField]
    public Color DifficultyTwoColor = Color.FromHex("#ffd24a");

    [DataField]
    public Color DifficultyThreeColor = Color.FromHex("#d93d3d");

    [DataField]
    public string? AnnouncementSender;

    [DataField]
    public int SelectedDifficulty;

    [DataField]
    public List<EntityUid> SpawnedRules = new();
}
