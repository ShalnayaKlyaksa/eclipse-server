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
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>Physical endurance is separate from non-lethal stamina damage and never causes stamina paralysis.</summary>
public sealed class SharedAdvancedExertionSystem : EntitySystem
{
    public const float JumpCost = 20f;
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
    }

    public void EnsurePhysiology(Entity<AdvancedHealthComponent> ent)
    {
        if (_net.IsServer)
            EnsureComp<AdvancedExertionComponent>(ent).NetworkHeartRate = ent.Comp.HeartRate;
    }

    private void OnStartup(Entity<AdvancedExertionComponent> ent, ref ComponentStartup args) => RefreshGait(ent);
    private void OnState(Entity<AdvancedExertionComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!_net.IsServer && TryComp<AdvancedHealthComponent>(ent, out var health))
            health.HeartRate = ent.Comp.NetworkHeartRate;
        // InputMover and MovementSpeedModifier restore their own authoritative snapshots.
        // Rewriting input here corrupts subtick history during prediction rollback.
    }

    public void PublishHeartRate(EntityUid uid, float pulse)
    {
        if (!_net.IsServer || !TryComp<AdvancedExertionComponent>(uid, out var exertion)
            || Math.Abs(exertion.NetworkHeartRate - pulse) < 0.1f) return;
        exertion.NetworkHeartRate = pulse;
        Dirty(uid, exertion);
    }

    public void RefreshGait(Entity<AdvancedExertionComponent> ent)
    {
        if (_timing.ApplyingState)
            return;

        var sprinting = ent.Comp.SprintRequested && _functions.CanSprint(ent.Owner);
        if (TryComp<InputMoverComponent>(ent, out var mover) && mover.Sprinting != sprinting)
            _mover.SetWalkingMode((ent.Owner, mover), 0, !sprinting);
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
    }

    public void SetCalmWalking(EntityUid uid, bool walking)
    {
        if (!TryComp<AdvancedExertionComponent>(uid, out var exertion) || exertion.CalmWalking == walking)
            return;
        exertion.CalmWalking = walking;
        Dirty(uid, exertion);
        RefreshGait((uid, exertion));
    }

    private void OnRejuvenate(Entity<AdvancedExertionComponent> ent, ref RejuvenateEvent args)
    {
        ent.Comp.Reserve = 100;
        if (TryComp<AdvancedHealthComponent>(ent, out var health)) ent.Comp.NetworkHeartRate = health.RestingHeartRate;
        ent.Comp.RecoveryLoad = ent.Comp.SleepDebt = ent.Comp.Effort = ent.Comp.WorkEffort = 0;
        ent.Comp.Exhausted = false;
        ent.Comp.WorkUntil = TimeSpan.Zero;
        ent.Comp.RecoveryDelay = 0;
        ent.Comp.ReserveCapacity = 100;
        ent.Comp.RunningSeconds = 0;
        ent.Comp.LastMotionSample = _timing.CurTime;
        Dirty(ent);
        RefreshGait(ent);
        UpdateAlerts(ent);
    }

    private void OnSpeed(Entity<AdvancedExertionComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var fatigue = Math.Clamp((ent.Comp.SleepDebt - 30) / 70, 0, 1);
        var walking = (ent.Comp.CalmWalking && (!ent.Comp.SprintRequested || !_functions.CanSprint(ent.Owner)) ? 0.5f : 1f)
            * (ent.Comp.Exhausted ? 0.85f : 1f) * (1 - fatigue * 0.15f);
        // Ordinary walking keeps the old running pace for each species. Shift adds 25%.
        var walkingScale = TryComp<MovementSpeedModifierComponent>(ent, out var speed) && speed.BaseWalkSpeed > 0
            ? speed.BaseSprintSpeed / speed.BaseWalkSpeed : 1f;
        args.ModifySpeed(walking * walkingScale, 1.25f * (1 - fatigue * 0.2f));
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
            if (TryComp<InputMoverComponent>(uid, out var mover))
            {
                RecordMotion(uid, mover);
                if (mover.Sprinting != (exertion.SprintRequested && _functions.CanSprint(uid))) RefreshGait((uid, exertion));
            }
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
        if (mover.Sprinting && mover.CanMove && mover.HasDirectionalMovement && _functions.CanSprint(uid)
            && TryComp<MovementSpeedModifierComponent>(uid, out var speed)
            && speed.CurrentSprintSpeed > speed.CurrentWalkSpeed * 1.05f
            && TryComp<PhysicsComponent>(uid, out var physics)
            && physics.LinearVelocity.Length() > speed.CurrentWalkSpeed * 1.05f)
            exertion.RunningSeconds += elapsed;
    }

    private void AdvanceFraction(Entity<AdvancedExertionComponent> ent, float seconds, float running, bool sleeping)
    {
        if (!_net.IsServer || !float.IsFinite(seconds) || seconds <= 0)
            return;
        var old = (ent.Comp.Reserve, ent.Comp.RecoveryLoad, ent.Comp.SleepDebt, ent.Comp.Effort, ent.Comp.Exhausted);
        var moving = TryComp<InputMoverComponent>(ent, out var mover) && mover.HasDirectionalMovement && mover.CanMove
            && TryComp<PhysicsComponent>(ent, out var motion) && motion.LinearVelocity.LengthSquared() > 0.04f;
        var working = ent.Comp.WorkUntil > _timing.CurTime;
        var nutritionRecovery = TryComp<AdvancedNutritionComponent>(ent, out var nutrition) ? nutrition.RecoveryMultiplier : 1f;
        ent.Comp.SleepDebt = Math.Clamp(ent.Comp.SleepDebt + (sleeping
            ? -ent.Comp.SleepRecoveryRate : ent.Comp.AwakeFatigueRate) * seconds, 0, 100);
        running = sleeping ? 0 : Math.Clamp(running, 0, 1);
        var targetEffort = sleeping ? 0 : Math.Max(running, working ? ent.Comp.WorkEffort : 0f);
        ent.Comp.Effort += (targetEffort - ent.Comp.Effort) * Math.Clamp(seconds * 0.25f, 0, 1);
        if (ent.Comp.Effort < 0.005f) ent.Comp.Effort = 0;
        var food = TryComp<Content.Shared._Eclipse.Cooking.CulinaryNutritionComponent>(ent, out var culinary) ? culinary : null;
        ent.Comp.Reserve -= ent.Comp.SprintCost * seconds * running
            * (1 + (food?.Heaviness ?? 0) * 0.2f - (food?.Efficiency ?? 0) * 0.15f);
        ent.Comp.ReserveCapacity = Math.Clamp(100 - ent.Comp.SleepDebt * 0.65f, 10, 100)
            * (nutrition?.ReserveMultiplier ?? 1);
        var recoverySeconds = Math.Max(0, seconds - ent.Comp.RecoveryDelay);
        if (running > 0 || working) ent.Comp.RecoveryDelay = 4;
        else ent.Comp.RecoveryDelay = Math.Max(0, ent.Comp.RecoveryDelay - seconds);
        if (nutrition != null)
            ent.Comp.Reserve -= ((nutrition.FoodDepleted ? 0.12f : 0) + (nutrition.WaterDepleted ? 0.2f : 0)
                + nutrition.Malnutrition * 0.1f + nutrition.DehydrationStrain * 0.15f) * seconds;
        if (!working && ent.Comp.RecoveryDelay <= 0)
            ent.Comp.Reserve += (sleeping ? ent.Comp.RestRecovery * 1.5f : moving
                ? ent.Comp.WalkingRecovery * (ent.Comp.CalmWalking ? 1.3f : 1f) : ent.Comp.RestRecovery)
                * (1 - Math.Clamp(ent.Comp.SleepDebt / 100f, 0, 1) * 0.45f) * nutritionRecovery
                * (1 + (food?.Recovery ?? 0) * 0.25f) * recoverySeconds * (1 - running);
        ent.Comp.Reserve = Math.Clamp(ent.Comp.Reserve, 0, ent.Comp.ReserveCapacity);
        ent.Comp.RecoveryLoad = Math.Clamp(ent.Comp.RecoveryLoad + (targetEffort > 0.2f
            ? targetEffort * 0.05f : -0.06f) * seconds, 0, 1);
        if (ent.Comp.Reserve <= 5) ent.Comp.Exhausted = true;
        else if (ent.Comp.Reserve >= 25) ent.Comp.Exhausted = false;
        if (old.Exhausted != ent.Comp.Exhausted || (int) (old.SleepDebt / 5) != (int) (ent.Comp.SleepDebt / 5))
            RefreshGait(ent);
        if (Math.Abs(old.Reserve - ent.Comp.Reserve) > 0.05f || Math.Abs(old.RecoveryLoad - ent.Comp.RecoveryLoad) > 0.005f
            || Math.Abs(old.Effort - ent.Comp.Effort) > 0.005f || old.Exhausted != ent.Comp.Exhausted
            || old.SleepDebt > 0 && ent.Comp.SleepDebt == 0
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
            if (TryComp<AdvancedNutritionComponent>(uid, out var nutrition)
                && Math.Max(nutrition.Malnutrition, nutrition.DehydrationStrain) >= 0.8f) return "advanced-exertion-block-nutrition";
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
        var cost = difficulty == PhysicalWorkClass.Heavy ? 5f * exertion.HeavyWorkCostMultiplier : 1.2f;
        ChargeExertion((uid, exertion), cost * seconds, difficulty == PhysicalWorkClass.Heavy ? 0.9f : 0.4f);
    }

    public void ChargeJump(Entity<AdvancedExertionComponent> ent)
    {
        if (_net.IsServer)
            ChargeExertion(ent, JumpCost, 0.9f);
    }

    private void ChargeExertion(Entity<AdvancedExertionComponent> ent, float cost, float effort)
    {
        ent.Comp.Reserve = Math.Max(0, ent.Comp.Reserve - cost);
        ent.Comp.WorkEffort = effort;
        ent.Comp.WorkUntil = _timing.CurTime + TimeSpan.FromSeconds(1.25);
        if (ent.Comp.Reserve <= 5) ent.Comp.Exhausted = true;
        Dirty(ent);
        RefreshGait(ent);
    }

    public float WorkDurationMultiplier(EntityUid uid, PhysicalWorkClass difficulty)
        => difficulty == PhysicalWorkClass.Light || !TryComp<AdvancedExertionComponent>(uid, out var exertion)
            ? 1f
            : (1f + (1 - exertion.Reserve / 100f) * 0.25f + exertion.SleepDebt / 100f * 0.3f)
              * (difficulty == PhysicalWorkClass.Heavy ? MathF.Sqrt(exertion.HeavyWorkCostMultiplier) : 1f);

    private void UpdateAlerts(Entity<AdvancedExertionComponent> ent)
    {
        byte flags = 0;
        if (ent.Comp.RecoveryLoad >= ((ent.Comp.AlertFlags & 1) != 0 ? 0.2f : 0.45f)) flags |= 1;
        if (ent.Comp.Exhausted) flags |= 2;
        if (ent.Comp.SleepDebt >= ((ent.Comp.AlertFlags & 4) != 0 ? 25 : 35)) flags |= 4;
        SetAlert(ent, "AdvancedBreathless", (flags & 1) != 0);
        SetAlert(ent, "AdvancedExhausted", (flags & 2) != 0);
        SetAlert(ent, "AdvancedSleepy", (flags & 4) != 0,
            ent.Comp.SleepDebt >= 75 ? (short) 3 : ent.Comp.SleepDebt >= 50 ? (short) 2 : (short) 1);
        ent.Comp.AlertFlags = flags;
    }

    private void SetAlert(EntityUid uid, ProtoId<AlertPrototype> alert, bool show, short? severity = null)
    {
        if (show) _alerts.ShowAlert(uid, alert, severity);
        else _alerts.ClearAlert(uid, alert);
    }

    public static float RestingPulseVariation(double seconds, int seed)
    {
        var phase = seed % 97 * 0.17f;
        return 3f * MathF.Sin((float) (seconds * 0.37) + phase)
            + 2f * MathF.Sin((float) (seconds * 0.83) + phase * 0.7f);
    }
}
