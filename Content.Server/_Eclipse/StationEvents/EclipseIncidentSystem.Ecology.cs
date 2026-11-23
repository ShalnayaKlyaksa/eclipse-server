using System.Numerics;
using System.Linq;
using Content.Server.Spreader;
using Content.Shared.GameTicking.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._Eclipse.StationEvents;

public sealed partial class EclipseIncidentSystem
{
    [Dependency] private readonly MobStateSystem _mobState = default!;

    /// <summary>Level five is rare. Levels six and seven never enter the random Living Hull pool.</summary>
    public static BiologicalHazardLevel SelectHazardLevel(int roll) => roll switch
    {
        < 0 or >= 100 => throw new ArgumentOutOfRangeException(nameof(roll)),
        < 35 => BiologicalHazardLevel.LocalGrowth,
        < 65 => BiologicalHazardLevel.InvasiveGrowth,
        < 87 => BiologicalHazardLevel.HostileEcosystem,
        < 98 => BiologicalHazardLevel.StationOvergrowth,
        _ => BiologicalHazardLevel.Blob,
    };

    private void StartLivingHull(EclipseIncidentComponent comp)
    {
        var level = comp.HazardLevel ?? SelectHazardLevel(RobustRandom.Next(100));
        comp.HazardLevel = level;
        comp.Subject = Spawn(level == BiologicalHazardLevel.Blob ? "EclipseBlobSeedPlaceholder" : "EclipseLivingHeart", comp.Target);
        var core = Comp<EclipseLivingCoreComponent>(comp.Subject.Value);
        core.Level = level;
        var tier = Math.Min(4, (int) level);
        core.MaxGrowth = tier >= 4 && TryComp<MapGridComponent>(comp.Grid, out var grid)
            ? _maps.GetAllTiles(comp.Grid.Value, grid).Count()
            : tier * 180;
        core.NextCreature = Timing.CurTime + TimeSpan.FromSeconds(60);
        SpawnGrowth(comp.Subject.Value, core, comp.Target);
        comp.NextAction = Timing.CurTime + comp.IdentificationDelay;
        Sawmill.Info($"Living Hull on {comp.Grid}: biological hazard {(int) level}; identification delayed.");
    }

    private void TickLivingHull(EntityUid uid, EclipseIncidentComponent comp, GameRuleComponent rule)
    {
        if (IsGone(comp.Subject) || !TryComp<EclipseLivingCoreComponent>(comp.Subject, out var core))
        {
            Announce(comp, "eclipse-incident-living-contained");
            ForceEndSelf(uid, rule);
            return;
        }

        if (comp.Phase == 0)
        {
            AnnounceLocation(comp, "eclipse-incident-living-location");
            Announce(comp, "eclipse-incident-living-classified", ("level", (int) core.Level));
            if (core.Level == BiologicalHazardLevel.Blob)
                Announce(comp, "eclipse-incident-living-blob-seed");
            comp.Phase = 1;
        }

        // Only living hostile creatures count against the cap. Dead bodies remain as evidence and resources.
        core.Creatures.RemoveWhere(ent => !TryComp(ent, out MobStateComponent? state) || _mobState.IsDead(ent, state));
        var tier = Math.Min(4, (int) core.Level);
        if (tier >= 3 && Timing.CurTime >= core.NextCreature && core.Creatures.Count < tier * 2)
        {
            core.Growth.RemoveWhere(ent => !Exists(ent));
            if (core.Growth.Count > 0)
            {
                var growth = new List<EntityUid>(core.Growth);
                var origin = Transform(RobustRandom.Pick(growth)).Coordinates;
                var proto = tier >= 4 && RobustRandom.Prob(0.3f) ? "EclipseMobRootWarden" : "EclipseMobThornCrawler";
                core.Creatures.Add(Spawn(proto, origin));
            }
            core.NextCreature = Timing.CurTime + TimeSpan.FromSeconds(tier >= 4 ? 45 : 75);
        }
        comp.NextAction = Timing.CurTime + TimeSpan.FromSeconds(2);
    }

    private void SpawnGrowth(EntityUid coreUid, EclipseLivingCoreComponent core, EntityCoordinates coordinates)
    {
        var uid = Spawn("EclipseLivingVines", coordinates);
        var growth = Comp<EclipseLivingGrowthComponent>(uid);
        growth.Core = coreUid;
        growth.NextSpread = Timing.CurTime + TimeSpan.FromSeconds(Math.Max(2, 8 - (int) core.Level));
        core.Growth.Add(uid);

        var xform = Transform(uid);
        if (!TryComp<MapGridComponent>(xform.GridUid, out var grid))
            return;

        var tier = Math.Min(4, (int) core.Level);
        // Jungle is a normal solid tile: it preserves a walkable, airtight floor instead of creating space.
        _maps.SetTile(xform.GridUid.Value, grid, xform.Coordinates, new Tile(_tiles["EclipseLivingFloor"].TileId));
        if (tier >= 2 && RobustRandom.Prob(tier * 0.025f))
            Spawn("EclipseLivingTree", xform.Coordinates);
        else if (RobustRandom.Prob(0.12f))
            Spawn("EclipseLivingBush", xform.Coordinates);
    }

