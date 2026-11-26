using System.Linq;
using System.Numerics;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Cloning.Events;
using Content.Shared.Climbing.Components;
using Content.Shared.Gravity;
using Content.Shared.Input;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Input.Binding;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Shared.Movement.Systems;

public sealed partial class SharedJumpAbilitySystem : EntitySystem
{
    private const float GroundJumpDistance = 2.8f;
    private const float GroundJumpMinSpeed = 6.5f;
    private const float GroundJumpSpeedBonus = 0.75f;
    private const int VaultLayers = (int) (CollisionGroup.TableLayer | CollisionGroup.LowImpassable);
    private const float LandingClearance = 0.15f;
    private static readonly SoundSpecifier GroundJumpSound =
        new SoundPathSpecifier("/Audio/Weapons/punchmiss.ogg", AudioParams.Default.WithPitchScale(1.33f).WithVolume(-5));

    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedGravitySystem _gravity = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private SharedAdvancedExertionSystem _exertion = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedBroadphaseSystem _broadphase = default!;
    [Dependency] private RayCastSystem _rayCast = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<JumpAbilityComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<JumpAbilityComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<JumpAbilityComponent, GravityJumpEvent>(OnGravityJump);

        SubscribeLocalEvent<ActiveLeaperComponent, StartCollideEvent>(OnLeaperCollide);
        SubscribeLocalEvent<ActiveLeaperComponent, LandEvent>(OnLeaperLand);
        SubscribeLocalEvent<ActiveLeaperComponent, StopThrowEvent>(OnLeaperStopThrow);

        SubscribeLocalEvent<JumpAbilityComponent, CloningEvent>(OnClone);

