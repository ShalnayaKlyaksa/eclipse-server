using System.Linq;
using System.Numerics;
using Content.Server.Bible.Components;
using Content.Server.Mind;
using Content.Shared._Eclipse.Poltergeist;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Bible;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Hands.Components;
using Content.Shared.Light;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Magic.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Nuke;
using Content.Shared.Radio.Components;
using Content.Shared.Radio.EntitySystems;
using Content.Shared.Speech;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Eclipse.Poltergeist;

public sealed class PoltergeistSystem : EntitySystem
{
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedEyeSystem _eye = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly Content.Shared.Popups.SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedEntityStorageSystem _storage = default!;
    [Dependency] private readonly SharedHandheldLightSystem _handheldLight = default!;
    [Dependency] private readonly SharedPoweredLightSystem _poweredLight = default!;
    [Dependency] private readonly SharedRadioDeviceSystem _radio = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private readonly HashSet<EntityUid> _pendingRelease = new();

    private static readonly string[] ActionPrototypes =
    [
        "ActionPoltergeistPossess", "ActionPoltergeistLeave", "ActionPoltergeistMove",
        "ActionPoltergeistRotate", "ActionPoltergeistKnock", "ActionPoltergeistUse", "ActionPoltergeistThrow",
    ];

    public override void Initialize()
    {
        SubscribeLocalEvent<PoltergeistComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<PoltergeistComponent, EntityTerminatingEvent>(OnSpiritTerminating, before: [typeof(MindSystem)]);
        SubscribeLocalEvent<PoltergeistComponent, PoltergeistPossessActionEvent>(OnPossess);
        SubscribeLocalEvent<PoltergeistComponent, PoltergeistLeaveActionEvent>(OnLeave);
        SubscribeLocalEvent<PoltergeistComponent, PoltergeistMoveActionEvent>(OnMove);
        SubscribeLocalEvent<PoltergeistComponent, PoltergeistThrowActionEvent>(OnThrow);
        SubscribeLocalEvent<PoltergeistComponent, PoltergeistRotateActionEvent>(OnRotate);
        SubscribeLocalEvent<PoltergeistComponent, PoltergeistKnockActionEvent>(OnKnock);
        SubscribeLocalEvent<PoltergeistComponent, PoltergeistUseActionEvent>(OnUse);
        SubscribeLocalEvent<PoltergeistComponent, PoltergeistExorciseDoAfterEvent>(OnExorcise);
        SubscribeLocalEvent<PoltergeistComponent, ExaminedEvent>(OnSpiritExamined);
        SubscribeLocalEvent<PoltergeistPossessedComponent, EntityTerminatingEvent>(OnHostTerminating, before: [typeof(MindSystem)]);
        SubscribeLocalEvent<PoltergeistPossessedComponent, MindRemovedMessage>(OnHostMindRemoved);
        SubscribeLocalEvent<PoltergeistPossessedComponent, ExaminedEvent>(OnHostExamined);
        SubscribeLocalEvent<PoltergeistPossessedComponent, GetVerbsEvent<AlternativeVerb>>(OnExorciseVerb);
        SubscribeLocalEvent<PoltergeistPossessedComponent, InteractUsingEvent>(OnBible);
        SubscribeLocalEvent<PoltergeistDetectorComponent, UseInHandEvent>(OnScan);
        SubscribeLocalEvent<PoltergeistDetectorComponent, AfterInteractEvent>(OnSeal);
    }

    private void OnInit(Entity<PoltergeistComponent> ent, ref MapInitEvent args)
    {
        foreach (var prototype in ActionPrototypes)
        {
            if (_actions.AddAction(ent, prototype) is { } action)
                ent.Comp.Actions.Add(action);
        }
    }

    private void OnPossess(Entity<PoltergeistComponent> ent, ref PoltergeistPossessActionEvent args)
    {
        if (!args.Handled && IsController(ent, args.Performer))
            args.Handled = TryPossess(ent, args.Target);
    }

    private bool IsController(Entity<PoltergeistComponent> ent, EntityUid performer) =>
        performer == (ent.Comp.Host ?? ent.Owner);

    /// <summary>Validates the target on the server; normal access rules also exclude held/container items.</summary>
    public bool CanPossess(EntityUid target)
    {
        return !TerminatingOrDeleted(target)
            && !HasComp<PoltergeistComponent>(target)
            && !HasComp<PoltergeistPossessedComponent>(target)
            && !HasComp<MindContainerComponent>(target)
            && !HasComp<MobStateComponent>(target)
            && !HasComp<NukeDiskComponent>(target)
            && !_containers.IsEntityOrParentInContainer(target)
            && HasComp<PhysicsComponent>(target)
            && (HasComp<ItemComponent>(target) || HasComp<AnimateableComponent>(target)
                || HasComp<PoweredLightComponent>(target));
    }

