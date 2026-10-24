using System.Numerics;
using Content.Shared._Lavaland.Megafauna.Events;
using Content.Shared._Lavaland.Movement;
using Content.Shared.Actions.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Lavaland.Megafauna.Systems;

/// <summary>
/// Handles general actions that are useful for all megafauna bosses.
/// </summary>
public sealed class MegafaunaActionsSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActionsComponent, SpawnEntityActionEvent>(OnAttackAction);
        SubscribeLocalEvent<ActionsComponent, MegafaunaProjectilePatternActionEvent>(OnProjectilePatternAction);
        SubscribeLocalEvent<ActionsComponent, ToggleTileMovementActionEvent>(OnTileMovement);
    }

    private void OnAttackAction(Entity<ActionsComponent> ent, ref SpawnEntityActionEvent args)
    {
        if (_net.IsClient // PredictedSpawn doesn't support spawning entities without initializing them yet...
            || args.Handled
            || _xform.GetGrid(args.Target) == null)
            return;

        EntityUid spawned;
        if (args.Entity != null && args.AttachToTarget)
            spawned = EntityManager.CreateEntityUninitialized(args.Spawn, new EntityCoordinates(args.Entity.Value, Vector2.Zero));
        else if (args.SpawnAtUser)
            spawned = EntityManager.CreateEntityUninitialized(args.Spawn, Transform(args.Performer).Coordinates);
        else
            spawned = EntityManager.CreateEntityUninitialized(args.Spawn, args.Target);

        var ev = new SpawnedByActionEvent(ent.Owner, args.Entity);
        RaiseLocalEvent(spawned, ref ev);

        // We run MapInitEvent only after SpawnedByActionEvent so all values are already set properly.
        EntityManager.InitializeEntity(spawned);
        EntityManager.RunMapInit(spawned, MetaData(spawned)); // InitializeEntity doesn't trigger MapInit event by itself....

        if (args.Entity != null && args.AttachToTarget)
            _xform.SetParent(spawned, args.Entity.Value); // It doesn't work without that for whatever reason??

        args.Handled = true;
    }

    private void OnProjectilePatternAction(Entity<ActionsComponent> ent, ref MegafaunaProjectilePatternActionEvent args)
    {
        if (_net.IsClient || args.Handled)
            return;

        var fromCoords = Transform(args.Performer).Coordinates;
        var fromMap = _xform.ToMapCoordinates(fromCoords);
        var targetMap = _xform.ToMapCoordinates(args.Target);
        var baseDirection = targetMap.Position - fromMap.Position;

        if (baseDirection.LengthSquared() < 0.01f)
            baseDirection = Vector2.UnitY;
        else
            baseDirection = Vector2.Normalize(baseDirection);

        var velocity = _physics.GetMapLinearVelocity(args.Performer);
        foreach (var direction in GetProjectileDirections(args, baseDirection))
        {
            var projectile = Spawn(args.Prototype, fromMap);
            _gun.ShootProjectile(projectile, direction, velocity, args.Performer, args.Performer, args.Speed);
        }

        args.Handled = true;
    }

    private IEnumerable<Vector2> GetProjectileDirections(MegafaunaProjectilePatternActionEvent args, Vector2 baseDirection)
    {
        var count = Math.Max(args.Count, 1);
        return args.Pattern switch
        {
            MegafaunaProjectilePattern.Cardinal => FixedDirections(0f + args.OffsetDegrees, 4),
            MegafaunaProjectilePattern.Diagonal => FixedDirections(45f + args.OffsetDegrees, 4),
            MegafaunaProjectilePattern.Ring => FixedDirections(args.OffsetDegrees, count),
            _ => TargetedSpread(baseDirection, count, args.SpreadDegrees, args.OffsetDegrees),
        };
    }

    private static IEnumerable<Vector2> FixedDirections(float offsetDegrees, int count)
    {
        for (var i = 0; i < count; i++)
            yield return DirectionFromDegrees(offsetDegrees + 360f / count * i);
    }

    private static IEnumerable<Vector2> TargetedSpread(Vector2 baseDirection, int count, float spreadDegrees, float offsetDegrees)
    {
        if (count == 1)
        {
            yield return Rotate(baseDirection, offsetDegrees);
            yield break;
        }

        var step = spreadDegrees / (count - 1);
        var start = -spreadDegrees / 2f;
        for (var i = 0; i < count; i++)
            yield return Rotate(baseDirection, start + step * i + offsetDegrees);
    }

    private static Vector2 DirectionFromDegrees(float degrees)
    {
        var radians = MathF.PI / 180f * degrees;
        return new Vector2(MathF.Cos(radians), MathF.Sin(radians));
    }

    private static Vector2 Rotate(Vector2 vector, float degrees)
    {
        var radians = MathF.PI / 180f * degrees;
        var sin = MathF.Sin(radians);
        var cos = MathF.Cos(radians);
        return new Vector2(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
    }

    private void OnTileMovement(Entity<ActionsComponent> ent, ref ToggleTileMovementActionEvent args)
    {
        if (args.Handled)
            return;

        if (HasComp<HierophantBeatComponent>(args.Target))
            RemComp<HierophantBeatComponent>(args.Target);
        else
            EnsureComp<HierophantBeatComponent>(args.Target);

        args.Handled = true;
    }
}
