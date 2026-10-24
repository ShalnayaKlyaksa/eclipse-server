using Content.Shared.Ghost;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Mind.Filters;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Station.Components;
using Robust.Shared.Player;

namespace Content.Server.Mind.Filters;

/// <summary>
/// Removes minds that cannot be valid antag objective targets (ghosts, offline players, off-station, dead, etc.).
/// </summary>
public sealed partial class ValidObjectiveTargetMindFilter : MindFilter
{
    protected override bool ShouldRemove(Entity<MindComponent> mind, EntityUid? exclude, IEntityManager entMan)
    {
        if (mind.Comp.OwnedEntity is not {} mob || !entMan.EntityExists(mob))
            return true;

        if (entMan.HasComponent<GhostComponent>(mob))
            return true;

        if (!entMan.HasComponent<HumanoidProfileComponent>(mob))
            return true;

        if (!entMan.TryGetComponent(mob, out MobStateComponent? mobState) ||
            entMan.System<MobStateSystem>().IsDead(mob, mobState))
            return true;

        var xform = entMan.GetComponent<TransformComponent>(mob);
        if (xform.GridUid is not {} grid || !entMan.HasComponent<StationMemberComponent>(grid))
            return true;

        if (!entMan.TryGetComponent(mob, out ActorComponent? actor) || actor.PlayerSession.AttachedEntity != mob)
            return true;

        return false;
    }
}
