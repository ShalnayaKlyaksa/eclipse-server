using System.Linq;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Shared._Eclipse.Industrial;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.Industrial;

public sealed partial class IndustrialWorkbenchSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IndustrialResourceSystem _resources = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IndustrialWorkbenchComponent, GetVerbsEvent<ActivationVerb>>(OnGetVerbs);
        SubscribeLocalEvent<IndustrialWorkbenchComponent, IndustrialWorkbenchCraftBuiMessage>(OnCraftMessage);
        SubscribeLocalEvent<IndustrialWorkbenchComponent, IndustrialFabricationDoAfterEvent>(OnFabricationFinished);
    }

    private void OnGetVerbs(Entity<IndustrialWorkbenchComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!HasComp<StorageComponent>(ent))
            return;

        var user = args.User;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("industrial-workbench-open-storage"),
            Act = () =>
            {
                if (!_ui.TryOpenUi(ent.Owner, StorageComponent.StorageUiKey.Key, user))
                    _popup.PopupCursor(Loc.GetString("industrial-workbench-storage-failed"), user);
            },
        });
    }

    private void OnCraftMessage(Entity<IndustrialWorkbenchComponent> ent, ref IndustrialWorkbenchCraftBuiMessage args)
    {
        TryStartFabrication(ent, args.Actor, args.RecipeId, args.Amount);
    }

    private bool TryStartFabrication(Entity<IndustrialWorkbenchComponent> ent, EntityUid user, string recipeId, int amount)
    {
        if (amount < 1 || amount > 10 || !_proto.TryIndex(recipeId, out IndustrialFabricationRecipePrototype? recipe))
            return false;

        if (ent.Comp.Tier < recipe.MinimumWorkbenchTier)
            return false;
        if (recipe.CleanroomGrade > 0 && (!TryComp<RoomSanitationComponent>(ent, out var room) || room.Grade < recipe.CleanroomGrade))
            return false;
        if (recipe.RequiredTechnology is { } technology &&
            (!TryComp<IndustrialMachineKnowledgeComponent>(ent, out var knowledge) || !knowledge.Technologies.Contains(technology)))
            return false;

        if (!_resources.CanAfford(Ingredients(user, ent), recipe.Ingredients, amount))
        {
            _popup.PopupCursor(Loc.GetString("industrial-workbench-missing-materials"), user);
            return false;
        }

        var doAfter = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(recipe.Duration * amount / ent.Comp.CraftSpeed), new IndustrialFabricationDoAfterEvent(recipeId, amount), ent, ent)
        {
            BreakOnMove = true,
            NeedHand = false,
            DistanceThreshold = 2f,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return false;

        _popup.PopupCursor(Loc.GetString("industrial-workbench-fabricating"), user);
        return true;
    }

    private void OnFabricationFinished(Entity<IndustrialWorkbenchComponent> ent, ref IndustrialFabricationDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        if (!_proto.TryIndex(args.RecipeId, out IndustrialFabricationRecipePrototype? recipe))
            return;

        if (ent.Comp.Tier < recipe.MinimumWorkbenchTier)
            return;
        if (recipe.CleanroomGrade > 0 && (!TryComp<RoomSanitationComponent>(ent, out var room) || room.Grade < recipe.CleanroomGrade))
            return;
        if (recipe.RequiredTechnology is { } technology &&
            (!TryComp<IndustrialMachineKnowledgeComponent>(ent, out var knowledge) || !knowledge.Technologies.Contains(technology)))
            return;

        if (args.Amount < 1 || args.Amount > 10 ||
            !_resources.TryPlan(Ingredients(args.User, ent), recipe.Ingredients, out var plan, args.Amount))
        {
            _popup.PopupCursor(Loc.GetString("industrial-workbench-missing-materials"), args.User);
            return;
        }
        foreach (var (item, count) in plan)
        {
            if (TryComp<StackComponent>(item, out var stack))
                _stack.TryUse((item, stack), count);
            else
                Del(item);
        }

        args.Handled = true;

        var coords = _transform.GetMapCoordinates(args.User);
        for (var i = 0; i < recipe.ResultCount * args.Amount; i++)
            Spawn(recipe.Result, coords);

        _popup.PopupPredicted(Loc.GetString("industrial-workbench-fabricated"), args.User, ent);
    }

    private IEnumerable<EntityUid> Ingredients(EntityUid user, EntityUid workbench)
    {
        if (TryComp<StorageComponent>(workbench, out var storage))
        {
            foreach (var item in storage.Container.ContainedEntities)
                yield return item;
        }
        foreach (var item in _hands.EnumerateHeld(user))
            yield return item;
    }
}
