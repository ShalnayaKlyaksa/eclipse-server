using System.Numerics;
using Content.Server.NPC.Pathfinding;
using Content.Server.Shuttles.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Body;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._Eclipse.StationEvents;

public sealed partial class EclipseIncidentSystem
{
    [Dependency] private readonly SharedVisualBodySystem _visualBody = default!;
    /// <summary>A small derelict built from normal map entities, with two distinct interior layouts.</summary>
    private void SpawnVessel(EclipseIncidentComponent incident)
    {
        var target = _transform.ToMapCoordinates(incident.Target);
        var grid = _mapManager.CreateGridEntity(target.MapId);
        var otherShift = incident.Kind == EclipseIncidentKind.OtherShift;
        var width = otherShift ? 21 : 13;
        const int height = 11;
        var floor = new Tile(_tiles["FloorSteel"].TileId);
        var tiles = new List<(Vector2i, Tile)>();
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
            tiles.Add((new Vector2i(x, y), floor));
        _maps.SetTiles(grid.Owner, grid.Comp, tiles);
        EnsureComp<ShuttleComponent>(grid);
        EnsureComp<GravityComponent>(grid);
        EnsureComp<GridPathfindingComponent>(grid);
        _metadata.SetEntityName(grid, Loc.GetString(otherShift ? "eclipse-other-shift-grid" : "eclipse-distress-grid"));

        var location = target.Position + incident.Direction * (incident.HalfLength + 25);
        _transform.SetWorldPosition(grid, location);

        EntityCoordinates At(int x, int y) => new(grid.Owner, new Vector2(x + 0.5f, y + 0.5f));
        // A two-door vestibule allows boarding without immediately venting the whole vessel.
        for (var x = 0; x < width; x++)
        for (var y = 0; y < height; y++)
        {
            var exterior = x == 0 || x == width - 1 || y == 0 || y == height - 1;
            if (exterior)
            {
                var entity = Spawn(x == width - 1 && y == 5 ? "AirlockShuttle" : "WallShuttle", At(x, y));
                if (x == width - 1 && y == 5)
                    _transform.SetLocalRotation(entity, Angle.FromDegrees(90));
            }
            else if (x == width - 3)
                Spawn(y == 5 ? "AirlockGlass" : "WallSolid", At(x, y));
            else if (otherShift && x == 9)
                Spawn(y == 3 || y == 7 ? "AirlockGlass" : "WallSolid", At(x, y));
        }

        // Initialize the new sealed grid immediately, before spawning survivors. Ordinary atmosphere updates
        // have not created its tile mixtures yet. This only rebuilds the newly constructed derelict.
        _atmos.RebuildGridAtmosphere((grid.Owner, EnsureComp<GridAtmosphereComponent>(grid), grid.Comp));

        foreach (var point in new[] { new Vector2i(3, 2), new Vector2i(3, 8), new Vector2i(width - 5, 2) })
            Spawn("EclipseDerelictLamp", At(point.X, point.Y));
        Spawn("Table", At(3, 5));
        Spawn("Chair", At(2, 5));
        Spawn("EclipseDistressLog", At(3, 5));
        Spawn("CrateEmergencyO2Kit", At(5, 2));
        Spawn("CrateEmergencyBruteKit", At(5, 8));

        var variant = RobustRandom.Next(4);
        if (otherShift)
        {
            var crewMembers = new List<EntityUid>();
            var crew = EntityQueryEnumerator<HumanoidProfileComponent, TransformComponent, MetaDataComponent>();
            while (crew.MoveNext(out var crewUid, out _, out var xform, out var metadata))
            {
                if (xform.GridUid == incident.Grid)
                    crewMembers.Add(crewUid);
            }
            for (var i = 0; i < 3; i++)
            {
                var source = crewMembers.Count > 0 ? RobustRandom.Pick(crewMembers) : (EntityUid?) null;
                var prototype = "MobHuman";
                if (TryComp<HumanoidProfileComponent>(source, out var profile)
                    && PrototypeManager.Resolve(profile.Species, out var species))
                    prototype = species.Prototype;
                var echo = Spawn(prototype, At(12 + i * 2, 3 + i));
                if (source is { } original)
                {
                    _metadata.SetEntityName(echo, MetaData(original).EntityName);
                    _visualBody.CopyAppearanceFrom(original, echo);
                }
                _damage.TryChangeDamage(echo, new DamageSpecifier { DamageDict = { ["Asphyxiation"] = 300 } }, ignoreResistances: true);
            }
            Spawn("EclipseEchoSurvivor", At(4, 7));
            var coreUid = Spawn("EclipseLivingHeart", At(16, 8));
            var core = Comp<EclipseLivingCoreComponent>(coreUid);
            core.Level = BiologicalHazardLevel.InvasiveGrowth;
            SpawnGrowth(coreUid, core, At(16, 8));
            Spawn("CrateEmergencyAdvancedKit", At(14, 7));
        }
        else
        {
            switch (variant)
            {
                case 0:
                    Spawn("EclipseDistressSurvivor", At(4, 7));
                    break;
                case 1:
                    Spawn("MobSpiderSpace", At(4, 7));
                    Spawn("MobSpiderSpace", At(6, 3));
                    break;
                case 2:
                    Spawn("EclipseTideCocoon", At(4, 7));
                    Spawn("EclipseTideCocoon", At(6, 3));
                    break;
                case 3:
                    var coreUid = Spawn("EclipseLivingHeart", At(4, 7));
                    var core = Comp<EclipseLivingCoreComponent>(coreUid);
                    SpawnGrowth(coreUid, core, At(4, 7));
                    break;
            }
        }
        Announce(incident, otherShift ? "eclipse-incident-shift-arrived" : "eclipse-incident-vessel-arrived",
            ("x", (int) location.X), ("y", (int) location.Y));
        Sawmill.Info($"Spawned {incident.Kind} vessel {grid.Owner}, variant {variant}, at {location}.");
    }
}
