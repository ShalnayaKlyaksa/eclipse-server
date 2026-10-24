using Robust.Shared.Prototypes;

namespace Content.Shared.Traits;

/// <summary>
/// Traits category with general settings. Allows you to limit the number of taken traits in one category
/// </summary>
[Prototype]
public sealed partial class TraitCategoryPrototype : IPrototype
{
    public const string Default = "Default";
    public const string Disadvantages = "Disadvantages";
    public const string Advantages = "Advantages";
    public const string Neutral = "Neutral";
    public const string Speech = "SpeechTraits";
    public const string Languages = "Languages";

    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    ///     Name of the trait category displayed in the UI
    /// </summary>
    [DataField]
    public LocId Name { get; private set; } = string.Empty;

    /// <summary>
    ///     The maximum number of traits that can be taken in this category.
    /// </summary>
    [DataField]
    public int? MaxTraitPoints;
}
