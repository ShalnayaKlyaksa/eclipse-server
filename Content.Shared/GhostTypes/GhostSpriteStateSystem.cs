using Content.Shared.Ghost;
using Content.Shared.Humanoid;
using Content.Shared.Mind;

namespace Content.Shared.GhostTypes;

public sealed class GhostSpriteStateSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    /// <summary>
    /// Selects a ghost sprite based only on the sex of its last body.
    /// </summary>
    public void SetGhostSprite(Entity<GhostSpriteStateComponent?> ent, EntityUid mind, Sex sex = Sex.Male)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        if (!TryComp<AppearanceComponent>(ent, out var appearance) || !HasComp<MindComponent>(mind))
            return;

        var variant = sex == Sex.Female ? "Female" : "Male";
        _appearance.SetData(ent, GhostVisuals.Variant, variant, appearance);
    }
}
