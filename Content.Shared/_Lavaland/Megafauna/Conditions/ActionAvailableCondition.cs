using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._Lavaland.Megafauna.Conditions;

public sealed partial class ActionAvailableCondition : MegafaunaCondition
{
    [DataField(required: true)]
    public EntProtoId ActionId;

    public override bool EvaluateImplementation(MegafaunaCalculationBaseArgs args)
    {
        var actionSys = args.EntityManager.System<SharedActionsSystem>();

        if (!TryFindActionById(args.EntityManager, actionSys, args.Entity, ActionId, out var action))
            return false;

        return action.Comp.Enabled && !actionSys.IsCooldownActive(action.Comp);
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