    public bool TryPossess(Entity<PoltergeistComponent> ent, EntityUid target)
    {
        if (IsSealed(ent))
            return Failure(ent.Comp.Host ?? ent.Owner, "poltergeist-blocked-sealed");
        if (_timing.CurTime < ent.Comp.PossessAfter || !CanPossess(target)
            || !_interaction.InRangeUnobstructed(ent.Comp.Host ?? ent.Owner, target, range: ent.Comp.PossessionRange)
            || !_mind.TryGetMind(ent.Comp.Host ?? ent.Owner, out var mind, out _))
            return Failure(ent.Comp.Host ?? ent.Owner, "poltergeist-cannot-possess");

        if (ent.Comp.Energy < 10)
            return Failure(ent.Comp.Host ?? ent.Owner, "poltergeist-no-energy");

        Leave(ent);
        ent.Comp.Energy -= 10;
        ent.Comp.Host = target;
        _blocker.UpdateCanMove(ent);
        _physics.SetLinearVelocity(ent, Vector2.Zero);
        ent.Comp.Mind = mind;
        ent.Comp.PossessAfter = _timing.CurTime + TimeSpan.FromSeconds(2);
        var possessed = AddComp<PoltergeistPossessedComponent>(target);
        possessed.Spirit = ent;
        AddTemporary<MindContainerComponent>(ent, target);
        AddTemporary<EyeComponent>(ent, target);
        _eye.RefreshVisibilityMask(target);
        AddTemporary<SpeechComponent>(ent, target);
        AddTemporary<HandsComponent>(ent, target);
        AddTemporary<ActionsComponent>(ent, target);
        foreach (var action in ent.Comp.Actions)
            _actions.AddActionDirect(target, action);
        _transform.SetCoordinates(ent, Transform(target).Coordinates);
        ent.Comp.Transferring = true;
        _mind.TransferTo(mind, target);
        ent.Comp.Transferring = false;
        _popup.PopupEntity(Loc.GetString("poltergeist-possessed", ("target", target)), target, target);
        return true;
    }

    private void AddTemporary<T>(Entity<PoltergeistComponent> ent, EntityUid host) where T : Component, new()
    {
        if (HasComp<T>(host))
            return;
        AddComp<T>(host);
        ent.Comp.AddedComponents.Add(typeof(T));
    }

    public void Leave(Entity<PoltergeistComponent> ent)
    {
        if (ent.Comp.Host is not { } host)
            return;

        ent.Comp.Transferring = true;
        ent.Comp.Host = null;
        _blocker.UpdateCanMove(ent);
        if (Exists(host))
            _transform.SetCoordinates(ent, _transform.GetMoverCoordinates(host));
        foreach (var action in ent.Comp.Actions)
        {
            if (!TerminatingOrDeleted(ent))
                _actions.AddActionDirect(ent.Owner, action);
        }

        // A mind moved away by an administrator or a different system must not be stolen back.
        if (ent.Comp.Mind is { } mind && _mind.TryGetMind(host, out var current, out _) && current == mind)
            _mind.TransferTo(mind, TerminatingOrDeleted(ent) ? null : ent.Owner);
        ent.Comp.Mind = null;
        if (!TerminatingOrDeleted(host))
        {
            RemComp<PoltergeistPossessedComponent>(host);
            foreach (var type in ent.Comp.AddedComponents.AsEnumerable().Reverse())
                RemComp(host, type);
            if (HasComp<EyeComponent>(host))
                _eye.RefreshVisibilityMask(host);
        }
        ent.Comp.AddedComponents.Clear();
        ent.Comp.Transferring = false;
    }

    private void OnLeave(Entity<PoltergeistComponent> ent, ref PoltergeistLeaveActionEvent args)
    {
        if (args.Handled || !IsController(ent, args.Performer) || ent.Comp.Host == null)
            return;
        if (IsSealed(ent))
        {
            Failure(args.Performer, "poltergeist-blocked-sealed");
            return;
        }
        Leave(ent);
        args.Handled = true;
    }

    private bool IsSealed(Entity<PoltergeistComponent> ent) =>
        TryComp<PoltergeistPossessedComponent>(ent.Comp.Host, out var host) && host.SealedUntil > _timing.CurTime;

    private bool TrySpend(Entity<PoltergeistComponent> ent, EntityUid performer, float cost)
    {
        if (!IsController(ent, performer) || ent.Comp.Host == null)
            return false;
        if (IsSealed(ent))
            return Failure(performer, "poltergeist-blocked-sealed");
        if (ent.Comp.Energy < cost)
            return Failure(performer, "poltergeist-no-energy");
        ent.Comp.Energy -= cost;
        if (TryComp<PoltergeistPossessedComponent>(ent.Comp.Host, out var host))
            host.RevealedUntil = _timing.CurTime + TimeSpan.FromSeconds(5);
        return true;
    }

