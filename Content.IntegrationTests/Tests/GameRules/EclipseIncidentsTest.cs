#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.StationEvents;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Destructible;
using Content.Server.GameTicking;
using Content.Server.Spreader;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Shared.GameTicking.Components;
using Content.Shared._Eclipse.StationEvents;
using Content.Shared.Atmos.Components;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests.GameRules;

public sealed class EclipseIncidentsTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true, Connected = true };

    [Test]
    public async Task GungnirStartsOnFirstInvocationWithoutAnyFreeStationTile()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var sparseGrid = Server.MapMan.CreateGridEntity(map.MapId);
            foreach (var index in new[] { new Vector2i(-1000, -1000), new Vector2i(1000, 1000) })
            {
                maps.SetTile(sparseGrid.Owner, sparseGrid.Comp, index, map.Tile.Tile);
                SEntMan.SpawnEntity("WallSolid", maps.GridTileToLocal(sparseGrid.Owner, sparseGrid.Comp, index));
            }
            var station = SEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            SEntMan.AddComponent<StationDataComponent>(station);
            SEntMan.AddComponent<StationEventEligibleComponent>(station);
            Server.System<StationSystem>().AddGridToStation(station, sparseGrid);
            var ticker = Server.System<GameTicker>();
            // No walkable tile exists, and almost the entire AABB is space. The launch must not depend on tile sampling.
            for (var i = 0; i < 20; i++)
            {
                Assert.That(ticker.StartGameRule("EclipseGungnirEvent", out var uid), Is.True, $"Invocation {i + 1}");
                Assert.That(ticker.IsGameRuleActive(uid), Is.True);
                var incident = SEntMan.GetComponent<EclipseIncidentComponent>(uid);
                Assert.That(incident.Grid, Is.EqualTo(sparseGrid.Owner));
                Assert.That(incident.Markers, Is.Empty, "No world-space trajectory may reveal the impact corridor.");
                Assert.That(SEntMan.GetComponent<IncomingGungnirComponent>(sparseGrid).SourceBearing.Length(), Is.EqualTo(1).Within(0.001));
                ticker.EndGameRule(uid);
                Assert.That(SEntMan.HasComponent<IncomingGungnirComponent>(sparseGrid), Is.False);
            }
        });
    }

    [Test]
    public async Task IncidentsSpawnAndReactToTheirSourcesBeingDestroyed()
    {
        var map = await Pair.CreateTestMap();
        EntityUid hammerWall = default;
        await Server.WaitAssertion(() =>
        {
            // Validation needs parent prototypes from the whole tree, even when checking only our files.
            var errors = SProtoMan.ValidateDirectory(new ResPath("/Prototypes"), out _);
            Assert.That(errors.Where(pair => pair.Key.Contains("/_Eclipse/StationEvents/"))
                .SelectMany(pair => pair.Value.Select(error => $"{pair.Key}: {error.ErrorReason}")), Is.Empty);
            var maps = Server.System<SharedMapSystem>();
            var tiles = new System.Collections.Generic.List<(Vector2i, Tile)>();
            for (var x = -25; x <= 25; x++)
            for (var y = -25; y <= 25; y++)
                tiles.Add((new Vector2i(x, y), map.Tile.Tile));
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, tiles);
            Server.System<AtmosphereSystem>().RebuildGridAtmosphere((map.Grid.Owner,
                SEntMan.GetComponent<GridAtmosphereComponent>(map.Grid.Owner), map.Grid.Comp));
            var station = SEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            SEntMan.AddComponent<StationDataComponent>(station);
            SEntMan.AddComponent<StationEventEligibleComponent>(station);
            Server.System<StationSystem>().AddGridToStation(station, map.Grid);
        });
        await Pair.RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var ticker = Server.System<GameTicker>();
            var system = Server.System<EclipseIncidentSystem>();
            var timing = Server.ResolveDependency<IGameTiming>();

            EntityUid Start(string prototype, BiologicalHazardLevel? level = null)
            {
                var uid = ticker.AddGameRule(prototype);
                var incident = SEntMan.GetComponent<EclipseIncidentComponent>(uid);
                incident.HazardLevel = level;
                Assert.That(ticker.StartGameRule(uid), Is.True, prototype);
                return uid;
            }

            var livingUid = Start("EclipseLivingHullEvent", BiologicalHazardLevel.StationOvergrowth);
            var living = SEntMan.GetComponent<EclipseIncidentComponent>(livingUid);
            Assert.That(living.Phase, Is.Zero, "Classification must remain undisclosed until the identification timer elapses.");
            var heart = living.Subject!.Value;
            var core = SEntMan.GetComponent<EclipseLivingCoreComponent>(heart);
            Assert.That(core.Growth.Count, Is.EqualTo(1));
            var firstRoots = core.Growth.Single();
            SEntMan.GetComponent<EclipseLivingGrowthComponent>(firstRoots).NextSpread = timing.CurTime;
            Server.System<SpreaderSystem>().Update(1.1f);
            Assert.That(core.Growth.Count, Is.GreaterThan(1), "Living roots must colonize adjacent floor tiles.");
            living.NextAction = timing.CurTime;
            system.Update(0);
            Assert.That(living.Phase, Is.EqualTo(1));
            Server.System<DestructibleSystem>().DestroyEntity(heart);
            living.NextAction = timing.CurTime;
            system.Update(0);
            Assert.That(ticker.IsGameRuleActive(livingUid), Is.False);
            var remainingGrowth = core.Growth.Count;
            SEntMan.GetComponent<EclipseLivingGrowthComponent>(firstRoots).NextSpread = timing.CurTime;
            Server.System<SpreaderSystem>().Update(1.1f);
            Assert.That(core.Growth.Count, Is.EqualTo(remainingGrowth), "Destroying the heart must stop further spread.");

            var blobUid = Start("EclipseLivingHullEvent", BiologicalHazardLevel.Blob);
            var blob = SEntMan.GetComponent<EclipseIncidentComponent>(blobUid);
            Assert.That(SEntMan.HasComponent<EclipseBlobSeedPlaceholderComponent>(blob.Subject), Is.True);
            Assert.That(SEntMan.GetComponent<EclipseLivingCoreComponent>(blob.Subject!.Value).Level, Is.EqualTo(BiologicalHazardLevel.Blob));
            ticker.EndGameRule(blobUid);

            var damoclesUid = Start("EclipseDamoclesEvent");
            var damocles = SEntMan.GetComponent<EclipseIncidentComponent>(damoclesUid);
            damocles.NextAction = timing.CurTime;
            system.Update(0);
            Assert.That(damocles.Subject, Is.Not.Null);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(damocles.Subject!.Value).LinearVelocity.Length(), Is.GreaterThan(0),
                "The approaching projectile must move continuously between server event updates.");
            Server.System<DestructibleSystem>().DestroyEntity(damocles.Subject!.Value);
            damocles.NextAction = timing.CurTime;
            system.Update(0);
            Assert.That(ticker.IsGameRuleActive(damoclesUid), Is.False, "Intercepted projectile must cancel the impact.");

            var coldUid = Start("EclipseZeroPointEvent");
            var cold = SEntMan.GetComponent<EclipseIncidentComponent>(coldUid);
            Assert.That(SEntMan.HasComponent<EclipseColdSourceComponent>(cold.Subject), Is.True);
            var coldTransform = SEntMan.GetComponent<TransformComponent>(cold.Subject!.Value);
            var coldIndex = Server.System<SharedMapSystem>().CoordinatesToTile(map.Grid.Owner, map.Grid.Comp, coldTransform.Coordinates);
            var air = Server.System<AtmosphereSystem>().GetTileMixture(map.Grid.Owner, map.MapUid, coldIndex, true)!;
            var originalTemperature = air.Temperature;
            system.Update(0);
            Assert.That(air.Temperature, Is.LessThan(originalTemperature));
            Server.System<DestructibleSystem>().DestroyEntity(cold.Subject!.Value);
            cold.NextAction = timing.CurTime;
            system.Update(0);
            Assert.That(ticker.IsGameRuleActive(coldUid), Is.False);

            // Vessels exercise procedural grid construction, survivor roles, and ordinary atmosphere.
            foreach (var prototype in new[] { "EclipseDistressVesselEvent", "EclipseOtherShiftEvent" })
            {
                var uid = Start(prototype);
                SEntMan.GetComponent<EclipseIncidentComponent>(uid).NextAction = timing.CurTime;
                system.Update(0);
                Assert.That(ticker.IsGameRuleActive(uid), Is.False);
            }

            var tideUid = Start("EclipseBlackTideEvent");
            var tide = SEntMan.GetComponent<EclipseIncidentComponent>(tideUid);
            tide.NextAction = timing.CurTime;
            system.Update(0);
            Assert.That(tide.Counter, Is.EqualTo(1));
            Assert.That(SEntMan.Count<EclipsePhototaxisComponent>(), Is.GreaterThanOrEqualTo(6));
            ticker.EndGameRule(tideUid);

            // Hatching uses the same initialized lifetime as migrating creatures.
            var cocoon = SEntMan.SpawnEntity("EclipseTideCocoon", map.GridCoords);
            var payload = SEntMan.GetComponent<EclipseTimedPayloadComponent>(cocoon);
            payload.Deadline = timing.CurTime;
            var before = SEntMan.Count<EclipsePhototaxisComponent>();
            system.Update(0);
            Assert.That(payload.Armed, Is.False);
            Assert.That(SEntMan.Count<EclipsePhototaxisComponent>(), Is.EqualTo(before + 3));

            foreach (var prototype in new[] { "EclipseLivingTree", "EclipseLivingBush", "EclipseMobThornCrawler", "EclipseMobRootWarden" })
                Assert.That(SEntMan.EntityExists(SEntMan.SpawnEntity(prototype, map.GridCoords.Offset(new Vector2(20, 20)))), Is.True);

            var hammerUid = Start("EclipseHammerEvent");
            var hammer = SEntMan.GetComponent<EclipseIncidentComponent>(hammerUid);
            hammer.Target = map.GridCoords;
            hammer.Direction = Vector2.UnitX;
            hammer.NextAction = timing.CurTime;
            system.Update(0);
            Assert.That(hammer.Phase, Is.EqualTo(1));
            Assert.That(hammer.Markers, Has.Count.EqualTo(3));
            var impact = SEntMan.GetComponent<TransformComponent>(hammer.Markers[0]).Coordinates;
            var impactIndex = Server.System<SharedMapSystem>().CoordinatesToTile(map.Grid.Owner, map.Grid.Comp, impact);
            hammerWall = SEntMan.SpawnEntity("WallSolid", impact.Offset(new Vector2(2, 0)));
            hammer.NextAction = timing.CurTime;
            system.Update(0);
            Assert.That(hammer.Counter, Is.EqualTo(1));
            Assert.That(Server.System<SharedMapSystem>().GetTileRef(map.Grid.Owner, map.Grid.Comp, impactIndex).Tile.IsEmpty,
                Is.True, "An artillery shell must penetrate the deck at its epicentre.");
            ticker.EndGameRule(hammerUid);

            var gungnirUid = Start("EclipseGungnirEvent");
            var gungnir = SEntMan.GetComponent<EclipseIncidentComponent>(gungnirUid);
            gungnir.Target = map.GridCoords;
            gungnir.Direction = Vector2.UnitX;
            gungnir.HalfLength = 6;
            gungnir.NextAction = timing.CurTime;
            system.Update(0);
            Assert.That(gungnir.Phase, Is.EqualTo(1));
            // A split may invalidate the original grid reference. The committed world-space cut must still finish.
            gungnir.Grid = null;
            gungnir.Target = EntityCoordinates.Invalid;
            for (var i = 0; i < 3; i++)
            {
                gungnir.NextAction = timing.CurTime;
                system.Update(0);
            }
            Assert.That(ticker.IsGameRuleActive(gungnirUid), Is.False);
            Assert.That(Server.System<SharedMapSystem>().GetTileRef(map.Grid.Owner, map.Grid.Comp, Vector2i.Zero).Tile.IsEmpty, Is.True);
        });
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() => Assert.That(SEntMan.EntityExists(hammerWall), Is.False,
            "The main salvo must demolish an ordinary wall beyond the direct impact crater."));
    }
}
