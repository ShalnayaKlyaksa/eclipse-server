using Robust.Shared.Prototypes;

namespace Content.Shared.Eclipse.News;

/// <summary>
/// Marker shown on a news entry. Purely editorial: it changes the badge colour and how loudly the entry is
/// presented, not any game logic.
/// </summary>
public enum EclipseNewsBadge : byte
{
    /// <summary>Ordinary entry, no badge.</summary>
    None,

    /// <summary>"Важное!"</summary>
    Important,

    /// <summary>"Новость дня"</summary>
    DayNews,

    /// <summary>"Экстренное" — the loudest one.</summary>
    Emergency,
}

/// <summary>
/// A single in-world news entry. Defined in YAML so the feed is content, not code, and cannot be edited in game.
/// </summary>
[Prototype]
public sealed partial class EclipseNewsPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; set; } = default!;

    /// <summary>
    /// Lower values are shown first. Ties fall back to the ID.
    /// </summary>
    [DataField]
    public int Order;

    /// <summary>
    /// Free-form date shown above the headline, e.g. "18.05.2024".
    /// </summary>
    [DataField]
    public string Date = string.Empty;

    [DataField(required: true)]
    public string Title = string.Empty;

    /// <summary>
    /// Short teaser shown on cards.
    /// </summary>
    [DataField]
    public string Summary = string.Empty;

    /// <summary>
    /// Full body shown when the entry is opened. Falls back to <see cref="Summary"/> when empty.
    /// </summary>
    [DataField]
    public string Text = string.Empty;

    /// <summary>
    /// Illustration for the card and the reader.
    /// </summary>
    [DataField]
    public string Texture = string.Empty;

    /// <summary>
    /// Editorial marker: "Важное!", "Новость дня" or "Экстренное".
    /// </summary>
    [DataField]
    public EclipseNewsBadge Badge = EclipseNewsBadge.None;

    /// <summary>
    /// Whether this entry is advertised on the lobby's main screen. The news feed always shows everything;
    /// the main screen is meant to carry only the loud and recent headlines.
    /// </summary>
    [DataField]
    public bool Featured = true;

    /// <summary>
    /// Optional byline, e.g. "Пресс-служба станции".
    /// </summary>
    [DataField]
    public string Source = string.Empty;
}
