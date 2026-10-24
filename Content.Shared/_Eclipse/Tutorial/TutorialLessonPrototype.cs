using System.Collections.Generic;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Eclipse.Tutorial;

/// <summary>
/// A single tutorial lesson shown in the lobby "Обучение" window. Lessons are data-driven so new ones
/// can be authored in YAML. The step sequence (dialogue/spotlight/tasks) is added on top of this in later
/// stages of the tutorial system.
/// </summary>
[Prototype]
public sealed partial class TutorialLessonPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Display name shown in the lesson list.</summary>
    [DataField]
    public string Name = string.Empty;

    /// <summary>Short description shown under the name.</summary>
    [DataField]
    public string Description = string.Empty;

    /// <summary>Icon texture path shown next to the lesson.</summary>
    [DataField]
    public string Icon = "/Textures/Interface/VerbIcons/examine.svg.192dpi.png";

    /// <summary>Sort order in the list (lower is higher up).</summary>
    [DataField]
    public int Order;

    /// <summary>
    /// Whether the lesson can be started. Lessons still being built can be listed but disabled so
    /// players can see what's coming.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>
    /// Optional grid map file to load for this lesson. If null, a bare empty map is created and the
    /// player is spawned floating at its origin.
    /// </summary>
    [DataField]
    public ResPath? Map;

    /// <summary>Entity prototype spawned as the player's body for the lesson.</summary>
    [DataField]
    public EntProtoId Body = "MobHuman";

    /// <summary>The ordered sequence of steps that make up the lesson.</summary>
    [DataField]
    public List<TutorialStep> Steps = new();
}
