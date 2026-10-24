using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._Eclipse.Tutorial;

/// <summary>
/// Base class for a single step in a tutorial lesson. Steps are a polymorphic YAML list
/// (<c>!type:TutorialDialogueStep</c>, ...). See TUTORIAL_GUIDE.md.
/// </summary>
[ImplicitDataDefinitionForInheritors]
public abstract partial class TutorialStep
{
}

/// <summary>
/// Shows a visual-novel style dialogue line and waits for the player to advance.
/// </summary>
public sealed partial class TutorialDialogueStep : TutorialStep
{
    [DataField]
    public string Speaker = string.Empty;

    [DataField]
    public string Text = string.Empty;

    /// <summary>
    /// Portrait: a texture path, or the tokens <c>shape:square</c> / <c>shape:circle</c> to draw a
    /// simple placeholder shape. Empty = no portrait.
    /// </summary>
    [DataField]
    public string? Portrait;

    [DataField]
    public TutorialSide Side = TutorialSide.Left;

    [DataField]
    public TutorialAnimation Animation = TutorialAnimation.None;
}

/// <summary>
/// Waits until the player clicks their health alert icon before advancing. A concrete, single-purpose
/// task; the general Task+trigger system comes later (D5).
/// </summary>
public sealed partial class TutorialClickHealthStep : TutorialStep
{
    [DataField]
    public string Objective = "Нажмите на иконку здоровья.";

    [DataField]
    public string Speaker = "Задание";

    [DataField]
    public string? Portrait;
}

/// <summary>
/// Server action (instant): spawns an entity into the player's hands. Advances immediately.
/// </summary>
public sealed partial class TutorialSpawnInHandStep : TutorialStep
{
    [DataField(required: true)]
    public EntProtoId Item;
}

/// <summary>
/// Server action (instant): deletes everything the player is currently holding. Advances immediately.
/// </summary>
public sealed partial class TutorialClearHandsStep : TutorialStep
{
}

[Serializable, NetSerializable]
public enum TutorialSide : byte
{
    Left,
    Center,
    Right,
}

[Serializable, NetSerializable]
public enum TutorialAnimation : byte
{
    None,
    Bounce,
    Shake,
    SwayLeft,
    SwayRight,
    Nod,
}
