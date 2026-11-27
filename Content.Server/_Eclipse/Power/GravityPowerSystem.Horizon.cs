using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server.AlertLevel;
using Content.Server.Chat.Systems;
using Content.Server.Ghost;
using Content.Server.Mind;
using Content.Server.Power.Components;
using Content.Server.Respawn;
using Content.Server.RoundEnd;
using Content.Server.Singularity.EntitySystems;
using Content.Server.Singularity.Events;
using Content.Server.Station.Systems;
using Content.Shared._Eclipse.Power;
using Content.Shared.Atmos.Components;
using Content.Shared.Ghost;
using Content.Shared.Respawn;
using Content.Shared.Singularity.Components;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Eclipse.Power;

public sealed partial class GravityPowerSystem
{
    internal const float MaximumHorizon = 6f;
    private const float HorizonPerRootMass = .6f;
    private const float PullRange = 21f;
    private const float GracePeriod = 12f;
    private const float MinimumMass = .025f;
    private const float SuppressionRange = 12f;
    internal const float RatedPull = 26f;

    [Dependency] private readonly PowerPlantSystem _plant = default!;
    [Dependency] private readonly EventHorizonSystem _horizons = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly GravityMagneticSystem _magnetism = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly ContainmentBreachAlertSystem _alerts = default!;
    [Dependency] private readonly RoundEndSystem _roundEnd = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SpecialRespawnSystem _respawn = default!;
    [Dependency] private readonly GhostSystem _ghosts = default!;
    [Dependency] private readonly MindSystem _minds = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;

    private readonly HashSet<EntityUid> _nearby = new();
    private readonly List<TileRef> _edibleTiles = new();

    private void InitializeHorizon()
    {
        SubscribeLocalEvent<GravityPowerSourceComponent, EntityConsumedByEventHorizonEvent>(OnContainedFeed);
        SubscribeLocalEvent<GravityPowerSourceComponent, TilesConsumedByEventHorizonEvent>(OnContainedTileFeed);
        SubscribeLocalEvent<ResidualMicroBlackHoleComponent, EntityConsumedByEventHorizonEvent>(OnEscapedFeed);
        SubscribeLocalEvent<ResidualMicroBlackHoleComponent, TilesConsumedByEventHorizonEvent>(OnEscapedTileFeed);
    }

    internal static bool IsPowered(PowerConsumerComponent power) =>
        power.NetworkLoad.Enabled && power.DrawRate > 0 && power.ReceivedPower >= power.DrawRate * .99f;

    internal static float Horizon(float mass) => HorizonPerRootMass * MathF.Sqrt(MathF.Max(0, mass));

    private static float CapacityAt(float radius) => MathF.Pow(radius / HorizonPerRootMass, 2);

    private static float ContainedCapacity(GravityPowerSourceComponent source) => CapacityAt(source.FieldLimit * .72f);

    public static float PullAt(float distance, float mass, float activity, bool escaped)
    {
        var edge = MathF.Max(0, distance - Horizon(mass));
        var reach = Math.Clamp((PullRange - edge) / 4, 0, 1);
        var strength = (escaped ? 105 : 70) * MathF.Sqrt(MathF.Max(0, mass)) * activity;
        return MathF.Min(140, strength * reach / (1 + edge * edge * .25f));
    }

