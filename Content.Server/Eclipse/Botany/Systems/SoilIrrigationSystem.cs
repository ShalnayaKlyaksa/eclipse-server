using Content.Server.Botany.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server.Eclipse.Botany.Systems;

public sealed class SoilIrrigationSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private TimeSpan _nextUpdate;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(3);
        DistributeWater();
    }

    public void DistributeWater()
    {
        var beds = EntityQueryEnumerator<IrrigatedSoilComponent, PlantHolderComponent, TransformComponent>();
        while (beds.MoveNext(out _, out var bed, out var transform))
        {
            if (!transform.Anchored || !TryComp<MapGridComponent>(transform.GridUid, out var grid))
                continue;

            var position = _map.TileIndicesFor(grid.Owner, grid, transform.Coordinates);
            // Looking east and north visits each shared edge once per update.
            ExchangeWater(bed, grid, position.Offset(Direction.East));
            ExchangeWater(bed, grid, position.Offset(Direction.North));
        }
    }

    private void ExchangeWater(PlantHolderComponent bed, MapGridComponent grid, Vector2i position)
    {
        var neighbors = _map.GetAnchoredEntitiesEnumerator(grid.Owner, grid, position);
        while (neighbors.MoveNext(out var uid))
        {
            if (!HasComp<IrrigatedSoilComponent>(uid) || !TryComp<PlantHolderComponent>(uid, out var neighbor) || IsPaused(uid))
                continue;

            // Share moisture already in the soil. Reagents remain in the bed that received them.
            var water = (bed.WaterLevel - neighbor.WaterLevel) / 4f;
            bed.WaterLevel -= water;
            neighbor.WaterLevel += water;
        }
    }
}
