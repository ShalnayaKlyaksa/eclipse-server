using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Movement.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>One calculation for the chip, movement and hand-use restrictions.</summary>
public sealed class SharedMedicalConditionSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedGunSystem _guns = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AdvancedHealthComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<AdvancedHealthComponent, RefreshMovementSpeedModifiersEvent>(OnSpeed);
        SubscribeLocalEvent<AdvancedHealthComponent, ContainerIsInsertingAttemptEvent>(OnInsert);
        SubscribeLocalEvent<AdvancedHealthComponent, UseAttemptEvent>(OnUse);
        SubscribeLocalEvent<AdvancedHealthComponent, WieldAttemptEvent>(OnWield);
        SubscribeLocalEvent<AdvancedHealthComponent, UpdateMobStateEvent>(OnMobState);
        SubscribeLocalEvent<ItemComponent, GetMeleeDamageEvent>(OnMeleeDamage);
        SubscribeLocalEvent<AdvancedHealthComponent, GetMeleeDamageEvent>(OnUnarmedDamage);
        SubscribeLocalEvent<ItemComponent, AttemptMeleeEvent>(OnMeleeAttempt);
        SubscribeLocalEvent<AdvancedHealthComponent, AttemptMeleeEvent>(OnUnarmedAttempt);
        SubscribeLocalEvent<GunComponent, ShotAttemptedEvent>(OnShootAttempt);
        SubscribeLocalEvent<ItemComponent, GunRefreshModifiersEvent>(OnGunModifiers);
    }

    public bool AddCondition(Entity<AdvancedHealthComponent?> ent, BodyPartSlot slot,
        ProtoId<MedicalConditionPrototype> prototype, float severity = 1f)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !_prototypes.HasIndex(prototype)
            || !float.IsFinite(severity) || !ent.Comp.BodyParts.TryGetValue(slot, out var part))
            return false;

        severity = Math.Clamp(severity, 0f, 1f);
        foreach (var condition in part.Conditions)
        {
            if (condition.Prototype != prototype)
                continue;
            condition.Severity = severity;
            Dirty(ent);
            Refresh(ent);
            return true;
        }
        part.Conditions.Add(new MedicalCondition { Prototype = prototype, Severity = severity });
        Dirty(ent);
        Refresh(ent);
        return true;
    }

    public bool RemoveCondition(Entity<AdvancedHealthComponent?> ent, BodyPartSlot slot,
        ProtoId<MedicalConditionPrototype> prototype)
    {
        if (!Resolve(ent, ref ent.Comp, false) || !ent.Comp.BodyParts.TryGetValue(slot, out var part)
            || part.Conditions.RemoveAll(condition => condition.Prototype == prototype) == 0)
            return false;
        Dirty(ent);
        Refresh(ent);
        return true;
    }

    public float GetFunction(AdvancedHealthComponent health, BodyPartSlot slot)
    {
        if (!health.BodyParts.TryGetValue(slot, out var part))
            return 0f;
        var function = part.GetFunction(health.HasBlood, health.CanFracture);
        foreach (var condition in part.Conditions)
        {
            if (_prototypes.TryIndex(condition.Prototype, out var prototype))
                function *= Math.Max(0f, 1f + (prototype.FunctionMultiplier - 1f)
                    * Math.Clamp(condition.Severity, 0f, 1f));
        }
        return Math.Clamp(function * (1 - health.SystemicWeakness), 0f, 1f);
    }

    /// <summary>The weakest segment limits a chain; damage is never multiplied three times.</summary>
    public float GetArmFunction(AdvancedHealthComponent health, HandLocation location)
    {
        var left = location == HandLocation.Left;
        if (location == HandLocation.Middle)
            return 1f; // Extra hands have no corresponding anatomy in the humanoid profile.
        return Math.Min(GetFunction(health, left ? BodyPartSlot.LeftUpperArm : BodyPartSlot.RightUpperArm),
            Math.Min(GetFunction(health, left ? BodyPartSlot.LeftForearm : BodyPartSlot.RightForearm),
                GetFunction(health, left ? BodyPartSlot.LeftHand : BodyPartSlot.RightHand)));
    }

    public float GetLegFunction(AdvancedHealthComponent health, bool left)
        => Math.Min(GetFunction(health, left ? BodyPartSlot.LeftThigh : BodyPartSlot.RightThigh),
            Math.Min(GetFunction(health, left ? BodyPartSlot.LeftShin : BodyPartSlot.RightShin),
                GetFunction(health, left ? BodyPartSlot.LeftFoot : BodyPartSlot.RightFoot)));

    public bool CanSprint(EntityUid user)
    {
        if (!TryComp<AdvancedHealthComponent>(user, out var health)) return true;
        if (health.IsUnconscious || health.Shock >= 60 || health.Pain >= 85 || health.Consciousness < 50
            || health.Perfusion < 0.5f || health.PulmonaryFunction * (1 - health.ClinicalLungImpairment) < 0.45f
            || (GetLegFunction(health, true) + GetLegFunction(health, false)) * 0.5f < 0.65f) return false;
        if (TryComp<AdvancedNutritionComponent>(user, out var nutrition)
            && (nutrition.RequiresCrawling || nutrition.ReserveMultiplier < 0.4f)) return false;
        if (TryComp<AdvancedExertionComponent>(user, out var exertion) && (exertion.Exhausted || exertion.SleepDebt >= 85)) return false;
        if (TryComp<AdvancedDiseaseComponent>(user, out var diseases) && diseases.SprintBlocked) return false;
        return true;
    }

    public bool CanUseHand(EntityUid user, string handId, AdvancedHealthComponent? health = null)
    {
        if (!TryComp(user, out health) || !_hands.TryGetHand(user, handId, out var hand))
            return true;
        return GetArmFunction(health, hand.Value.Location) > 0.01f;
    }

    public void Refresh(Entity<AdvancedHealthComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;
        _movement.RefreshMovementSpeedModifiers(ent);
        var left = GetArmFunction(ent.Comp, HandLocation.Left);
        var right = GetArmFunction(ent.Comp, HandLocation.Right);
        if (Math.Abs(left - ent.Comp.LastLeftArmFunction) > 0.005f
            || Math.Abs(right - ent.Comp.LastRightArmFunction) > 0.005f)
        {
            ent.Comp.LastLeftArmFunction = left;
            ent.Comp.LastRightArmFunction = right;
            foreach (var item in _hands.EnumerateHeld(ent.Owner))
                if (HasComp<GunComponent>(item))
                    _guns.RefreshModifiers((item, null));
        }
    }

    public static MobState GetMobState(AdvancedHealthComponent health, MobState current)
    {
        if (current == MobState.Dead
            || health.IsBrainDestroyed
            || health.IsHeartStopped && health.TimeSinceHeartStopped >= health.HeartStoppedDeathTime
            || health.HasBlood && health.BloodVolume <= health.BloodDeathThreshold
            || health.TraumaLoad >= health.InstantDeathTraumaThreshold
            || health.NeedsOxygen && health.TimeBelowLethalOxygen >= 60f)
            return MobState.Dead;
        var recovering = current == MobState.Critical;
        var bloodRatio = health.BloodVolume / Math.Max(1f, health.MaxBloodVolume);
        return health.IsUnconscious || health.IsHeartStopped
            || health.HasBlood && bloodRatio <= (recovering ? 0.56f : 0.5f)
            || health.Consciousness <= (recovering ? 32f : 25f)
            || health.NeedsOxygen && health.Oxygenation <= health.CriticalOxygenation + (recovering ? 8f : 0f)
            ? MobState.Critical : MobState.Alive;
    }

    private void OnMobState(Entity<AdvancedHealthComponent> ent, ref UpdateMobStateEvent args)
    {
        if (TryComp<MobStateComponent>(ent, out var state))
            args.State = GetMobState(ent.Comp, state.CurrentState);
    }

    private float ActiveHandFunction(EntityUid user)
    {
        if (!TryComp<AdvancedHealthComponent>(user, out var health))
            return 1f;
        var name = _hands.GetActiveHand(user);
        return _hands.TryGetHand(user, name, out var hand)
            ? GetArmFunction(health, hand.Value.Location) : 1f;
    }

    private void OnMeleeDamage(Entity<ItemComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (!HasComp<AdvancedHealthComponent>(ent))
            args.Damage *= 0.25f + 0.75f * ActiveHandFunction(args.User);
    }

    private void OnUnarmedDamage(Entity<AdvancedHealthComponent> ent, ref GetMeleeDamageEvent args)
        => args.Damage *= 0.25f + 0.75f * ActiveHandFunction(args.User);

    private void OnMeleeAttempt(Entity<ItemComponent> ent, ref AttemptMeleeEvent args)
    {
        if (ActiveHandFunction(args.User) <= 0.01f)
            args.Cancelled = true;
    }

    private void OnUnarmedAttempt(Entity<AdvancedHealthComponent> ent, ref AttemptMeleeEvent args)
    {
        if (ActiveHandFunction(args.User) <= 0.01f)
            args.Cancelled = true;
    }

    private void OnShootAttempt(Entity<GunComponent> ent, ref ShotAttemptedEvent args)
    {
        if (ActiveHandFunction(args.User) <= 0.01f)
            args.Cancel();
    }

    private void OnGunModifiers(Entity<ItemComponent> ent, ref GunRefreshModifiersEvent args)
    {
        var penalty = 1f - ActiveHandFunction(Transform(ent).ParentUid);
        var spread = Angle.FromDegrees(12f * penalty);
        args.MinAngle += spread;
        args.MaxAngle += spread;
    }

    private void OnStartup(Entity<AdvancedHealthComponent> ent, ref ComponentStartup args)
    {
        EntityManager.System<SharedAdvancedExertionSystem>().EnsurePhysiology(ent);
        Refresh((ent.Owner, ent.Comp));
    }

    private void OnSpeed(Entity<AdvancedHealthComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!ent.Comp.BodyParts.ContainsKey(BodyPartSlot.LeftThigh)
            && !ent.Comp.BodyParts.ContainsKey(BodyPartSlot.RightThigh))
            return;
        var legs = (GetLegFunction(ent.Comp, true) + GetLegFunction(ent.Comp, false)) * 0.5f;
        // No functional legs: crawling remains possible, sprinting does not.
        args.ModifySpeed(0.2f + 0.8f * legs, legs);
        if (TryComp<AdvancedNutritionComponent>(ent, out var nutrition)) args.ModifySpeed(nutrition.MovementMultiplier);
    }

    private void OnInsert(Entity<AdvancedHealthComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (!CanUseHand(ent, args.Container.ID, ent.Comp))
            args.Cancel();
    }

    private void OnUse(Entity<AdvancedHealthComponent> ent, ref UseAttemptEvent args)
    {
        var hand = _hands.GetActiveHand(ent.Owner);
        if (hand != null && !CanUseHand(ent, hand, ent.Comp))
            args.Cancel();
    }

    private void OnWield(Entity<AdvancedHealthComponent> ent, ref WieldAttemptEvent args)
    {
        if (GetArmFunction(ent.Comp, HandLocation.Left) <= 0.01f
            || GetArmFunction(ent.Comp, HandLocation.Right) <= 0.01f)
            args.Cancelled = true;
    }
}
