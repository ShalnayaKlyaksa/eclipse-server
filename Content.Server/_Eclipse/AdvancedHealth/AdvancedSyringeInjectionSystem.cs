using System.Linq;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.ActionBlocker;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Eclipse.AdvancedHealth;

/// <summary>Administers real solution while the held needle is in tissue. Clients cannot supply a dose or a clock.</summary>
public sealed class AdvancedSyringeInjectionSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly InjectorSystem _injectors = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ActionBlockerSystem _actions = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedMedicalConditionSystem _functions = default!;
    [Dependency] private readonly SharedTransformSystem _transforms = default!;
    private readonly Dictionary<EntityUid, Injection> _injections = new();
    private float _cleanup;

    private sealed class Injection(int id, EntityUid target, EntityUid tool, BodyPartSlot slot, TimeSpan now,
        MapCoordinates userPosition, MapCoordinates targetPosition)
    {
        public readonly int Id = id;
        public readonly EntityUid Target = target;
        public readonly EntityUid Tool = tool;
        public readonly BodyPartSlot Slot = slot;
        public readonly MapCoordinates UserPosition = userPosition;
        public readonly MapCoordinates TargetPosition = targetPosition;
        public bool Interrupted;
        public bool Closed;
        public bool Stopped;
        public bool Failed;
        public bool NeedleBroken;
        public float Remaining;
        public TimeSpan LastInput = now;
        public FixedPoint2 Injected;
        public float Depth;
        public bool Held;
    }

    public override void Initialize()
    {
        SubscribeNetworkEvent<AdvancedHealthInjectionInputEvent>(OnInput);
        SubscribeLocalEvent<DamageableComponent, DamageChangedEvent>((EntityUid uid, DamageableComponent component, DamageChangedEvent ev) =>
        {
            if (ev.InterruptsDoAfters && _injections.TryGetValue(uid, out var injection)) injection.Interrupted = true;
        });
        SubscribeLocalEvent<InjectorComponent, AdvancedHealthSyringeUseEvent>(OnSyringeUse);
    }

    private void OnSyringeUse(EntityUid uid, InjectorComponent component, ref AdvancedHealthSyringeUseEvent args)
    {
        var slot = TryComp<AimTargetComponent>(args.User, out var aim) && aim.CurrentTarget != BodyPartTarget.Auto
            ? aim.CurrentTarget.ToSlot() : BodyPartSlot.Chest;
        if (!CanInject(args.User, args.Target, uid, slot) || !TryComp<ActorComponent>(args.User, out var actor)) return;
        RaiseNetworkEvent(new AdvancedHealthOpenTreatmentEvent(GetNetEntity(args.Target), GetNetEntity(uid), slot), Filter.SinglePlayer(actor.PlayerSession));
    }

    private void OnInput(AdvancedHealthInjectionInputEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user) return;
        var result = HandleInput(user, ev);
        if (result != null) RaiseNetworkEvent(result, Filter.SinglePlayer(args.SenderSession));
    }

    // Also used by integration tests with the actual server clock and solutions.
    public AdvancedHealthInjectionStatusEvent? HandleInput(EntityUid user, AdvancedHealthInjectionInputEvent ev)
    {
        var target = GetEntity(ev.Target);
        var tool = GetEntity(ev.Tool);
        var now = _timing.CurTime;
        if (ev.Action is not (InjectionInputAction.Start or InjectionInputAction.Adjust or InjectionInputAction.Stop or InjectionInputAction.BreakNeedle)) return null;
        if (ev.Id <= 0 || !float.IsFinite(ev.Depth) || ev.Depth < 0 || ev.Depth > 44) return null;
        if (ev.Action == InjectionInputAction.Start)
        {
            if (_injections.TryGetValue(user, out var existing) && existing.Id == ev.Id) return null;
            _injections.Remove(user);
            if (!CanInject(user, target, tool, ev.Slot))
                return new(ev.Id, 0, Remaining(tool), true, true);
            _injections[user] = new Injection(ev.Id, target, tool, ev.Slot, now,
                _transforms.GetMapCoordinates(user), _transforms.GetMapCoordinates(target));
        }
        if (!_injections.TryGetValue(user, out var injection))
            return new(ev.Id, 0, Remaining(tool), true, true);
        if (injection.Id != ev.Id || injection.Target != target || injection.Tool != tool || injection.Slot != ev.Slot) return null;
        if (ev.Action == InjectionInputAction.BreakNeedle)
        {
            if (injection.Depth >= 12 && !injection.Closed && !injection.Failed && !injection.NeedleBroken
                && CanManipulate(user, target, tool, ev.Slot) && _injectors.TryBreakNeedle(tool))
            {
                LeaveNeedle(target, ev.Slot);
                injection.NeedleBroken = injection.Stopped = true;
            }
            injection.LastInput = now;
            return new(ev.Id, injection.Injected.Float(), injection.Remaining, injection.Stopped, injection.Failed, injection.NeedleBroken);
        }
        if (injection.Stopped)
        {
            if (ev.Action == InjectionInputAction.Stop) injection.Closed = true;
            injection.LastInput = now;
            return new(ev.Id, injection.Injected.Float(), injection.Remaining, true, injection.Failed, injection.NeedleBroken);
        }
        var valid = CanManipulate(user, target, tool, ev.Slot)
            && _transforms.GetMapCoordinates(user).InRange(injection.UserPosition, 0.2f)
            && _transforms.GetMapCoordinates(target).InRange(injection.TargetPosition, 0.2f)
            && !injection.Interrupted;
        var elapsed = (float) (now - injection.LastInput).TotalSeconds;
        var failed = !valid;
        // A suspended client cannot bank elapsed time and claim it in a later packet.
        if (valid && injection.Held && elapsed > 0 && elapsed <= 0.4f)
        {
            var requested = FixedPoint2.New(AdvancedInjectionFlow.Rate(injection.Depth) * Math.Min(elapsed, 0.25f));
            if (requested > 0)
            {
                if (_injectors.TryInjectTreatmentAmount(tool, user, target, requested, out var transferred))
                    injection.Injected += transferred;
                else
                    failed = true;
            }
        }
        injection.LastInput = now;
        injection.Depth = ev.Depth;
        injection.Held = ev.Held;
        var remaining = Remaining(tool);
        var stopped = failed || ev.Action == InjectionInputAction.Stop || remaining <= 0;
        injection.Stopped = stopped;
        injection.Closed = failed || ev.Action == InjectionInputAction.Stop;
        injection.Failed = failed;
        injection.Remaining = remaining;
        return new(ev.Id, injection.Injected.Float(), remaining, stopped, failed);
    }

    private float Remaining(EntityUid tool) => Exists(tool) && _injectors.TryGetInjectionVolume(tool, out var volume) ? volume.Float() : 0;

    private bool CanInject(EntityUid user, EntityUid target, EntityUid tool, BodyPartSlot slot)
        => CanManipulate(user, target, tool, slot) && _injectors.TryGetInjectionVolume(tool, out _);

    private bool CanManipulate(EntityUid user, EntityUid target, EntityUid tool, BodyPartSlot slot)
    {
        if (!Exists(user) || !Exists(target) || !Exists(tool)
            || !_actions.CanConsciouslyPerformAction(user) || !_actions.CanInteract(user, target)
            || !_actions.CanUseHeldEntity(user, tool) || !_hands.IsHolding(user, tool)
            || !_hands.EnumerateHands(user).Any(hand => _hands.GetHeldItem(user, hand) == tool && _functions.CanUseHand(user, hand))
            || user != target && !_interaction.InRangeUnobstructed(user, target)
            || !TryComp<InjectorComponent>(tool, out var injector) || injector.NeedleBroken
            || !TryComp<AdvancedHealthComponent>(target, out var health)
            || !health.BodyParts.TryGetValue(slot, out var part) || !AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.Injection))
            return false;
        return true;
    }

    private void LeaveNeedle(EntityUid target, BodyPartSlot slot)
    {
        var health = Comp<AdvancedHealthComponent>(target);
        var part = health.BodyParts[slot];
        part.Wounds.Add(new Wound
        {
            BodyPart = slot, Type = WoundType.Puncture, ForeignBody = ForeignBodyKind.Needle, HasForeignBody = true,
            ExternalBleedingRate = health.HasBlood ? 0.25f : 0, Pain = 20, IsDirty = true, InfectionRisk = 0.08f,
        });
        part.ForeignBodyCount = (byte) Math.Min(byte.MaxValue, part.ForeignBodyCount + 1);
        part.SkinIntegrity = Math.Max(0, part.SkinIntegrity - 3);
        part.IsBleeding |= health.HasBlood;
        health.Pain = Math.Min(100, health.Pain + 14);
        health.TraumaLoad = Math.Min(100, health.TraumaLoad + 1);
        Dirty(target, health);
        _functions.Refresh((target, health));
    }

    public override void Update(float frameTime)
    {
        _cleanup -= frameTime;
        if (_cleanup > 0) return;
        _cleanup = 1;
        foreach (var (user, injection) in _injections.ToArray())
            if (_timing.CurTime - injection.LastInput > TimeSpan.FromSeconds(1)) _injections.Remove(user);
    }
}
