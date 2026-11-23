using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.ActionBlocker;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._Eclipse.AdvancedHealth;

public sealed class SurgicalEquipmentSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _actions = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SurgicalInstrumentComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<SurgicalInstrumentComponent, DroppedEvent>(OnDrop);
        SubscribeLocalEvent<SurgicalInstrumentComponent, MeleeHitEvent>(OnHit);
        SubscribeLocalEvent<InstrumentSterilizationComponent, AfterInteractEvent>(OnUse);
        SubscribeLocalEvent<SurgicalInstrumentComponent, InstrumentSterilizationDoAfterEvent>(OnSterilized);
    }

    private void OnExamine(Entity<SurgicalInstrumentComponent> ent, ref ExaminedEvent args)
        => args.PushMarkup(Loc.GetString(ent.Comp.Sterile ? "surgery-tool-sterile" : "surgery-tool-contaminated"));

    private void OnDrop(Entity<SurgicalInstrumentComponent> ent, ref DroppedEvent args) => Contaminate(ent);

    private void OnHit(Entity<SurgicalInstrumentComponent> ent, ref MeleeHitEvent args)
    {
        if (args.HitEntities.Count > 0) Contaminate(ent);
    }

    public void Contaminate(EntityUid tool)
    {
        if (!TryComp<SurgicalInstrumentComponent>(tool, out var instrument) || !instrument.Sterile) return;
        instrument.Sterile = false;
        Dirty(tool, instrument);
    }

    public bool CanOperate(EntityUid user, EntityUid tool, AdvancedTreatmentType treatment)
    {
        if (!AdvancedTreatmentRules.IsOperative(treatment)
            || !TryComp<SurgicalInstrumentComponent>(tool, out var instrument) || instrument.Sterile) return true;
        _popup.PopupEntity(Loc.GetString("surgery-sterilize-first"), tool, user);
        return false;
    }

    private bool CanUse(EntityUid user, EntityUid wrap, EntityUid tool)
        => Exists(wrap) && Exists(tool) && _hands.IsHolding(user, wrap)
            && _actions.CanConsciouslyPerformAction(user) && _actions.CanInteract(user, tool)
            && _interaction.InRangeUnobstructed(user, tool)
            && HasComp<InstrumentSterilizationComponent>(wrap)
            && TryComp<SurgicalInstrumentComponent>(tool, out var instrument) && !instrument.Sterile;

    private void OnUse(Entity<InstrumentSterilizationComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } tool || !CanUse(args.User, ent, tool)) return;
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay,
            new InstrumentSterilizationDoAfterEvent(), tool, target: tool, used: ent)
        { NeedHand = true, BreakOnMove = true, BreakOnDamage = true });
    }

    private void OnSterilized(Entity<SurgicalInstrumentComponent> ent, ref InstrumentSterilizationDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target != ent.Owner || args.Used is not { } wrap
            || !CanUse(args.User, wrap, ent)) return;
        ent.Comp.Sterile = true;
        Dirty(ent);
        QueueDel(wrap);
        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("surgery-tool-ready"), ent, args.User);
    }
}
