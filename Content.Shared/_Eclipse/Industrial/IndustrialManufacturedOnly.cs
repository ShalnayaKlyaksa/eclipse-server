using Content.Shared.Construction;
using Robust.Shared.Map;

namespace Content.Shared.Construction.Conditions;

/// <summary>Prefabricated parts can still be refitted, but cannot be created through a forged construction request.</summary>
[DataDefinition]
public sealed partial class IndustrialManufacturedOnly : IConstructionCondition
{
    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction) => false;
    public ConstructionGuideEntry GenerateGuideEntry() => new() { Localization = "industrial-manufactured-only" };
}