    private void OnGrowthSpread(Entity<EclipseLivingGrowthComponent> ent, ref SpreadNeighborsEvent args)
    {
        if (IsGone(ent.Comp.Core) || !TryComp<EclipseLivingCoreComponent>(ent.Comp.Core, out var core))
        {
            RemCompDeferred<ActiveEdgeSpreaderComponent>(ent);
            return;
        }

        if (args.NeighborFreeTiles.Count == 0)
        {
            RemCompDeferred<ActiveEdgeSpreaderComponent>(ent);
            return;
        }

        if (Timing.CurTime < ent.Comp.NextSpread)
            return;

        var tier = Math.Min(4, (int) core.Level);
        ent.Comp.NextSpread = Timing.CurTime + TimeSpan.FromSeconds(8 - tier);
        // Bound the live population, while allowing cleared areas to regrow until the heart is destroyed.
        if (core.Growth.Count >= core.MaxGrowth)
            return;

        foreach (var neighbor in args.NeighborFreeTiles)
        {
            if (args.Updates <= 0 || core.Growth.Count >= core.MaxGrowth)
                break;
            SpawnGrowth(ent.Comp.Core!.Value, core, _maps.GridTileToLocal(neighbor.Tile.GridUid, neighbor.Grid, neighbor.Tile.GridIndices));
            args.Updates--;
            if (tier <= 2)
                break;
        }
    }

    private void OnGrowthRemoved(Entity<EclipseLivingGrowthComponent> ent, ref EntityTerminatingEvent args)
    {
        if (TryComp<EclipseLivingCoreComponent>(ent.Comp.Core, out var core))
            core.Growth.Remove(ent.Owner);
    }

    private void OnColdInit(Entity<EclipseColdSourceComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextTick = Timing.CurTime;
        ent.Comp.Expires = Timing.CurTime + ent.Comp.Lifetime;
    }

    private void UpdateColdSources()
    {
        var query = EntityQueryEnumerator<EclipseColdSourceComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var cold, out var xform))
        {
            if (Paused(uid) || Timing.CurTime < cold.NextTick)
                continue;
            if (Timing.CurTime >= cold.Expires)
            {
                QueueDel(uid);
                continue;
            }
            cold.NextTick = Timing.CurTime + TimeSpan.FromSeconds(2);
            if (!TryComp<MapGridComponent>(xform.GridUid, out var grid))
                continue;

            var center = _maps.CoordinatesToTile(xform.GridUid.Value, grid, xform.Coordinates);
            var warmest = center;
            var warmestTemperature = float.MinValue;
            // Follow the local temperature gradient. Heaters can draw the source into an isolated room.
            foreach (var direction in new[] { Vector2i.Zero, new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
            {
                var index = center + direction;
                if (_atmos.IsTileAirBlockedCached(xform.GridUid.Value, index) || _atmos.IsTileSpace(xform.GridUid.Value, xform.MapUid, index))
                    continue;
                var air = _atmos.GetTileMixture(xform.GridUid, xform.MapUid, index, true);
                if (air == null || air.Temperature <= warmestTemperature)
                    continue;
                warmestTemperature = air.Temperature;
                warmest = index;
            }

            var pending = new Queue<Vector2i>();
            var visited = new HashSet<Vector2i> { center };
            pending.Enqueue(center);
            while (pending.TryDequeue(out var index))
            {
                var offset = index - center;
                if (offset.X * offset.X + offset.Y * offset.Y > cold.Radius * cold.Radius
                    || _atmos.IsTileAirBlockedCached(xform.GridUid.Value, index)
                    || _atmos.IsTileSpace(xform.GridUid.Value, xform.MapUid, index))
                    continue;
                var air = _atmos.GetTileMixture(xform.GridUid, xform.MapUid, index, true);
                if (air is { Immutable: false } && air.Temperature > cold.MinimumTemperature)
                    air.Temperature = MathF.Max(cold.MinimumTemperature, air.Temperature - cold.CoolingPerSecond * 2);
                foreach (var direction in new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
                {
                    var adjacent = index + direction;
                    if (visited.Add(adjacent))
                        pending.Enqueue(adjacent);
                }
            }
            _transform.SetCoordinates(uid, _maps.GridTileToLocal(xform.GridUid.Value, grid, warmest));
        }
    }
}
