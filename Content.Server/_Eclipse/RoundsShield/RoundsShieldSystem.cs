using System.Numerics;
using Content.Shared._Eclipse.RoundsShield;
using Content.Shared._Eclipse.RoundsShield.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.CombatMode;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Input;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Reflect;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Eclipse.RoundsShield;

public sealed class RoundsShieldSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly SharedCombatModeSystem _combat = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        CommandBinds.Builder
            .BindAfter(EngineKeyFunctions.UseSecondary, new PointerInputCmdHandler(HandleUseSecondary, false, false), typeof(SharedInteractionSystem))
            .Register<RoundsShieldSystem>();

        SubscribeLocalEvent<RoundsShieldComponent, AttackAttemptEvent>(OnAttackAttempt);
        SubscribeLocalEvent<RoundsShieldComponent, ProjectileReflectAttemptEvent>(OnProjectileReflectAttempt);
        SubscribeLocalEvent<MeleeWeaponComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeAllEvent<RoundsShieldAimEvent>(OnAimEvent);
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<RoundsShieldSystem>();
        base.Shutdown();
    }

    private bool HandleUseSecondary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        if (args.Session?.AttachedEntity is not { Valid: true } user ||
            !Exists(user))
        {
            return false;
        }

        if (args.State == BoundKeyState.Up)
        {
            if (TryComp<RoundsShieldComponent>(user, out var component))
                StopShield(user, component);

            return false;
        }

        if (args.State != BoundKeyState.Down)
            return false;

        if (TryComp<RoundsShieldComponent>(user, out var existing) && (existing.Raising || existing.Active))
        {
            return false;
        }

        TryStartShield(user, args.Coordinates);
        return false;
    }

    private bool TryStartShield(EntityUid user, EntityCoordinates? target = null)
    {
        if (!CanUseShield(user) || !HasActiveEnergyShield(user))
            return false;

        var component = EnsureComp<RoundsShieldComponent>(user);
        component.Raising = true;
        component.Active = false;
        component.Charges = component.MaxCharges;
        component.RaiseEndTime = _timing.CurTime + component.RaiseDelay;
        component.AimAngle = GetAimAngle(user, target);
        Dirty(user, component);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<RoundsShieldComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (!component.Raising && !component.Active)
                continue;

            if (!CanUseShield(uid))
            {
                StopShield(uid, component);
                continue;
            }

            if (component.Raising)
            {
                if (_timing.CurTime < component.RaiseEndTime)
                {
                    Dirty(uid, component);
                    continue;
                }

                component.Raising = false;
                component.Active = true;
            }

            if (component.Active)
                Dirty(uid, component);
        }
    }

    private void OnAimEvent(RoundsShieldAimEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity != GetEntity(msg.User) ||
            args.SenderSession.AttachedEntity is not { Valid: true } user ||
            !TryComp<RoundsShieldComponent>(user, out var component) ||
            (!component.Raising && !component.Active) ||
            !CanUseShield(user))
        {
            return;
        }

        component.AimAngle = SnapAimAngle(msg.AimAngle);
        Dirty(user, component);
    }

    private void OnProjectileReflectAttempt(EntityUid uid, RoundsShieldComponent shield, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled ||
            args.Component.ProjectileSpent ||
            !CanBlock(uid, shield))
        {
            return;
        }

        var projectilePosition = _transform.GetWorldPosition(args.ProjUid);
        if (!InShieldArc(uid, shield, projectilePosition) ||
            !_random.Prob(shield.ProjectileBlockChance))
        {
            return;
        }

        args.Cancelled = true;
        args.Component.ProjectileSpent = true;
        Dirty(args.ProjUid, args.Component);
        QueueDel(args.ProjUid);

        shield.Charges--;
        var ev = new RoundsShieldProjectileBlockedEvent(args.ProjUid, uid);
        RaiseLocalEvent(uid, ref ev);

        if (shield.Charges <= 0)
            StopShield(uid, shield);
        else
            Dirty(uid, shield);
    }

    private void OnAttackAttempt(EntityUid uid, RoundsShieldComponent component, AttackAttemptEvent args)
    {
        if (!component.Raising && !component.Active)
            return;

        args.Cancel();
    }

    private void OnMeleeHit(EntityUid uid, MeleeWeaponComponent component, ref MeleeHitEvent args)
    {
        if (args.Handled || !args.IsHit || args.HitEntities.Count == 0)
            return;

        var attackerPosition = _transform.GetWorldPosition(args.User);
        foreach (var target in args.HitEntities)
        {
            if (!TryComp<RoundsShieldComponent>(target, out var shield) ||
                !CanBlock(target, shield) ||
                !InShieldArc(target, shield, attackerPosition) ||
                !_random.Prob(shield.MeleeMissChance))
            {
                continue;
            }

            args.Handled = true;
            var ev = new RoundsShieldMeleeBlockedEvent(args.User, target, args.Weapon);
            RaiseLocalEvent(target, ref ev);
            return;
        }
    }

    private bool CanBlock(EntityUid user, RoundsShieldComponent component)
    {
        return component.Active &&
               component.Charges > 0 &&
               CanUseShield(user);
    }

    private bool CanUseShield(EntityUid user)
    {
        return _combat.IsInCombatMode(user) &&
               _actionBlocker.CanConsciouslyPerformAction(user) &&
               _mobState.IsAlive(user) &&
               !HasComp<StunnedComponent>(user) &&
               !HasComp<KnockedDownComponent>(user) &&
               ActiveHandIsEmpty(user);
    }

    private bool ActiveHandIsEmpty(EntityUid user)
    {
        return TryComp<HandsComponent>(user, out var hands) &&
               _hands.ActiveHandIsEmpty((user, hands));
    }

    private bool HasActiveEnergyShield(EntityUid user)
    {
        if (!TryComp<HandsComponent>(user, out var hands))
            return false;

        foreach (var held in _hands.EnumerateHeld((user, hands)))
        {
            if (TryComp<ItemToggleComponent>(held, out var toggle) &&
                toggle.Activated &&
                HasComp<ReflectComponent>(held))
                return true;
        }

        return false;
    }

    private Angle GetAimAngle(EntityUid user, EntityCoordinates? target = null)
    {
        if (target != null)
        {
            var direction = _transform.ToMapCoordinates(target.Value).Position - _transform.GetWorldPosition(user);
            if (direction.LengthSquared() > 0.001f)
                return SnapAimAngle(Angle.FromWorldVec(direction));
        }

        return SnapAimAngle(_transform.GetWorldRotation(user));
    }

    private static Angle SnapAimAngle(Angle angle)
    {
        return angle.GetCardinalDir().ToAngle();
    }

    private bool InShieldArc(EntityUid defender, RoundsShieldComponent shield, Vector2 sourcePosition)
    {
        var defenderPosition = _transform.GetWorldPosition(defender);
        var direction = sourcePosition - defenderPosition;
        if (direction.LengthSquared() < 0.001f)
            return true;

        var sourceAngle = Angle.FromWorldVec(direction);
        var delta = Math.Abs(Angle.ShortestDistance(shield.AimAngle, sourceAngle).Degrees);
        return delta <= shield.ArcDegrees / 2f;
    }

    private void StopShield(EntityUid owner, RoundsShieldComponent? component = null)
    {
        if (!Resolve(owner, ref component, false))
            return;

        component.Raising = false;
        component.Active = false;
        Dirty(owner, component);
    }
}