    private void UpdateContained(EntityUid uid, GravityPowerSourceComponent source, PowerPlantComponent plant,
        TransformComponent xform, float frameTime, float pulse)
    {
        source.FuelPower = source.AvailablePower;
        var online = source.FuelPower > 0;
        if (source.Mass <= 0 && online && plant.FieldCount >= plant.RequiredFields)
            source.Mass = 1;

        if (source.Mass > 0 && (plant.FieldCount < plant.RequiredFields || !xform.Anchored))
        {
            _plant.Trip((uid, plant), PowerPlantCondition.MissingField);
            Escape(uid, source, xform);
            return;
        }

        source.Mass = MathF.Min(source.Mass, ContainedCapacity(source));
        if (pulse > 0 && source.Mass > 0)
        {
            source.StarvingTime = online ? 0 : source.StarvingTime + pulse;
            var suppression = SuppressionAt(xform);
            var decay = source.StaticProjectors < 4 && source.StarvingTime > GracePeriod ? .012f : 0;
            source.Mass = MathF.Max(0, source.Mass - (decay + suppression) * pulse);
            if (source.Mass < MinimumMass && online)
                _generators.SetFuelGeneratorOn(uid, false);
        }

        if (source.Mass < MinimumMass)
        {
            source.Mass = source.StoredEnergy = source.ResidualActivity = source.AvailablePower = 0;
            UpdateVisual(uid, 0, 0, 0);
            return;
        }

        var target = online ? MathF.Max(.55f, plant.Warmup) : .55f;
        source.ResidualActivity = Math.Clamp(MathF.Max(target,
            source.ResidualActivity - frameTime * .5f), 0, 1);
        var instability = Math.Clamp((plant.Temperature / plant.ShutdownTemperature - .8f) * 5, 0, 1);
        UpdateVisual(uid, source.Mass, source.ResidualActivity, instability, source.FieldLimit);
        if (pulse > 0)
            PulseHorizon(uid, xform, source.Mass, source.ResidualActivity, pulse, false);

        if (!plant.Tripped && !plant.Failed && frameTime > 0)
        {
            var bonus = Math.Clamp((source.Mass - 1) * 150000, 0, 1800000);
            source.AvailablePower += MathF.Min(bonus, source.StoredEnergy / frameTime);
        }
    }

    private void UpdateEscaped(EntityUid uid, ResidualMicroBlackHoleComponent hole, TransformComponent xform, float pulse)
    {
        if (pulse <= 0)
            return;

        hole.StarvingTime += pulse;
        var decay = hole.StarvingTime > GracePeriod ? .012f : 0;
        hole.Mass -= (decay + SuppressionAt(xform)) * pulse;
        if (hole.Mass < MinimumMass)
        {
            QueueDel(uid);
            return;
        }

        hole.Activity = Math.Clamp(.55f + hole.Mass * .45f, 0, 1);
        UpdateVisual(uid, hole.Mass, hole.Activity, 1);
        PulseHorizon(uid, xform, hole.Mass, hole.Activity, pulse, true);
    }

    private float SuppressionAt(TransformComponent xform)
    {
        var position = _transform.GetWorldPosition(xform);
        var strength = 0f;
        var emitters = EntityQueryEnumerator<GravitySuppressionEmitterComponent, PowerConsumerComponent, TransformComponent>();
        while (emitters.MoveNext(out var uid, out _, out var power, out var emitter))
        {
            if (emitter.MapID != xform.MapID || !emitter.Anchored || EntityManager.IsQueuedForDeletion(uid) || !IsPowered(power))
                continue;
            if (Vector2.DistanceSquared(_transform.GetWorldPosition(emitter), position) <= SuppressionRange * SuppressionRange)
                strength += .65f;
        }
        return strength;
    }

    private void PulseHorizon(EntityUid uid, TransformComponent xform, float mass, float activity, float elapsed, bool escaped)
    {
        var position = _transform.GetWorldPosition(xform);
        var radius = Horizon(mass);
        var horizon = Comp<EventHorizonComponent>(uid);
        _nearby.Clear();
        _lookup.GetEntitiesInRange(xform.MapID, position, radius + PullRange, _nearby,
            LookupFlags.Dynamic | LookupFlags.Sundries);
        if (escaped)
            _lookup.GetEntitiesInRange(xform.MapID, position, radius, _nearby, LookupFlags.Static | LookupFlags.Sundries);

        foreach (var entity in _nearby)
        {
            if (entity == uid || TerminatingOrDeleted(entity) || EntityManager.IsQueuedForDeletion(entity) ||
                HasComp<EventHorizonComponent>(entity) || HasComp<GhostComponent>(entity) ||
                !TryComp<PhysicsComponent>(entity, out var body))
                continue;

            var other = Transform(entity);
            if (!escaped && (other.Anchored || body.BodyType == BodyType.Static))
                continue;
            var delta = position - _transform.GetWorldPosition(other);
            var distance = delta.Length();
            // Consider bodies that touch the horizon with their fixtures before their center crosses it.
            if (distance <= radius + (other.Anchored ? 0 : .25f))
            {
                _horizons.AttemptConsumeEntity(uid, entity, horizon);
                continue;
            }

            if (other.Anchored || body.BodyType == BodyType.Static ||
                TryComp<MovedByPressureComponent>(entity, out var pressure) && !pressure.Enabled)
                continue;
            var pull = PullAt(distance, mass, activity, escaped);
            if (_magnetism.IsAdhering(entity))
                pull -= GravityMagneticSystem.HoldingAcceleration;
            if (pull > 0)
                _physics.ApplyLinearImpulse(entity, delta / distance * (pull * body.Mass * elapsed), body: body);
        }

        if (escaped)
        {
            _horizons.ConsumeTilesInRange(uid, radius, xform, horizon);
        }
        else if (radius >= 1 && xform.GridUid is { } gridUid && TryComp<MapGridComponent>(gridUid, out var grid))
        {
            _edibleTiles.Clear();
            var coreTile = _maps.TileIndicesFor(gridUid, grid, xform.Coordinates);
            var coolantAxis = xform.LocalRotation.RotateVec(Vector2.UnitX);
            var horizontal = MathF.Abs(coolantAxis.X) >= MathF.Abs(coolantAxis.Y);
            foreach (var tile in _maps.GetTilesIntersecting(gridUid, grid, new Circle(position, radius)))
            {
                // Keep the core's coolant route connected to the surrounding deck.
                if (horizontal ? tile.GridIndices.Y == coreTile.Y : tile.GridIndices.X == coreTile.X)
                    continue;
                var anchors = _maps.GetAnchoredEntitiesEnumerator(gridUid, grid, tile.GridIndices);
                if (!anchors.MoveNext(out _))
                    _edibleTiles.Add(tile);
            }

            _horizons.AttemptConsumeTiles(uid, _edibleTiles, gridUid, grid, horizon);
        }
    }