    private bool Failure(EntityUid user, string message)
    {
        _popup.PopupEntity(Loc.GetString(message), user, user);
        return false;
    }

    private bool CanMoveHost(Entity<PoltergeistComponent> ent, float massLimit, out EntityUid host)
    {
        host = ent.Comp.Host ?? EntityUid.Invalid;
        return Exists(host) && !Transform(host).Anchored
            && !_containers.IsEntityOrParentInContainer(host)
            && TryComp<PhysicsComponent>(host, out var physics) && physics.BodyType == BodyType.Dynamic
            && physics.Mass <= massLimit;
    }

    private void OnMove(Entity<PoltergeistComponent> ent, ref PoltergeistMoveActionEvent args)
    {
        if (args.Handled || !CanMoveHost(ent, ent.Comp.MaxMoveMass, out var host)
            || !_interaction.InRangeUnobstructed(host, args.Target, range: 1.5f)
            || !TrySpend(ent, args.Performer, 2))
            return;
        _throwing.TryThrow(host, args.Target, 2, user: host, pushbackRatio: 0, recoil: false, animated: false, playSound: false, doSpin: false);
        args.Handled = true;
    }

    private void OnThrow(Entity<PoltergeistComponent> ent, ref PoltergeistThrowActionEvent args)
    {
        if (args.Handled || !CanMoveHost(ent, ent.Comp.MaxThrowMass, out var host)
            || !_interaction.InRangeUnobstructed(host, args.Target, range: 5)
            || !TrySpend(ent, args.Performer, 25))
            return;
        _throwing.TryThrow(host, args.Target, 7, user: host, pushbackRatio: 0, recoil: false);
        args.Handled = true;
    }

    private void OnRotate(Entity<PoltergeistComponent> ent, ref PoltergeistRotateActionEvent args)
    {
        if (args.Handled || !CanMoveHost(ent, ent.Comp.MaxMoveMass, out var host) || !TrySpend(ent, args.Performer, 1))
            return;
        _transform.SetLocalRotation(host, Transform(host).LocalRotation + Angle.FromDegrees(90));
        args.Handled = true;
    }

    private void OnKnock(Entity<PoltergeistComponent> ent, ref PoltergeistKnockActionEvent args)
    {
        if (args.Handled || !TrySpend(ent, args.Performer, 2) || ent.Comp.Host is not { } host)
            return;
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/glass_knock.ogg"), host);
        _popup.PopupEntity(Loc.GetString("poltergeist-knock", ("target", host)), host);
        args.Handled = true;
    }

    private void OnUse(Entity<PoltergeistComponent> ent, ref PoltergeistUseActionEvent args)
    {
        if (args.Handled || ent.Comp.Host is not { } host
            || !(HasComp<EntityStorageComponent>(host) || HasComp<HandheldLightComponent>(host)
                || HasComp<PoweredLightComponent>(host) || HasComp<RadioMicrophoneComponent>(host))
            || !TrySpend(ent, args.Performer, 5))
            return;
        if (TryComp<EntityStorageComponent>(host, out var storage))
            _storage.ToggleOpen(host, host, storage);
        else if (TryComp<HandheldLightComponent>(host, out var light))
        {
            if (light.Activated)
                _handheldLight.TurnOff((host, light));
            else
                _handheldLight.TurnOn(host, (host, light));
        }
        else if (HasComp<PoweredLightComponent>(host))
            _poweredLight.ToggleLight(host);
        else
            _radio.ToggleRadioMicrophone(host, host);
        args.Handled = true;
    }

