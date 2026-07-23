using Robust.Shared.Map;

namespace Content.Server._Eclipse.Tutorial;

/// <summary>
/// Marks an entity as a player's tutorial body. Used to identify tutorial players (for isolation, D2b)
/// and to know which map to clean up.
/// </summary>
[RegisterComponent]
public sealed partial class TutorialSessionComponent : Component
{
    [DataField]
    public string LessonId = string.Empty;

    [ViewVariables]
    public MapId MapId;
}
