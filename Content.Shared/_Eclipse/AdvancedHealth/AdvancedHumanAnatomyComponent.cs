using Content.Shared.Humanoid;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>Additional organs for humanoids with standard digestive anatomy.</summary>
[RegisterComponent]
public sealed partial class AdvancedHumanAnatomyComponent : Component
{
    public bool Ready;
    public Sex? AppliedSex;
    public readonly List<EntityUid> ReproductiveOrgans = new();
}
