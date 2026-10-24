using Content.Shared._Lavaland.Megafauna.Components;
using Content.Shared._Lavaland.Megafauna.Systems;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Lavaland.Megafauna.Selectors;

/// <summary>
/// Performs an action and if required, tries to get target positions
/// from <see cref="MegafaunaAiTargetingComponent"/>.
/// </summary>
public sealed partial class PerformActionSelector : MegafaunaSelector
{
    [DataField]
    public EntProtoId ActionId;

    protected override float InvokeImplementation(MegafaunaCalculationBaseArgs args)
    {
        var entMan = args.EntityManager;
        var actionSys = entMan.System<SharedActionsSystem>();
        var megafaunaSys = entMan.System<MegafaunaSystem>();

        if (!TryFindActionById(args.EntityManager, actionSys, args.Entity, ActionId, out var action))
        {
            DebugTools.Assert($"{entMan.ToPrettyString(args.Entity)}'s AI failed to get an action with ID {ActionId}!");
            return FailDelay;
        }

        var targetingComp = entMan.GetComponentOrNull<MegafaunaAiTargetingComponent>(args.Entity);
        var ev = actionSys.GetEvent(action.Owner);

        if (ev is EntityTargetActionEvent entityTarget && targetingComp?.TargetEnt is { } targetEnt)
            entityTarget.Target = targetEnt;

        if (ev is WorldTargetActionEvent worldTarget && targetingComp?.TargetCoords is { } targetCoords)
            worldTarget.Target = targetCoords;

        actionSys.PerformAction(args.Entity, action, ev, predicted: false);

        return DelaySelector.Get(args);
    }

    private bool TryFindActionById(
        IEntityManager entMan,
        SharedActionsSystem actions,
        EntityUid holder,
        EntProtoId actionId,
        out Entity<ActionComponent> action)
    {
        foreach (var candidate in actions.GetActions(holder))
        {
            if (entMan.GetComponent<MetaDataComponent>(candidate).EntityPrototype?.ID != actionId.Id)
                continue;

            action = candidate;
            return true;
        }

        action = default;
        return false;
    }
}