    private void OnScan(Entity<PoltergeistDetectorComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || _timing.CurTime < ent.Comp.NextUse)
            return;
        ent.Comp.NextUse = _timing.CurTime + TimeSpan.FromSeconds(3);
        var found = 0;
        var query = EntityQueryEnumerator<PoltergeistPossessedComponent, TransformComponent>();
        while (query.MoveNext(out var host, out var possessed, out _))
        {
            if (!_transform.InRange(Transform(args.User).Coordinates, Transform(host).Coordinates, ent.Comp.Range))
                continue;
            possessed.RevealedUntil = _timing.CurTime + TimeSpan.FromSeconds(10);
            found++;
        }
        _popup.PopupEntity(Loc.GetString(found > 0 ? "poltergeist-scan-found" : "poltergeist-scan-clear", ("count", found)), args.User, args.User);
        args.Handled = true;
    }

    private void OnSeal(Entity<PoltergeistDetectorComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } host || _timing.CurTime < ent.Comp.NextUse)
            return;
        ent.Comp.NextUse = _timing.CurTime + TimeSpan.FromSeconds(3);
        if (TryComp<PoltergeistPossessedComponent>(host, out var possessed))
        {
            possessed.SealedUntil = _timing.CurTime + ent.Comp.SealDuration;
            possessed.RevealedUntil = possessed.SealedUntil;
            _popup.PopupEntity(Loc.GetString("poltergeist-sealed", ("target", host)), host);
        }
        else
            _popup.PopupEntity(Loc.GetString("poltergeist-scan-clear"), args.User, args.User);
        args.Handled = true;
    }

    private void OnBible(Entity<PoltergeistPossessedComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<BibleComponent>(args.Used) || !HasComp<BibleUserComponent>(args.User))
            return;
        args.Handled = true;
        StartExorcism(ent, ent.Comp, args.User, args.Used);
    }

    private void OnExorciseVerb(Entity<PoltergeistPossessedComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.SealedUntil <= _timing.CurTime)
            return;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("poltergeist-exorcise-verb"),
            Act = () => StartExorcism(ent, ent.Comp, user, null),
        });
    }

    private void StartExorcism(EntityUid host, PoltergeistPossessedComponent possessed, EntityUid user, EntityUid? bible)
    {
        if (!_blocker.CanInteract(user, host) || !_interaction.InRangeUnobstructed(user, host)
            || possessed.SealedUntil <= _timing.CurTime)
            return;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, bible == null ? 8f : 4f,
            new PoltergeistExorciseDoAfterEvent(), possessed.Spirit, target: host, used: bible)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            DistanceThreshold = 1.5f,
        });
    }

    private void OnExorcise(Entity<PoltergeistComponent> ent, ref PoltergeistExorciseDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || ent.Comp.Host is not { } host || args.Target != host || !IsSealed(ent))
            return;
        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("poltergeist-exorcised", ("target", host)), host);
        Leave(ent);
        Spawn("Ectoplasm", Transform(ent).Coordinates);
        if (_mind.TryGetMind(ent, out var mind, out _))
            _mind.TransferTo(mind, null);
        QueueDel(ent);
    }

    private void OnSpiritExamined(Entity<PoltergeistComponent> ent, ref ExaminedEvent args) => ShowEnergy(ent, args);

    private void OnHostExamined(Entity<PoltergeistPossessedComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.RevealedUntil > _timing.CurTime)
            args.PushMarkup(Loc.GetString(ent.Comp.SealedUntil > _timing.CurTime ? "poltergeist-examine-sealed" : "poltergeist-examine-haunted"));
        if (args.Examiner == ent.Owner && TryComp<PoltergeistComponent>(ent.Comp.Spirit, out var spirit))
            ShowEnergy((ent.Comp.Spirit, spirit), args);
    }

    private void ShowEnergy(Entity<PoltergeistComponent> ent, ExaminedEvent args)
    {
        if (args.Examiner == (ent.Comp.Host ?? ent.Owner))
            args.PushMarkup(Loc.GetString("poltergeist-energy", ("energy", (int)ent.Comp.Energy), ("max", (int)ent.Comp.MaxEnergy)));
    }

    private void OnHostMindRemoved(Entity<PoltergeistPossessedComponent> ent, ref MindRemovedMessage args)
    {
        if (TryComp<PoltergeistComponent>(ent.Comp.Spirit, out var spirit) && !spirit.Transferring)
            // MindSystem still uses the old MindContainer after raising this notification.
            _pendingRelease.Add(ent.Comp.Spirit);
    }

    private void OnHostTerminating(Entity<PoltergeistPossessedComponent> ent, ref EntityTerminatingEvent args)
    {
        if (TryComp<PoltergeistComponent>(ent.Comp.Spirit, out var spirit))
            Leave((ent.Comp.Spirit, spirit));
    }

    private void OnSpiritTerminating(Entity<PoltergeistComponent> ent, ref EntityTerminatingEvent args) => Leave(ent);

    public override void Update(float frameTime)
    {
        foreach (var uid in _pendingRelease)
        {
            if (TryComp<PoltergeistComponent>(uid, out var spirit) && !TerminatingOrDeleted(uid)
                && spirit.Host is { } host
                && (!_mind.TryGetMind(host, out var current, out _) || current != spirit.Mind))
                Leave((uid, spirit));
        }
        _pendingRelease.Clear();
        var query = EntityQueryEnumerator<PoltergeistComponent>();
        while (query.MoveNext(out var uid, out var spirit))
        {
            spirit.Energy = Math.Min(spirit.MaxEnergy, spirit.Energy + spirit.EnergyPerSecond * frameTime);
            if (spirit.Host is { } host && !TerminatingOrDeleted(host))
                _transform.SetCoordinates(uid, _transform.GetMoverCoordinates(host));
        }
    }
}
