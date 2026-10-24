using Content.Shared.Roles;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Shared.Traits;

/// <summary>
/// Describes a trait.
/// </summary>
[Prototype]
public sealed partial class TraitPrototype : IPrototype
{
    [ViewVariables]
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The name of this trait.
    /// </summary>
    [DataField]
    public LocId Name { get; private set; } = string.Empty;

    /// <summary>
    /// The description of this trait.
    /// </summary>
    [DataField]
    public LocId? Description { get; private set; }

    /// <summary>
    /// Don't apply this trait to entities this whitelist IS NOT valid for.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// Don't apply this trait to entities this whitelist IS valid for. (hence, a blacklist)
    /// </summary>
    [DataField]
    public EntityWhitelist? Blacklist;

    /// <summary>
    /// The components that get added to the player, when they pick this trait.
    /// NOTE: When implementing a new trait, it's preferable to add it as a status effect instead if possible.
    /// </summary>
    [DataField]
    [Obsolete("Use JobSpecial instead.")]
    public ComponentRegistry Components { get; private set; } = new();

    /// <summary>
    /// Special effects applied to the player who takes this Trait.
    /// </summary>
    [DataField(serverOnly: true)]
    public List<JobSpecial> Specials { get; private set; } = new();

    /// <summary>
    /// Gear that is given to the player, when they pick this trait.
    /// </summary>
    [DataField]
    public EntProtoId? TraitGear;

    /// <summary>
    /// Trait point value. Positive values grant points, negative values spend points, and zero is neutral.
    /// </summary>
    [DataField]
    public int Cost = 0;

    /// <summary>
    /// Adds a trait to a category, allowing you to limit the selection of some traits to the settings of that category.
    /// </summary>
    [DataField]
    public ProtoId<TraitCategoryPrototype>? Category;

    /// <summary>
    /// Other traits that cannot be selected together with this trait.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<TraitPrototype>> Conflicts = new();

    /// <summary>
    /// Jobs this trait should not be used with. This is data for role restriction validation.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<JobPrototype>> RestrictedJobs = new();
}