    private void UpdateVisual(EntityUid uid, float mass, float activity, float instability, float fieldRadius = 0)
    {
        var visual = Comp<MicroBlackHoleVisualComponent>(uid);
        var radius = MathF.Max(.5f, 2.5f * MathF.Sqrt(mass));
        if (MathF.Abs(visual.Activity - activity) < .005f &&
            MathF.Abs(visual.Instability - instability) < .01f && MathF.Abs(visual.Radius - radius) < .025f &&
            MathF.Abs(visual.FieldRadius - fieldRadius) < .01f)
            return;

        visual.Activity = activity;
        visual.Instability = instability;
        visual.Radius = radius;
        visual.FieldRadius = fieldRadius;
        Dirty(uid, visual);
    }

    private float FeedMass(EntityUid entity)
    {
        // A recovered authentication disk can be fed repeatedly, so it carries no harvestable mass.
        if (IsAuthenticationDisk(entity, out _))
            return 0;
        var mass = TryComp<PhysicsComponent>(entity, out var body) ? body.Mass : 1;
        if (TryComp<StackComponent>(entity, out var stack))
            mass *= stack.Count;
        return Math.Clamp(mass * .025f, .06f, 2.5f);
    }

    private void OnContainedFeed(Entity<GravityPowerSourceComponent> ent, ref EntityConsumedByEventHorizonEvent args)
    {
        AddContainedMass(ent.Comp, FeedMass(args.Entity));
        GhostConsumedPlayer(args.Entity);
        RecoverDisk(ent, args.Entity);
    }

    private void OnContainedTileFeed(Entity<GravityPowerSourceComponent> ent, ref TilesConsumedByEventHorizonEvent args) =>
        AddContainedMass(ent.Comp, args.Tiles.Count * .45f);

    private static void AddContainedMass(GravityPowerSourceComponent source, float addedMass)
    {
        var oldMass = source.Mass;
        source.Mass = MathF.Min(ContainedCapacity(source), oldMass + addedMass);
        source.StoredEnergy += MathF.Max(0, source.Mass - oldMass) * 18000000;
        if (addedMass > 0)
            source.StarvingTime = 0;
    }

    private void OnEscapedFeed(Entity<ResidualMicroBlackHoleComponent> ent, ref EntityConsumedByEventHorizonEvent args)
    {
        ent.Comp.Mass = MathF.Min(CapacityAt(MaximumHorizon), ent.Comp.Mass + FeedMass(args.Entity));
        ent.Comp.StarvingTime = 0;
        GhostConsumedPlayer(args.Entity);
        RecoverDisk(ent, args.Entity);
    }

    private void GhostConsumedPlayer(EntityUid body)
    {
        // Move the player to an observer while the body's coordinates are still valid.
        if (_minds.TryGetMind(body, out var mindId, out var mind) && mind.UserId != null && mind.VisitingEntity == null)
            _ghosts.SpawnGhost((mindId, mind), body);
    }

