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

public sealed class IndustrialWorkbenchSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

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
        TryStartFabrication(ent, args.Actor, args.RecipeId);
    }

    private bool TryStartFabrication(Entity<IndustrialWorkbenchComponent> ent, EntityUid user, string recipeId)
    {
        if (!_proto.TryIndex(recipeId, out IndustrialFabricationRecipePrototype? recipe))
            return false;

        if (!CanAfford(user, ent, recipe))
        {
            _popup.PopupCursor(Loc.GetString("industrial-workbench-missing-materials"), user);
            return false;
        }

        var doAfter = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(recipe.Duration), new IndustrialFabricationDoAfterEvent(recipeId), ent, ent)
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

        if (!CanAfford(args.User, ent, recipe))
        {
            _popup.PopupCursor(Loc.GetString("industrial-workbench-missing-materials"), args.User);
            return;
        }

        if (!TryConsumeIngredients(args.User, ent, recipe))
        {
            _popup.PopupCursor(Loc.GetString("industrial-workbench-missing-materials"), args.User);
            return;
        }

        args.Handled = true;

        var coords = _transform.GetMapCoordinates(ent);
        for (var i = 0; i < recipe.ResultCount; i++)
            Spawn(recipe.Result, coords);

        _popup.PopupPredicted(Loc.GetString("industrial-workbench-fabricated"), args.User, ent);
    }

    private bool CanAfford(EntityUid user, EntityUid workbench, IndustrialFabricationRecipePrototype recipe)
    {
        var remaining = new Dictionary<string, int>(recipe.Ingredients);
        CountFromStorage(workbench, remaining);
        if (remaining.Count == 0)
            return true;

        foreach (var hand in _hands.EnumerateHeld(user))
            CountFromItem(hand, remaining);

        return remaining.Count == 0;
    }

    private bool TryConsumeIngredients(EntityUid user, EntityUid workbench, IndustrialFabricationRecipePrototype recipe)
    {
        var remaining = new Dictionary<string, int>(recipe.Ingredients);

        if (TryComp<StorageComponent>(workbench, out var storage))
        {
            foreach (var item in storage.Container.ContainedEntities.ToArray())
            {
                if (!TryTakeFromItem(item, remaining, out var deleteItem))
                    continue;

                if (deleteItem)
                    Del(item);

                if (remaining.Count == 0)
                    return true;
            }
        }

        foreach (var hand in _hands.EnumerateHeld(user).ToArray())
        {
            if (!TryTakeFromItem(hand, remaining, out var deleteItem))
                continue;

            if (deleteItem)
                Del(hand);

            if (remaining.Count == 0)
                return true;
        }

        return remaining.Count == 0;
    }

    private void CountFromStorage(EntityUid workbench, Dictionary<string, int> remaining)
    {
        if (!TryComp<StorageComponent>(workbench, out var storage))
            return;

        foreach (var item in storage.Container.ContainedEntities)
            CountFromItem(item, remaining);
    }

    private void CountFromItem(EntityUid item, Dictionary<string, int> remaining)
    {
        if (remaining.Count == 0)
            return;

        var protoId = MetaData(item).EntityPrototype?.ID;
        if (protoId == null || !remaining.TryGetValue(protoId, out var needed))
            return;

        if (TryComp<StackComponent>(item, out var stack))
        {
            if (stack.Count < needed)
                return;

            remaining[protoId] = needed - stack.Count;
            if (remaining[protoId] <= 0)
                remaining.Remove(protoId);

            return;
        }

        remaining[protoId] = needed - 1;
        if (remaining[protoId] <= 0)
            remaining.Remove(protoId);
    }

    private bool TryTakeFromItem(EntityUid item, Dictionary<string, int> remaining, out bool deleteItem)
    {
        deleteItem = false;

        if (remaining.Count == 0)
            return false;

        var protoId = MetaData(item).EntityPrototype?.ID;
        if (protoId == null || !remaining.TryGetValue(protoId, out var needed))
            return false;

        if (TryComp<StackComponent>(item, out var stack))
        {
            if (stack.Count <= needed)
            {
                remaining[protoId] = needed - stack.Count;
                if (remaining[protoId] <= 0)
                    remaining.Remove(protoId);

                deleteItem = true;
                return true;
            }

            _stack.SetCount(item, stack.Count - needed, stack);
            remaining.Remove(protoId);
            return true;
        }

        remaining[protoId] = needed - 1;
        if (remaining[protoId] <= 0)
            remaining.Remove(protoId);

        deleteItem = true;
        return true;
    }
}
