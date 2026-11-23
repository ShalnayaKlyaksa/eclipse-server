using Content.Server.Atmos.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Atmos;
using Content.Shared.Examine;
using Content.Shared.Fluids.Components;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Eclipse.Industrial;

/// <summary>Bounded room scans per controller, never a flood-fill per machine or per frame.</summary>
public sealed class RoomSanitationSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    private float _elapsed;
    private readonly Dictionary<(EntityUid, Vector2i), (int Grade, float Cleanliness)> _rooms = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<CleanroomControllerComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<RoomSanitationComponent, ExaminedEvent>(OnReading);
    }

    private void OnExamine(Entity<CleanroomControllerComponent> ent, ref ExaminedEvent args)
        => args.PushMarkup(Loc.GetString("industrial-cleanroom-reading", ("grade", ent.Comp.Grade), ("cleanliness", (int) ent.Comp.Cleanliness)));

    private void OnReading(Entity<RoomSanitationComponent> ent, ref ExaminedEvent args)
        => args.PushMarkup(Loc.GetString("industrial-cleanroom-reading", ("grade", ent.Comp.Grade), ("cleanliness", (int) ent.Comp.Cleanliness)));

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 2f)
            return;
        var step = MathF.Min(_elapsed, 4f);
        _elapsed = 0;
        _rooms.Clear();
        var controllers = EntityQueryEnumerator<CleanroomControllerComponent, TransformComponent>();
        while (controllers.MoveNext(out var uid, out var controller, out var xform))
        {
            if (!xform.Anchored || xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid) ||
                !this.IsPowered(uid, EntityManager))
            {
                controller.Cleanliness = MathF.Max(0, controller.Cleanliness - step * 8);
                continue;
            }
            var origin = _map.TileIndicesFor(gridUid, grid, _transform.GetMapCoordinates(uid));
            var enclosed = TryGetRoom(gridUid, grid, origin, controller.MaximumTiles, out var tiles);
            var air = _atmos.GetContainingMixture(uid);
            var breathable = air != null && air.Pressure >= 60 && air.Pressure <= 140 &&
                air.Temperature >= 285 && air.Temperature <= 305 &&
                air.GetMoles(Gas.Plasma) < 0.01f && air.GetMoles(Gas.Tritium) < 0.01f;
            var dirty = false;
            var puddles = EntityQueryEnumerator<PuddleComponent, TransformComponent>();
            while (puddles.MoveNext(out var puddle, out _, out var puddleXform))
            {
                if (puddleXform.GridUid == gridUid && tiles.Contains(_map.TileIndicesFor(gridUid, grid, _transform.GetMapCoordinates(puddle))))
                {
                    dirty = true;
                    break;
                }
            }
            controller.Cleanliness = Math.Clamp(controller.Cleanliness + step *
                (enclosed && breathable && !dirty ? controller.PurificationPerSecond : -8f), 0, 100);
            if (!enclosed || !breathable || dirty)
                continue;
            var grade = controller.Cleanliness >= (controller.Grade >= 2 ? 95 : 80) ? controller.Grade : 0;
            foreach (var tile in tiles)
            {
                var key = (gridUid, tile);
                if (!_rooms.TryGetValue(key, out var old) || grade > old.Grade || grade == old.Grade && controller.Cleanliness > old.Cleanliness)
                    _rooms[key] = (grade, controller.Cleanliness);
            }
        }
        var readings = EntityQueryEnumerator<RoomSanitationComponent, TransformComponent>();
        while (readings.MoveNext(out var uid, out var reading, out var xform))
        {
            var value = xform.GridUid is { } gridUid && TryComp<MapGridComponent>(gridUid, out var grid) &&
                _rooms.TryGetValue((gridUid, _map.TileIndicesFor(gridUid, grid, _transform.GetMapCoordinates(uid))), out var measured)
                    ? measured : (Grade: 0, Cleanliness: 0f);
            if (reading.Grade == value.Grade && MathF.Abs(reading.Cleanliness - value.Cleanliness) < 0.1f)
                continue;
            reading.Grade = value.Grade;
            reading.Cleanliness = value.Cleanliness;
            Dirty(uid, reading);
        }
    }

    public bool TryGetRoom(EntityUid uid, MapGridComponent grid, Vector2i origin, int maximum, out HashSet<Vector2i> tiles)
    {
        tiles = new();
        var queue = new Queue<Vector2i>();
        queue.Enqueue(origin);
        while (queue.TryDequeue(out var tile))
        {
            if (tiles.Contains(tile) || Blocked(uid, grid, tile))
                continue;
            if (_map.GetTileRef(uid, grid, tile).Tile.IsEmpty || tiles.Count >= Math.Clamp(maximum, 1, 256))
                return false;
            tiles.Add(tile);
            foreach (var direction in SharedIndustrialProcessorSystem.CardinalDirections)
            {
                var next = tile.Offset(direction);
                var flag = direction switch
                {
                    Robust.Shared.Maths.Direction.North => AtmosDirection.North,
                    Robust.Shared.Maths.Direction.East => AtmosDirection.East,
                    Robust.Shared.Maths.Direction.South => AtmosDirection.South,
                    _ => AtmosDirection.West,
                };
                var opposite = direction switch
                {
                    Robust.Shared.Maths.Direction.North => AtmosDirection.South,
                    Robust.Shared.Maths.Direction.East => AtmosDirection.West,
                    Robust.Shared.Maths.Direction.South => AtmosDirection.North,
                    _ => AtmosDirection.East,
                };
                if (!EdgeBlocked(uid, grid, tile, flag) && !EdgeBlocked(uid, grid, next, opposite))
                    queue.Enqueue(next);
            }
        }
        return tiles.Count > 0;
    }

    private bool Blocked(EntityUid uid, MapGridComponent grid, Vector2i tile)
        => EdgeBlocked(uid, grid, tile, AtmosDirection.All, true);

    private bool EdgeBlocked(EntityUid uid, MapGridComponent grid, Vector2i tile, AtmosDirection direction, bool fully = false)
    {
        var entities = _map.GetAnchoredEntitiesEnumerator(uid, grid, tile);
        while (entities.MoveNext(out var entity))
        {
            if (TryComp<AirtightComponent>(entity, out var airtight) && airtight.AirBlocked &&
                (fully ? airtight.CurrentAirBlockedDirection == (int) AtmosDirection.All :
                    (airtight.CurrentAirBlockedDirection & (int) direction) != 0))
                return true;
        }
        return false;
    }
}
