using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>
/// Restores the organ tissue of the head, used by neuroregenerative medicine such as mannitol.
/// </summary>
public sealed partial class RestoreBrainIntegrityEffectSystem
    : EntityEffectSystem<AdvancedHealthComponent, RestoreBrainIntegrity>
{
    protected override void Effect(Entity<AdvancedHealthComponent> entity,
        ref EntityEffectEvent<RestoreBrainIntegrity> args)
    {
        if (!entity.Comp.BodyParts.TryGetValue(BodyPartSlot.Head, out var head))
            return;

        var restored = args.Effect.Amount * args.Scale;
        if (restored <= 0f || head.OrganIntegrity >= head.MaxOrganIntegrity)
            return;

        head.OrganIntegrity = Math.Min(head.MaxOrganIntegrity, head.OrganIntegrity + restored);
        Dirty(entity);
    }
}

/// <summary>Restores brain/head organ integrity by a fixed amount per metabolism effect.</summary>
public sealed partial class RestoreBrainIntegrity : EntityEffectBase<RestoreBrainIntegrity>
{
    [DataField]
    public float Amount = 2f;

    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("entity-effect-guidebook-restore-brain-integrity",
            ("chance", Probability),
            ("amount", Amount));
}