        SubscribeLocalEvent<JumpFlightComponent, PreventCollideEvent>(OnJumpCollision);
        SubscribeLocalEvent<JumpFlightComponent, StopThrowEvent>(OnStopJump);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.GroundJump, InputCmdHandler.FromDelegate(Jump, handle: false, outsidePrediction: false))
            .Register<SharedJumpAbilitySystem>();
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<SharedJumpAbilitySystem>();
        base.Shutdown();
    }

    private void Jump(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { } uid
            || !TryComp<AdvancedExertionComponent>(uid, out var exertion))
            return;

        if (HasComp<ThrownItemComponent>(uid))
            return;

        if (!TryComp<InputMoverComponent>(uid, out var mover) || !mover.CanMove
            || !TryComp<PhysicsComponent>(uid, out var body) || body.BodyType != BodyType.KinematicController
            || !body.CanCollide || body.BodyStatus != BodyStatus.OnGround
            || HasComp<RelayInputMoverComponent>(uid)
            || TryComp<BuckleComponent>(uid, out var buckle) && buckle.Buckled
            || TryComp<PullableComponent>(uid, out var pullable) && pullable.BeingPulled
            || _containers.IsEntityInContainer(uid) || _standing.IsDown(uid) || _gravity.IsWeightless(uid))
        {
            _popup.PopupClient(Loc.GetString("jump-ability-failure"), uid, uid);
            return;
        }

        if ((GetEquipmentJump(uid, "outerClothing") ?? GetEquipmentJump(uid, "shoes")) is { } equipmentJump)
        {
            _actions.TryPerformAction(new RequestPerformActionEvent(GetNetEntity(equipmentJump)), uid);
            return;
        }

        if (exertion.Reserve < SharedAdvancedExertionSystem.JumpCost)
        {
            _popup.PopupClient(Loc.GetString("ground-jump-too-tired"), uid, uid);
            return;
        }

        var xform = Transform(uid);
        var (position, rotation) = _transform.GetWorldPositionRotation(xform);
        var direction = _mover.GetHeldMovementDirection(mover);
        if (direction == Vector2.Zero)
            direction = rotation.ToWorldVec();

        var speed = TryComp<MovementSpeedModifierComponent>(uid, out var movement)
            ? (mover.Sprinting ? movement.CurrentSprintSpeed : movement.CurrentWalkSpeed)
            : MovementSpeedModifierComponent.DefaultBaseSprintSpeed;
        var launchSpeed = Math.Max(GroundJumpMinSpeed, Math.Max(speed + GroundJumpSpeedBonus, Vector2.Dot(body.LinearVelocity, direction)));
        var distanceMultiplier = TryComp<JumpAbilityComponent>(uid, out var innateJump) ? innateJump.GroundJumpDistanceMultiplier : 1f;
        var path = direction * (GroundJumpDistance * distanceMultiplier);
        var (jumpPath, obstacles) = PlanVault(uid, xform, position, rotation, path);

        // Throwing adds its launch velocity to the existing velocity. Set the speed explicitly so
        // jumping while running gives the same small boost as jumping from a walk.
        _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);
        LaunchJump(uid, jumpPath, launchSpeed, GroundJumpSound, obstacles);
        if (obstacles is { Count: > 0 })
            _broadphase.RegenerateContacts((uid, body, null, xform));

        _exertion.ChargeJump((uid, exertion));
    }

    private EntityUid? GetEquipmentJump(EntityUid uid, string slot)
    {
        if (!_inventory.TryGetSlotEntity(uid, slot, out var item) || !TryComp<JumpAbilityComponent>(item, out var jump))
            return null;

        foreach (var action in _actions.GetActions(uid))
        {
            if (action.Comp.Container == item && MetaData(action).EntityPrototype?.ID == jump.Action.Id
                && HasComp<InstantActionComponent>(action))
                return action.Owner;
        }

        return null;
    }

    private (Vector2 Path, List<EntityUid>? Obstacles) PlanVault(
        EntityUid uid, TransformComponent xform, Vector2 position, Angle rotation, Vector2 path)
    {
        if (xform.MapUid == null
            || !TryComp<ClimbingComponent>(uid, out var climbing) || !climbing.CanClimb || climbing.IsClimbing
            || !TryComp<FixturesComponent>(uid, out var fixtures))
            return (path, null);

        var fixture = fixtures.Fixtures.Values.FirstOrDefault(fixture => fixture.Hard);
        if (fixture == null)
            return (path, null);

        var distance = path.Length();
        var filter = new QueryFilter
        {
            MaskBits = fixture.CollisionMask,
            LayerBits = fixture.CollisionLayer,
            IsIgnored = target => target == uid,
        };
        var forward = Sweep(position, path);
        var landingFraction = 1f;
        foreach (var hit in forward)
        {
            if (!CanVault(hit.Obstacle, hit.Fixture))
                landingFraction = Math.Min(landingFraction, hit.Fraction - 0.03f / distance);
        }

        if (landingFraction * distance < LandingClearance)
            return (path, null);

        var jumpPath = path * landingFraction;
        // The reverse sweep confirms clearance behind each obstacle, including one we touch at takeoff.
        var backward = Sweep(position + jumpPath, -jumpPath);
        var cleared = backward
            .Where(hit => CanVault(hit.Obstacle, hit.Fixture) && hit.Fraction * jumpPath.Length() >= LandingClearance)
            .ToList();
        foreach (var hit in forward)
        {
            if (hit.Fraction < landingFraction && CanVault(hit.Obstacle, hit.Fixture)
                && !cleared.Exists(landing => landing.Fixture == hit.Fixture))
                return (path, null);
        }

        return cleared.Count == 0
            ? (path, null)
            : (jumpPath, cleared.Select(hit => hit.Obstacle).Distinct().ToList());

        List<(EntityUid Obstacle, Fixture Fixture, float Fraction)> Sweep(Vector2 origin, Vector2 travel)
        {
            var hits = new List<(EntityUid, Fixture, float)>();
            _rayCast.CastShape(xform.MapID, fixture.Shape, new Transform(origin, rotation), travel, filter, CollectHit);
            return hits;

            float CollectHit(FixtureProxy proxy, Vector2 point, Vector2 normal, float fraction, ref RayResult result)
            {
                hits.Add((proxy.Entity, proxy.Fixture, fraction));
                return 1f;
            }
        }
    }

    private bool CanVault(EntityUid obstacle, Fixture fixture) =>
        (fixture.CollisionLayer & ~VaultLayers) == 0
        && (fixture.CollisionLayer & VaultLayers) != 0
        && TryComp<ClimbableComponent>(obstacle, out var climbable) && climbable.Vaultable;

    private void OnJumpCollision(Entity<JumpFlightComponent> ent, ref PreventCollideEvent args)
    {
        if (args.OurBody.BodyStatus == BodyStatus.InAir && ent.Comp.VaultedObstacles.Contains(args.OtherEntity)
            && CanVault(args.OtherEntity, args.OtherFixture))
            args.Cancelled = true;
    }

    private void OnStopJump(Entity<JumpFlightComponent> ent, ref StopThrowEvent args) => RemCompDeferred<JumpFlightComponent>(ent);

    private void OnInit(Entity<JumpAbilityComponent> entity, ref MapInitEvent args)
    {
        if (!TryComp(entity, out ActionsComponent? comp))
            return;

        _actions.AddAction(entity, ref entity.Comp.ActionEntity, entity.Comp.Action, component: comp);
    }

    private void OnShutdown(Entity<JumpAbilityComponent> entity, ref ComponentShutdown args)
    {
        _actions.RemoveAction(entity.Owner, entity.Comp.ActionEntity);
    }

    private void OnLeaperCollide(Entity<ActiveLeaperComponent> entity, ref StartCollideEvent args)
    {
        _stun.TryKnockdown(entity.Owner, entity.Comp.KnockdownDuration, force: true);
        RemCompDeferred<ActiveLeaperComponent>(entity);
    }

    private void OnLeaperLand(Entity<ActiveLeaperComponent> entity, ref LandEvent args)
    {
        RemCompDeferred<ActiveLeaperComponent>(entity);
    }

    private void OnLeaperStopThrow(Entity<ActiveLeaperComponent> entity, ref StopThrowEvent args)
    {
        RemCompDeferred<ActiveLeaperComponent>(entity);
    }

    private void OnGravityJump(Entity<JumpAbilityComponent> entity, ref GravityJumpEvent args)
    {
        var user = args.Performer;
        if (_gravity.IsWeightless(user) || _standing.IsDown(user) || _containers.IsEntityInContainer(user)
            || HasComp<ThrownItemComponent>(user) || !TryComp<PhysicsComponent>(user, out var body)
            || body.BodyStatus != BodyStatus.OnGround || !body.CanCollide
            || TryComp<BuckleComponent>(user, out var buckle) && buckle.Buckled
            || TryComp<PullableComponent>(user, out var pullable) && pullable.BeingPulled)
        {
            if (entity.Comp.JumpFailedPopup != null)
                _popup.PopupClient(Loc.GetString(entity.Comp.JumpFailedPopup.Value), args.Performer, args.Performer);
            return;
        }

        var direction = _transform.GetWorldRotation(args.Performer).ToWorldVec();
        LaunchJump(args.Performer, direction * entity.Comp.JumpDistance, entity.Comp.JumpThrowSpeed, entity.Comp.JumpSound, ability: true);
        if (!HasComp<ThrownItemComponent>(user))
            return;

        if (entity.Comp.CanCollide)
        {
            EnsureComp<ActiveLeaperComponent>(entity, out var leaperComp);
            leaperComp.KnockdownDuration = entity.Comp.CollideKnockdown;
            Dirty(entity.Owner, leaperComp);
        }

        args.Handled = true;
    }

    private void LaunchJump(EntityUid user, Vector2 path, float speed, SoundSpecifier? sound,
        List<EntityUid>? obstacles = null, bool ability = false)
    {
        _throwing.TryThrow(user, path, speed, playSound: ability, doSpin: ability, animated: false);
        if (HasComp<ThrownItemComponent>(user))
        {
            var flight = EnsureComp<JumpFlightComponent>(user);
            flight.VaultedObstacles = obstacles ?? new();
            Dirty(user, flight);
        }
        _audio.PlayPredicted(sound, user, user);
    }

    private void OnClone(Entity<JumpAbilityComponent> ent, ref CloningEvent args)
    {
        if (!args.Settings.EventComponents.Contains(Factory.GetRegistration(ent.Comp.GetType()).Name))
            return;

        // Make sure to set the datafields before adding the component so that the correct action gets spawned on map init.
        var targetComp = Factory.GetComponent<JumpAbilityComponent>();
        targetComp.Action = ent.Comp.Action;
        targetComp.JumpDistance = ent.Comp.JumpDistance;
        targetComp.GroundJumpDistanceMultiplier = ent.Comp.GroundJumpDistanceMultiplier;
        targetComp.JumpThrowSpeed = ent.Comp.JumpThrowSpeed;
        targetComp.CanCollide = ent.Comp.CanCollide;
        targetComp.CollideKnockdown = ent.Comp.CollideKnockdown;
        targetComp.JumpSound = ent.Comp.JumpSound;
        targetComp.JumpFailedPopup = ent.Comp.JumpFailedPopup;
        AddComp(args.CloneUid, targetComp, true);
    }
}
