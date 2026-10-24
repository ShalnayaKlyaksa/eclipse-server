using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Tutorial;

/// <summary>
/// Server -> client: show/update the tutorial dialogue box.
/// </summary>
[Serializable, NetSerializable]
public sealed class TutorialShowDialogueEvent : EntityEventArgs
{
    public string Speaker = string.Empty;
    public string Text = string.Empty;
    public string? Portrait;
    public TutorialSide Side = TutorialSide.Left;
    public TutorialAnimation Animation = TutorialAnimation.None;

    /// <summary>
    /// True for a dialogue line (advances on click). False for a task prompt (advances when the player
    /// performs the required action).
    /// </summary>
    public bool CanAdvance = true;

    /// <summary>Task prompts: highlight the health alert with a spotlight and wait for the player to click it.</summary>
    public bool SpotlightHealthAlert;
}

/// <summary>
/// Server -> client: hide the tutorial dialogue box (tutorial ended).
/// </summary>
[Serializable, NetSerializable]
public sealed class TutorialHideEvent : EntityEventArgs
{
}

/// <summary>
/// Client -> server: the player pressed "Далее" to advance a dialogue step.
/// </summary>
[Serializable, NetSerializable]
public sealed class TutorialAdvanceEvent : EntityEventArgs
{
}
