using System.Linq;
using System.Numerics;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Cloning.Events;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
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
    private const float GroundJumpDistance = 1.8f;
    private const float GroundJumpSpeedBonus = 0.75f;
    private const int VaultLayers = (int) (CollisionGroup.TableLayer | CollisionGroup.LowImpassable);

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
    [Dependency] private SharedMedicalConditionSystem _medical = default!;
    [Dependency] private ClimbSystem _climb = default!;
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

        SubscribeLocalEvent<AdvancedExertionComponent, PreventCollideEvent>(OnJumpCollision);
        SubscribeLocalEvent<AdvancedExertionComponent, StopThrowEvent>(OnStopGroundJump);

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
            || !TryComp<AdvancedExertionComponent>(uid, out var exertion)
            || !TryComp<InputMoverComponent>(uid, out var mover) || !mover.CanMove
            || !TryComp<PhysicsComponent>(uid, out var body) || body.BodyType != BodyType.KinematicController
            || !body.CanCollide || body.BodyStatus != BodyStatus.OnGround
            || HasComp<RelayInputMoverComponent>(uid)
            || TryComp<BuckleComponent>(uid, out var buckle) && buckle.Buckled
            || TryComp<PullableComponent>(uid, out var pullable) && pullable.BeingPulled
            || _containers.IsEntityInContainer(uid) || _standing.IsDown(uid) || _gravity.IsWeightless(uid))
            return;

        if ((GetEquipmentJump(uid, "outerClothing") ?? GetEquipmentJump(uid, "shoes")) is { } equipmentJump)
        {
            _actions.TryPerformAction(new RequestPerformActionEvent(GetNetEntity(equipmentJump)), uid);
            return;
        }

        if (exertion.Reserve < SharedAdvancedExertionSystem.JumpCost || !_medical.CanSprint(uid))
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
        var launchSpeed = Math.Max(speed + GroundJumpSpeedBonus, Vector2.Dot(body.LinearVelocity, direction));
        var path = direction * GroundJumpDistance;

        exertion.JumpObstacle = FindVaultObstacle(uid, xform, position, rotation, path);
        if (exertion.JumpObstacle != null)
            Dirty(uid, exertion);

        // Throwing adds its launch velocity to the existing velocity. Set the speed explicitly so
        // jumping while running gives the same small boost as jumping from a walk.
        _physics.SetLinearVelocity(uid, Vector2.Zero, body: body);
        _throwing.TryThrow(uid, path, body, xform, launchSpeed, playSound: false, doSpin: false);
        if (exertion.JumpObstacle != null)
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

    private EntityUid? FindVaultObstacle(EntityUid uid, TransformComponent xform, Vector2 position, Angle rotation, Vector2 path)
    {
        if (!TryComp<FixturesComponent>(uid, out var fixtures) || xform.MapUid == null)
            return null;

        var fixture = fixtures.Fixtures.Values.FirstOrDefault(fixture => fixture.Hard);
        if (fixture == null)
            return null;

        var filter = new QueryFilter
        {
            MaskBits = fixture.CollisionMask,
            LayerBits = fixture.CollisionLayer,
            IsIgnored = target => target == uid,
        };
        // Sweep from the landing point first. A forward cast can miss a table the player is touching.
        var backward = _rayCast.CastShape(xform.MapID, fixture.Shape, new Transform(position + path, rotation), -path, filter, CollectVaultHit);
        if (backward.Results.Count == 0)
            return null;

        var obstacle = backward.Results[0].Entity;
        if (!TryComp<ClimbableComponent>(obstacle, out var climbable) || !_climb.CanVault(climbable, uid, obstacle, out _))
            return null;

        foreach (var hit in backward.Results)
        {
            if (hit.Entity != obstacle || hit.Fraction <= 0.1f)
                return null;
        }

        var forward = _rayCast.CastShape(xform.MapID, fixture.Shape, new Transform(position, rotation), path, filter, CollectVaultHit);
        foreach (var hit in forward.Results)
        {
            if (hit.Entity != obstacle)
                return null;
        }

        return obstacle;
    }

    private static float CollectVaultHit(FixtureProxy proxy, Vector2 point, Vector2 normal, float fraction, ref RayResult result)
    {
        result.Results.Add(new RayHit(proxy.Entity, normal, fraction) { Point = point });
        return 1f;
    }

    private void OnJumpCollision(Entity<AdvancedExertionComponent> ent, ref PreventCollideEvent args)
    {
        if (args.OurBody.BodyStatus == BodyStatus.InAir && args.OtherEntity == ent.Comp.JumpObstacle
            && (args.OtherFixture.CollisionLayer & ~VaultLayers) == 0
            && (args.OtherFixture.CollisionLayer & VaultLayers) != 0)
            args.Cancelled = true;
    }

    private void OnStopGroundJump(Entity<AdvancedExertionComponent> ent, ref StopThrowEvent args)
    {
        if (ent.Comp.JumpObstacle == null)
            return;

        ent.Comp.JumpObstacle = null;
        Dirty(ent);
    }

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
        if (_gravity.IsWeightless(args.Performer) || _standing.IsDown(args.Performer))
        {
            if (entity.Comp.JumpFailedPopup != null)
                _popup.PopupClient(Loc.GetString(entity.Comp.JumpFailedPopup.Value), args.Performer, args.Performer);
            return;
        }

        var xform = Transform(args.Performer);
        var throwing = xform.LocalRotation.ToWorldVec() * entity.Comp.JumpDistance;
        var direction = xform.Coordinates.Offset(throwing); // to make the character jump in the direction he's looking

        _throwing.TryThrow(args.Performer, direction, entity.Comp.JumpThrowSpeed);

        _audio.PlayPredicted(entity.Comp.JumpSound, args.Performer, args.Performer);

        if (entity.Comp.CanCollide)
        {
            EnsureComp<ActiveLeaperComponent>(entity, out var leaperComp);
            leaperComp.KnockdownDuration = entity.Comp.CollideKnockdown;
            Dirty(entity.Owner, leaperComp);
        }

        args.Handled = true;
    }

    private void OnClone(Entity<JumpAbilityComponent> ent, ref CloningEvent args)
    {
        if (!args.Settings.EventComponents.Contains(Factory.GetRegistration(ent.Comp.GetType()).Name))
            return;

        // Make sure to set the datafields before adding the component so that the correct action gets spawned on map init.
        var targetComp = Factory.GetComponent<JumpAbilityComponent>();
        targetComp.Action = ent.Comp.Action;
        targetComp.JumpDistance = ent.Comp.JumpDistance;
        targetComp.JumpThrowSpeed = ent.Comp.JumpThrowSpeed;
        targetComp.CanCollide = ent.Comp.CanCollide;
        targetComp.CollideKnockdown = ent.Comp.CollideKnockdown;
        targetComp.JumpSound = ent.Comp.JumpSound;
        targetComp.JumpFailedPopup = ent.Comp.JumpFailedPopup;
        AddComp(args.CloneUid, targetComp, true);
    }
}
