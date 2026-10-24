using Content.Shared.Alert;
using Content.Shared.Bed.Sleep;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>Physical endurance is separate from non-lethal stamina damage and never causes stamina paralysis.</summary>
public sealed class SharedAdvancedExertionSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMoverController _mover = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedMedicalConditionSystem _functions = default!;
    [Dependency] private readonly AlertsSystem _alerts = default!;
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<AdvancedExertionComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<AdvancedExertionComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<AdvancedExertionComponent, RefreshMovementSpeedModifiersEvent>(OnSpeed);
        SubscribeLocalEvent<AdvancedExertionComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeNetworkEvent<ToggleCalmWalkingEvent>(OnToggleCalm);
    }

    public void EnsurePhysiology(Entity<AdvancedHealthComponent> ent)
    {
        if (_net.IsServer)
            EnsureComp<AdvancedExertionComponent>(ent);
    }

    private void OnStartup(Entity<AdvancedExertionComponent> ent, ref ComponentStartup args) => RefreshGait(ent);
    private void OnState(Entity<AdvancedExertionComponent> ent, ref AfterAutoHandleStateEvent args) => RefreshGait(ent);

    public void RefreshGait(Entity<AdvancedExertionComponent> ent)
    {
        if (TryComp<InputMoverComponent>(ent, out var mover))
            _mover.SetWalkingMode((ent.Owner, mover), 0, !ent.Comp.SprintRequested || ent.Comp.Exhausted);
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
    }

    private void OnToggleCalm(ToggleCalmWalkingEvent args, EntitySessionEventArgs session)
    {
        if (session.SenderSession.AttachedEntity is not { } uid || !TryComp<AdvancedExertionComponent>(uid, out var exertion))
            return;
        exertion.CalmWalking = !exertion.CalmWalking;
        Dirty(uid, exertion);
        RefreshGait((uid, exertion));
    }

    private void OnRejuvenate(Entity<AdvancedExertionComponent> ent, ref RejuvenateEvent args)
    {
        ent.Comp.Reserve = 100;
        ent.Comp.RecoveryLoad = ent.Comp.SleepDebt = ent.Comp.Effort = ent.Comp.WorkEffort = 0;
        ent.Comp.Exhausted = false;
        ent.Comp.WorkUntil = TimeSpan.Zero;
        ent.Comp.RunningSeconds = 0;
        ent.Comp.LastMotionSample = _timing.CurTime;
        Dirty(ent);
        RefreshGait(ent);
        UpdateAlerts(ent);
    }

    private void OnSpeed(Entity<AdvancedExertionComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var fatigue = Math.Clamp((ent.Comp.SleepDebt - 30) / 70, 0, 1);
        var walking = (ent.Comp.CalmWalking && !ent.Comp.SprintRequested ? 0.72f : 1f)
            * (ent.Comp.Exhausted ? 0.85f : 1f) * (1 - fatigue * 0.15f);
        args.ModifySpeed(walking, 1 - fatigue * 0.2f);
    }

    public override void Update(float frameTime)
    {
        if (!_net.IsServer)
            return;
        _accumulator += frameTime;
        if (_accumulator < 0.5f)
            return;
        var elapsed = _accumulator;
        _accumulator = 0;
        var query = EntityQueryEnumerator<AdvancedExertionComponent, AdvancedHealthComponent>();
        while (query.MoveNext(out var uid, out var exertion, out var health))
        {
            if (TryComp<MobStateComponent>(uid, out var state) && state.CurrentState == MobState.Dead)
            {
                if (exertion.AlertFlags != 0)
                {
                    SetAlert(uid, "AdvancedBreathless", false);
                    SetAlert(uid, "AdvancedExhausted", false);
                    SetAlert(uid, "AdvancedSleepy", false);
                    exertion.AlertFlags = 0;
                }
                exertion.RunningSeconds = 0;
                exertion.LastMotionSample = _timing.CurTime;
                continue;
            }
            if (TryComp<InputMoverComponent>(uid, out var mover)) RecordMotion(uid, mover);
            var fraction = health.IsUnconscious ? 0 : Math.Clamp(exertion.RunningSeconds / elapsed, 0, 1);
            exertion.RunningSeconds = 0;
            AdvanceFraction((uid, exertion), elapsed, fraction, HasComp<SleepingComponent>(uid));
        }
    }

    public void Advance(Entity<AdvancedExertionComponent> ent, float seconds, bool running, bool sleeping)
        => AdvanceFraction(ent, seconds, running ? 1 : 0, sleeping);

    /// <summary>Accounts for input transitions as well as periodic samples; short sprints cannot hide between ticks.</summary>
    public void RecordMotion(EntityUid uid, InputMoverComponent mover)
    {
        if (!_net.IsServer || !TryComp<AdvancedExertionComponent>(uid, out var exertion)) return;
        var elapsed = Math.Max(0, (float) (_timing.CurTime - exertion.LastMotionSample).TotalSeconds);
        exertion.LastMotionSample = _timing.CurTime;
        if (mover.Sprinting && mover.CanMove && mover.HasDirectionalMovement
            && TryComp<PhysicsComponent>(uid, out var physics) && physics.LinearVelocity.LengthSquared() > 0.04f)
            exertion.RunningSeconds += elapsed;
    }

    private void AdvanceFraction(Entity<AdvancedExertionComponent> ent, float seconds, float running, bool sleeping)
    {
        if (!_net.IsServer || !float.IsFinite(seconds) || seconds <= 0)
            return;
        var old = (ent.Comp.Reserve, ent.Comp.RecoveryLoad, ent.Comp.SleepDebt, ent.Comp.Effort, ent.Comp.Exhausted);
        var moving = TryComp<InputMoverComponent>(ent, out var mover) && mover.HasDirectionalMovement && mover.CanMove;
        var working = ent.Comp.WorkUntil > _timing.CurTime;
        running = sleeping ? 0 : Math.Clamp(running, 0, 1);
        var targetEffort = sleeping ? 0 : Math.Max(running, working ? ent.Comp.WorkEffort : 0f);
        ent.Comp.Effort += (targetEffort - ent.Comp.Effort) * Math.Clamp(seconds * 0.25f, 0, 1);
        if (ent.Comp.Effort < 0.005f) ent.Comp.Effort = 0;
        ent.Comp.Reserve -= ent.Comp.SprintCost * seconds * running;
        if (!working)
            ent.Comp.Reserve += (sleeping ? ent.Comp.RestRecovery * 1.5f : moving
                ? ent.Comp.WalkingRecovery * (ent.Comp.CalmWalking ? 1.3f : 1f) : ent.Comp.RestRecovery)
                * (1 - Math.Clamp(ent.Comp.SleepDebt / 100f, 0, 1) * 0.45f) * seconds * (1 - running);
        ent.Comp.Reserve = Math.Clamp(ent.Comp.Reserve, 0, 100);
        ent.Comp.RecoveryLoad = Math.Clamp(ent.Comp.RecoveryLoad + (targetEffort > 0.2f
            ? targetEffort * 0.05f : -0.06f) * seconds, 0, 1);
        ent.Comp.SleepDebt = Math.Clamp(ent.Comp.SleepDebt + (sleeping
            ? -ent.Comp.SleepRecoveryRate : ent.Comp.AwakeFatigueRate) * seconds, 0, 100);
        if (ent.Comp.Reserve <= 5) ent.Comp.Exhausted = true;
        else if (ent.Comp.Reserve >= 25) ent.Comp.Exhausted = false;
        if (old.Exhausted != ent.Comp.Exhausted || (int) (old.SleepDebt / 5) != (int) (ent.Comp.SleepDebt / 5))
            RefreshGait(ent);
        if (Math.Abs(old.Reserve - ent.Comp.Reserve) > 0.05f || Math.Abs(old.RecoveryLoad - ent.Comp.RecoveryLoad) > 0.005f
            || Math.Abs(old.Effort - ent.Comp.Effort) > 0.005f || old.Exhausted != ent.Comp.Exhausted
            || Math.Abs(ent.Comp.LastSentSleepDebt - ent.Comp.SleepDebt) >= 0.25f)
        {
            ent.Comp.LastSentSleepDebt = ent.Comp.SleepDebt;
            Dirty(ent);
        }
        UpdateAlerts(ent);
    }

    public bool CanWork(EntityUid uid, PhysicalWorkClass difficulty, int workingHands = 0, int freeHands = 0, bool starting = true)
        => WorkBlockReason(uid, difficulty, workingHands, freeHands, starting) == null;

    public string? WorkBlockReason(EntityUid uid, PhysicalWorkClass difficulty, int workingHands = 0, int freeHands = 0, bool starting = true)
    {
        if (!TryComp<AdvancedExertionComponent>(uid, out var exertion)) return null;
        if (difficulty == PhysicalWorkClass.Heavy)
        {
            if (exertion.SleepDebt >= 85) return "advanced-exertion-block-sleep";
            if (exertion.Exhausted || exertion.Reserve < (starting ? 20 : 5)) return "advanced-exertion-block-reserve";
        }
        if (workingHands <= 0 && freeHands <= 0) return null;
        if (!TryComp<HandsComponent>(uid, out var hands)) return "advanced-exertion-block-hands";
        var functional = 0;
        var empty = 0;
        foreach (var hand in _hands.EnumerateHands((uid, hands)))
        {
            if (!_functions.CanUseHand(uid, hand)) continue;
            functional++;
            if (_hands.GetHeldItem((uid, hands), hand) == null) empty++;
        }
        if (functional < workingHands) return "advanced-exertion-block-hands";
        return empty < freeHands ? "advanced-exertion-block-free-hand" : null;
    }

    public bool SpendWork(EntityUid uid, PhysicalWorkClass difficulty, float seconds, int workingHands = 0, int freeHands = 0)
    {
        if (!float.IsFinite(seconds) || seconds < 0 || !CanWork(uid, difficulty, workingHands, freeHands, false)) return false;
        ChargeWork(uid, difficulty, seconds);
        return true;
    }

    /// <summary>Accounts for effort already performed, including the final fraction before cancellation.</summary>
    public void ChargeWork(EntityUid uid, PhysicalWorkClass difficulty, float seconds)
    {
        if (!_net.IsServer || difficulty == PhysicalWorkClass.Light || !float.IsFinite(seconds) || seconds <= 0
            || !TryComp<AdvancedExertionComponent>(uid, out var exertion)) return;
        var cost = difficulty == PhysicalWorkClass.Heavy ? 5f : 1.2f;
        exertion.Reserve = Math.Max(0, exertion.Reserve - cost * seconds);
        exertion.WorkEffort = difficulty == PhysicalWorkClass.Heavy ? 0.9f : 0.4f;
        exertion.WorkUntil = _timing.CurTime + TimeSpan.FromSeconds(1.25);
        if (exertion.Reserve <= 5) exertion.Exhausted = true;
        Dirty(uid, exertion);
        RefreshGait((uid, exertion));
    }

    public float WorkDurationMultiplier(EntityUid uid, PhysicalWorkClass difficulty)
        => difficulty == PhysicalWorkClass.Light || !TryComp<AdvancedExertionComponent>(uid, out var exertion)
            ? 1f : 1f + (1 - exertion.Reserve / 100f) * 0.25f + exertion.SleepDebt / 100f * 0.3f;

    private void UpdateAlerts(Entity<AdvancedExertionComponent> ent)
    {
        byte flags = 0;
        if (ent.Comp.RecoveryLoad >= ((ent.Comp.AlertFlags & 1) != 0 ? 0.2f : 0.45f)) flags |= 1;
        if (ent.Comp.Exhausted) flags |= 2;
        if (ent.Comp.SleepDebt >= ((ent.Comp.AlertFlags & 4) != 0 ? 25 : 35)) flags |= 4;
        if (flags == ent.Comp.AlertFlags) return;
        SetAlert(ent, "AdvancedBreathless", (flags & 1) != 0);
        SetAlert(ent, "AdvancedExhausted", (flags & 2) != 0);
        SetAlert(ent, "AdvancedSleepy", (flags & 4) != 0);
        ent.Comp.AlertFlags = flags;
    }

    private void SetAlert(EntityUid uid, ProtoId<AlertPrototype> alert, bool show)
    {
        if (show) _alerts.ShowAlert(uid, alert);
        else _alerts.ClearAlert(uid, alert);
    }

    public static float RestingPulseVariation(double seconds, int seed)
    {
        var phase = seed % 97 * 0.17f;
        return 3f * MathF.Sin((float) (seconds * 0.37) + phase)
            + 2f * MathF.Sin((float) (seconds * 0.83) + phase * 0.7f);
    }
}