    private void OnEscapedTileFeed(Entity<ResidualMicroBlackHoleComponent> ent, ref TilesConsumedByEventHorizonEvent args)
    {
        ent.Comp.Mass = MathF.Min(CapacityAt(MaximumHorizon), ent.Comp.Mass + args.Tiles.Count * .45f);
        ent.Comp.StarvingTime = 0;
    }

    private void RecoverDisk(EntityUid hole, EntityUid entity)
    {
        if (!IsAuthenticationDisk(entity, out var respawn))
            return;
        var holeTransform = Transform(hole);
        var station = respawn.StationMap.Item2 is { } originGrid && Exists(originGrid)
            ? _station.GetOwningStation(originGrid)
            : null;
        station ??= _station.GetOwningStation(hole) ?? _station.GetStationInMap(holeTransform.MapID);
        var position = _transform.GetWorldPosition(holeTransform);
        var closest = float.PositiveInfinity;
        var lockers = EntityQueryEnumerator<GravityRecoveryLockerComponent, TransformComponent>();
        while (lockers.MoveNext(out var uid, out _, out var locker))
        {
            if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid) ||
                (station is { } stationUid ? _station.GetOwningStation(uid) != stationUid : locker.MapID != holeTransform.MapID))
                continue;

            var distance = locker.MapID == holeTransform.MapID
                ? Vector2.DistanceSquared(position, _transform.GetWorldPosition(locker))
                : float.MaxValue;
            if (distance <= MaximumHorizon * MaximumHorizon || distance >= closest)
                continue;
            closest = distance;
            respawn.PreferredContainer = uid;
        }
    }

    private bool IsAuthenticationDisk(EntityUid uid, [NotNullWhen(true)] out SpecialRespawnComponent? respawn) =>
        TryComp(uid, out respawn) && respawn.Prototype == "NukeDisk";

    private void OnDestroyed(Entity<GravityPowerSourceComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Mass > 0 && Transform(ent).GridUid is { } grid && !TerminatingOrDeleted(grid))
            Escape(ent, ent.Comp, Transform(ent));
    }

    private void Escape(EntityUid uid, GravityPowerSourceComponent source, TransformComponent xform)
    {
        var mass = source.Mass;
        source.Mass = source.StoredEnergy = source.ResidualActivity = source.AvailablePower = source.FuelPower = 0;
        UpdateVisual(uid, 0, 0, 0);
        // Keep the escaped horizon on the map when its starting tile is eaten.
        var residual = Spawn("ResidualMicroBlackHole", _transform.GetMapCoordinates(uid));
        var hole = Comp<ResidualMicroBlackHoleComponent>(residual);
        hole.Mass = mass;
        UpdateVisual(residual, mass, 1, 1);
        HandleBreach(uid, xform);
    }

    private void HandleBreach(EntityUid uid, TransformComponent xform)
    {
        var station = _station.GetOwningStation(uid) ?? _station.GetStationInMap(xform.MapID);
        if (station is not { } stationUid)
            return;

        _alerts.TrySetEnigma(uid);
        if (HasComp<GravityBreachDispatchedComponent>(stationUid))
            return;
        EnsureComp<GravityBreachDispatchedComponent>(stationUid);
        _chat.DispatchStationAnnouncement(stationUid, Loc.GetString("gravity-breach-announcement"),
            Loc.GetString("gravity-breach-sender"));
        _roundEnd.RequestRoundEnd(TimeSpan.FromMinutes(4), machine: uid, checkCooldown: false, cantRecall: true);

        if (xform.GridUid is not { } grid || xform.MapUid is not { } map)
            return;
        var origin = _transform.GetWorldPosition(xform);
        var stagingArea = EntityCoordinates.Invalid;
        var bestDistance = MathF.Pow(MaximumHorizon + 4, 2);
        for (var i = 0; i < 10; i++)
        {
            if (!_respawn.TryFindRandomTile(grid, map, 12, out var tile))
                continue;
            var distance = Vector2.DistanceSquared(_transform.ToMapCoordinates(tile).Position, origin);
            if (distance <= bestDistance)
                continue;
            bestDistance = distance;
            stagingArea = tile;
            if (bestDistance >= 30 * 30)
                break;
        }

        if (stagingArea == EntityCoordinates.Invalid)
            return;
        Spawn("RandomHumanoidSpawnerERTEngineerEVA", stagingArea);
        Spawn("GravitySuppressionEmitterFlatpack", stagingArea);
        Spawn("GravitySuppressionEmitterFlatpack", stagingArea);
    }
}
