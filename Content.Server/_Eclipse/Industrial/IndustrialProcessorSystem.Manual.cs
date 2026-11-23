using Content.Shared._Eclipse.Industrial;
using Content.Shared.DoAfter;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared._Eclipse.Industrial.Prototypes;

namespace Content.Server._Eclipse.Industrial;

public sealed partial class IndustrialProcessorSystem
{
    private void InitializeManual()
    {
        SubscribeLocalEvent<IndustrialManualProcessorComponent, GetVerbsEvent<ActivationVerb>>(OnManualVerb);
        SubscribeLocalEvent<IndustrialManualProcessorComponent, IndustrialManualDoAfterEvent>(OnManualFinished);
        SubscribeLocalEvent<IndustrialManualProcessorComponent, IndustrialManualOperateMessage>(OnManualMessage);
    }

    private void OnManualMessage(Entity<IndustrialManualProcessorComponent> ent, ref IndustrialManualOperateMessage args)
    {
        if (!StartManual(ent, args.Actor))
            _popup.PopupEntity(Loc.GetString("industrial-manual-cannot-operate"), ent, args.Actor);
    }

    private void OnManualVerb(Entity<IndustrialManualProcessorComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.Busy)
            return;
        var user = args.User;
        args.Verbs.Add(new ActivationVerb { Text = Loc.GetString("industrial-manual-operate"), Act = () => StartManual(ent, user) });
    }

    public bool StartManual(Entity<IndustrialManualProcessorComponent> ent, EntityUid user)
    {
        if (ent.Comp.Busy || !TryComp<IndustrialProcessorComponent>(ent, out var comp))
            return false;
        Entity<IndustrialProcessorComponent> processor = (ent, comp);
        if (!CanOperate(processor) || !TryMatchRecipe(processor, out var recipe) || recipe == null || !CanAcceptOutputs(processor, recipe))
            return false;
        var args = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(recipe.Time),
            new IndustrialManualDoAfterEvent(recipe.ID), ent, ent) { BreakOnMove = true, BreakOnDamage = true, DistanceThreshold = 1.5f };
        ent.Comp.Busy = true;
        var started = _fluidDoAfter.TryStartDoAfter(args);
        if (!started)
            ent.Comp.Busy = false;
        UpdateAppearance(processor);
        UpdateUserInterface(processor);
        return started;
    }

    private void OnManualFinished(Entity<IndustrialManualProcessorComponent> ent, ref IndustrialManualDoAfterEvent args)
    {
        ent.Comp.Busy = false;
        if (args.Cancelled || args.Handled || !TryComp<IndustrialProcessorComponent>(ent, out var comp) ||
            !PrototypeManager.TryIndex(args.Recipe, out IndustrialRecipePrototype? recipe))
            return;
        Entity<IndustrialProcessorComponent> processor = (ent, comp);
        if (CanOperate(processor) && IsRecipeAvailable(processor, recipe) &&
            CanAcceptOutputs(processor, recipe) && HasFluidIngredients(processor, recipe))
            args.Handled = TryCompleteRecipe(processor, recipe);
        UpdateAppearance(processor);
        UpdateUserInterface(processor);
    }
}

