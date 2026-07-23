using Robust.Shared.Prototypes;

namespace Content.Shared.Eclipse.Achievements;

/// <summary>
/// What an achievement measures. Every kind is derived from data the client already has, so achievements
/// work without any server-side backend.
/// </summary>
public enum EclipseAchievementKind : byte
{
    /// <summary>Total playtime across all roles, in hours.</summary>
    Playtime,

    /// <summary>Account level from <see cref="Progression.EclipseProgression"/>.</summary>
    Level,

    /// <summary>Playtime in the role named by <see cref="EclipseAchievementPrototype.Tracker"/>, in hours.</summary>
    RolePlaytime,

    /// <summary>Number of distinct roles played for at least an hour.</summary>
    RoleVariety,
}

/// <summary>
/// A single account achievement. Defined in YAML so the list is content, not code.
/// </summary>
[Prototype("eclipseAchievement")]
public sealed partial class EclipseAchievementPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; set; } = default!;

    /// <summary>
    /// Lower values are listed first. Ties fall back to the ID.
    /// </summary>
    [DataField]
    public int Order;

    [DataField(required: true)]
    public string Name = string.Empty;

    [DataField]
    public string Description = string.Empty;

    [DataField]
    public string Icon = string.Empty;

    [DataField]
    public EclipseAchievementKind Kind = EclipseAchievementKind.Playtime;

    /// <summary>
    /// Value that counts as complete: hours for playtime kinds, the level for <see cref="EclipseAchievementKind.Level"/>,
    /// or the number of roles for <see cref="EclipseAchievementKind.RoleVariety"/>.
    /// </summary>
    [DataField]
    public float Goal = 1f;

    /// <summary>
    /// Playtime tracker id, only used by <see cref="EclipseAchievementKind.RolePlaytime"/>.
    /// </summary>
    [DataField]
    public string Tracker = string.Empty;
}
