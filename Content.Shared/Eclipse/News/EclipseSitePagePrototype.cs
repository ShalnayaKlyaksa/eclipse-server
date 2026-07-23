using Robust.Shared.Prototypes;

namespace Content.Shared.Eclipse.News;

/// <summary>
/// One section of a site page: a heading plus a body paragraph.
/// </summary>
[DataDefinition]
public sealed partial class EclipseSiteSection
{
    [DataField]
    public string Heading = string.Empty;

    [DataField(required: true)]
    public string Body = string.Empty;
}

/// <summary>
/// A page of the in-game imitation of the station's public site. This is flavour only — there is no real site,
/// the pages exist so the lobby can be browsed like one for immersion.
/// </summary>
[Prototype("eclipseSitePage")]
public sealed partial class EclipseSitePagePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; set; } = default!;

    /// <summary>
    /// Order in the site's navigation column. Lower is higher up.
    /// </summary>
    [DataField]
    public int Order;

    /// <summary>
    /// Navigation entry title.
    /// </summary>
    [DataField(required: true)]
    public string Title = string.Empty;

    /// <summary>
    /// Small line under the title in the navigation.
    /// </summary>
    [DataField]
    public string Subtitle = string.Empty;

    /// <summary>
    /// Page contents, rendered top to bottom.
    /// </summary>
    [DataField]
    public List<EclipseSiteSection> Sections = new();

    /// <summary>
    /// When set, this page renders the news feed instead of <see cref="Sections"/>. Exactly one page is
    /// normally expected to set it.
    /// </summary>
    [DataField]
    public bool NewsFeed;
}
