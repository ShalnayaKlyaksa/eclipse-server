using System.Collections.Generic;
using System.Numerics;
using Content.Client._Eclipse.Power;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._Eclipse.Power;
using Content.Server.Materials;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Power.Generator;
using Content.Shared._Eclipse.Power;
using Content.Shared.Clothing;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Power.Generator;
using Content.Shared.Respawn;
using Content.Shared.Storage.Components;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class GravityPowerTest : GameTest
{
    [Test]
    [EnsureCVar(Side.Server, typeof(CVars), nameof(CVars.NetPVS), true)]
    public async Task LargeEscapedHoleIsReadyToDrawBeforeItsCenterEntersPvs()
    {
        var map = await Pair.CreateTestMap();
        EntityUid viewer = default;
        NetEntity hole = default;
        NetEntity marker = default;
        await Server.WaitAssertion(() =>
        {
            viewer = SEntMan.SpawnEntity("MobObserver", map.MapCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, viewer);
            var origin = map.MapCoords.Offset(new Vector2(60, 0));
            var residual = SEntMan.SpawnEntity("ResidualMicroBlackHole", origin);
            SEntMan.GetComponent<ResidualMicroBlackHoleComponent>(residual).Mass = 100;
            hole = SEntMan.GetNetEntity(residual);
            marker = SEntMan.GetNetEntity(SEntMan.SpawnEntity(null, origin));
        });
        await Pair.RunTicksSync(20);
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.TryGetEntity(marker, out _), Is.False);
            Assert.That(CEntMan.TryGetEntity(hole, out var entity), Is.True);
            var visuals = CEntMan.System<MicroBlackHoleVisualSystem>();
            visuals.FrameUpdate(2f);
            Assert.That(visuals.GetDisplay(entity!.Value).Radius, Is.GreaterThan(24f));
        });

        await Server.WaitPost(() => Server.System<SharedTransformSystem>().SetWorldPosition(viewer, new Vector2(29, 0)));
        await Pair.RunTicksSync(20);
        await Client.WaitAssertion(() =>
        {
            // The 25-tile disk can now overlap the view, though an entity at its center is still out of PVS.
            Assert.That(CEntMan.TryGetEntity(marker, out _), Is.False);
            Assert.That(CEntMan.TryGetEntity(hole, out var entity), Is.True);
            Assert.That(CEntMan.System<MicroBlackHoleVisualSystem>().GetDisplay(entity!.Value).Radius, Is.GreaterThan(24f));
        });
    }

    [Test]
    public async Task DestroyingActiveContainmentTransfersItsMassToAnEscapedHole()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var hole = SEntMan.SpawnEntity("IndustrialMicroBlackHoleGenerator", map.GridCoords);
            SEntMan.GetComponent<GravityPowerSourceComponent>(hole).Mass = 2;
            SEntMan.DeleteEntity(hole);
            var query = SEntMan.EntityQueryEnumerator<ResidualMicroBlackHoleComponent>();
            Assert.That(query.MoveNext(out var residual, out var state), Is.True);
            Assert.That(state!.Mass, Is.EqualTo(2));
            Assert.That(SEntMan.HasComponent<PowerSupplierComponent>(residual), Is.False);
            Assert.That(SEntMan.HasComponent<FuelGeneratorComponent>(residual), Is.False);
            var gravity = Server.System<GravityPowerSystem>();
            gravity.Update(.5f);
            Assert.That(SEntMan.GetComponent<MicroBlackHoleVisualComponent>(residual).Instability, Is.EqualTo(1));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task KineticGeneratorsShareOneFuelPaymentAndCannotGenerateAfterItIsSpent()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            for (var x = -4; x <= 4; x++)
            for (var y = -4; y <= 4; y++)
            {
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                SEntMan.SpawnEntity("CableHV", new EntityCoordinates(map.Grid.Owner, x + .5f, y + .5f));
            }
            var hole = SEntMan.SpawnEntity("IndustrialMicroBlackHoleGenerator", map.GridCoords);
            var transform = Server.System<SharedTransformSystem>();
            transform.AnchorEntity(hole);
            var plant = SEntMan.GetComponent<PowerPlantComponent>(hole);
            plant.FieldCount = 4;
            plant.Warmup = 1;
            var generators = Server.System<GeneratorSystem>();
            Server.System<MaterialStorageSystem>().TryChangeMaterialAmount(hole, "Steel", 1000);
            var collectors = new List<EntityUid>();
            foreach (var offset in new[] { new Vector2i(-3, 0), new Vector2i(3, 0), new Vector2i(0, -3), new Vector2i(0, 3) })
            {
                var collector = SEntMan.SpawnEntity("GravitationalKineticGenerator", new EntityCoordinates(map.Grid.Owner, .5f + offset.X, .5f + offset.Y));
                transform.AnchorEntity(collector);
                SEntMan.GetComponent<GravitationalKineticGeneratorComponent>(collector).RotorSpeed = 1;
                collectors.Add(collector);
            }
            Server.System<NodeGroupSystem>().ForceUpdate();
            var before = generators.GetFuel(hole);
            generators.SetFuelGeneratorOn(hole, true);
            generators.Update(1);
            var gravity = Server.System<GravityPowerSystem>();
            gravity.Update(1);
            var source = SEntMan.GetComponent<GravityPowerSourceComponent>(hole);
            Assert.That(source.HarvestedPower, Is.EqualTo(1200000).Within(1));
            Assert.That(source.Collectors, Is.EqualTo(4));
            Assert.That(generators.GetFuel(hole), Is.LessThan(before));
            Assert.That(SEntMan.GetComponent<PowerSupplierComponent>(hole).MaxSupply, Is.Zero);
            foreach (var collector in collectors)
                Assert.That(SEntMan.GetComponent<PowerSupplierComponent>(collector).MaxSupply, Is.EqualTo(300000).Within(1));
            gravity.Update(1);
            Assert.That(source.HarvestedPower, Is.Zero, "The same fuel payment cannot be harvested twice.");
            foreach (var collector in collectors)
                Assert.That(SEntMan.GetComponent<PowerSupplierComponent>(collector).Enabled, Is.False);
            generators.Update(1);
            transform.Unanchor(collectors[0]);
            gravity.Update(1);
            Assert.That(SEntMan.GetComponent<PowerSupplierComponent>(collectors[0]).MaxSupply, Is.Zero);
            Assert.That(source.HarvestedPower, Is.LessThanOrEqualTo(source.AvailablePower));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task FieldFailureTransfersGravityAndConsumesLooseObjects()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var hole = SEntMan.SpawnEntity("IndustrialMicroBlackHoleGenerator", map.GridCoords);
            var transform = Server.System<SharedTransformSystem>();
            transform.AnchorEntity(hole);
            var source = SEntMan.GetComponent<GravityPowerSourceComponent>(hole);
            source.ResidualActivity = 1;
            source.Mass = 1;
            var plant = SEntMan.GetComponent<PowerPlantComponent>(hole);
            plant.Tripped = true;
            plant.Condition = PowerPlantCondition.MissingField;
            var loose = SEntMan.SpawnEntity("NukeDiskFake", new EntityCoordinates(map.Grid.Owner, 2.5f, .5f));
            var swallowed = SEntMan.SpawnEntity("SheetSteel", new EntityCoordinates(map.Grid.Owner, .6f, .5f));
            var maps = Server.System<SharedMapSystem>();
            var wallTile = new Vector2i(1, 0);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, wallTile, map.Tile.Tile);
            var wall = SEntMan.SpawnEntity("WallReinforced", new EntityCoordinates(map.Grid.Owner, 1.5f, .5f));
            var gravity = Server.System<GravityPowerSystem>();
            gravity.Update(.5f);
            gravity.Update(.5f);
            Assert.That(SEntMan.IsQueuedForDeletion(swallowed), Is.True, "Loose items crossing the horizon must not accumulate forever.");
            Assert.That(source.AvailablePower, Is.Zero);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(loose).LinearVelocity.X, Is.LessThan(0));
            Assert.That(source.ResidualActivity, Is.Zero);
            var escaped = SEntMan.EntityQueryEnumerator<ResidualMicroBlackHoleComponent>();
            Assert.That(escaped.MoveNext(out _, out var state), Is.True);
            Assert.That(state!.Mass, Is.GreaterThan(1));
            Assert.That(state.PendingMass, Is.GreaterThan(0), "Consumed mass should be added gradually.");
            for (var i = 0; i < 10; i++)
                gravity.Update(.2f);
            Assert.That(SEntMan.IsQueuedForDeletion(wall), Is.True);
            Assert.That(maps.GetTileRef(map.Grid.Owner, map.Grid.Comp, wallTile).Tile.IsEmpty, Is.True);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task EscapedHoleMovesTowardAnAnchoredPoweredLure()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            Server.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(5, 0), map.Tile.Tile);
            var transform = Server.System<SharedTransformSystem>();
            var lure = SEntMan.SpawnEntity("GravityBlackHoleLure", new EntityCoordinates(map.Grid.Owner, 5.5f, 0.5f));
            transform.AnchorEntity(lure);
            var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(lure);
            power.Powered = false;
            var hole = SEntMan.SpawnEntity("ResidualMicroBlackHole", map.MapCoords);
            var start = transform.GetWorldPosition(hole);
            var gravity = Server.System<GravityPowerSystem>();
            gravity.Update(.2f);
            Assert.That(transform.GetWorldPosition(hole), Is.EqualTo(start));
            power.Powered = true;
            gravity.Update(.2f);
            Assert.That(transform.GetWorldPosition(hole).X, Is.GreaterThan(start.X));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public void LocalPullFadesWithDistanceAndGrowsWithMass()
    {
        var edge = GravityPowerSystem.PullAt(.6f, 1, 1, false);
        var tenTiles = GravityPowerSystem.PullAt(10.6f, 1, 1, false);
        var twentyTiles = GravityPowerSystem.PullAt(20.6f, 1, 1, false);
        Assert.That(edge, Is.GreaterThan(tenTiles * 10));
        Assert.That(tenTiles, Is.GreaterThan(twentyTiles * 10));
        Assert.That(twentyTiles, Is.GreaterThan(0));
        Assert.That(GravityPowerSystem.PullAt(22, 1, 1, false), Is.Zero);
        Assert.That(GravityPowerSystem.PullAt(10.6f, 4, 1, false), Is.GreaterThan(tenTiles));
        Assert.That(GravityPowerSystem.PullAt(23, 4, 1, true), Is.GreaterThan(0));
        Assert.That(GravityPowerSystem.PullAt(10.6f, 1, 1, true), Is.GreaterThan(tenTiles));
    }

    [Test]
    public async Task ContainedGrowthIsCappedAndAuthenticationDiskReturnsToCaptainLocker()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            Server.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(8, 0), map.Tile.Tile);
            var hole = SEntMan.SpawnEntity("IndustrialMicroBlackHoleGenerator", map.GridCoords);
            Server.System<SharedTransformSystem>().AnchorEntity(hole);
            SEntMan.GetComponent<PowerPlantComponent>(hole).FieldCount = 4;
            var source = SEntMan.GetComponent<GravityPowerSourceComponent>(hole);
            source.FieldLimit = 1f;
            source.Mass = 1;
            var locker = SEntMan.SpawnEntity("LockerCaptain", new EntityCoordinates(map.Grid.Owner, 8.5f, .5f));
            var disk = SEntMan.SpawnEntity("NukeDisk", map.GridCoords);
            var food = SEntMan.SpawnEntity("SheetSteel10", map.GridCoords);
            Server.System<GravityPowerSystem>().Update(.2f);
            Assert.That(SEntMan.IsQueuedForDeletion(food), Is.True);
            Assert.That(source.Mass, Is.EqualTo(.72f * .72f / (.6f * .6f)).Within(.001f));
            Assert.That(source.StoredEnergy, Is.GreaterThan(0));
            Assert.That(SEntMan.IsQueuedForDeletion(disk), Is.True);
            Assert.That(SEntMan.GetComponent<SpecialRespawnComponent>(disk).PreferredContainer, Is.EqualTo(locker));
            SEntMan.DeleteEntity(disk);
            Assert.That(SEntMan.GetComponent<EntityStorageComponent>(locker).Contents.ContainedEntities.Count, Is.EqualTo(1));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task FourPoweredProjectorsStopDecayAndAnEmitterCanStillReduceTheHorizon()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var transform = Server.System<SharedTransformSystem>();
            var turf = new List<(Vector2i, Tile)>();
            for (var x = -5; x <= 5; x++)
            for (var y = -4; y <= 4; y++)
                turf.Add((new Vector2i(x, y), map.Tile.Tile));
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, turf);
            var hole = SEntMan.SpawnEntity("IndustrialMicroBlackHoleGenerator", map.GridCoords);
            transform.AnchorEntity(hole);
            var source = SEntMan.GetComponent<GravityPowerSourceComponent>(hole);
            source.Mass = 2;
            source.StarvingTime = 20;

            PowerConsumerComponent Install(string prototype, int x, int y)
            {
                var device = SEntMan.SpawnEntity(prototype, new EntityCoordinates(map.Grid.Owner, x + .5f, y + .5f));
                transform.AnchorEntity(device);
                var power = SEntMan.GetComponent<PowerConsumerComponent>(device);
                power.NetworkLoad.ReceivingPower = power.DrawRate;
                return power;
            }

            Install("PowerPlantFieldStabilizer", 2, 0);
            Install("PowerPlantFieldStabilizer", -3, 0);
            Install("PowerPlantFieldStabilizer", 0, 3);
            Install("PowerPlantFieldStabilizer", 0, -3);
            var fourth = Install("GravityStaticChargeProjector", 4, 0);
            Install("GravityStaticChargeProjector", -4, 0);
            Install("GravityStaticChargeProjector", 0, 4);
            Install("GravityStaticChargeProjector", 0, -4);

            var plant = Server.System<PowerPlantSystem>();
            var gravity = Server.System<GravityPowerSystem>();
            plant.Update(.5f);
            gravity.Update(.2f);
            Assert.That(source.FieldLimit, Is.EqualTo(1.5f));
            Assert.That(source.StaticProjectors, Is.EqualTo(4));
            Assert.That(source.Mass, Is.EqualTo(2));

            fourth.NetworkLoad.ReceivingPower = 0;
            plant.Update(.5f);
            gravity.Update(.2f);
            Assert.That(source.StaticProjectors, Is.EqualTo(3));
            Assert.That(source.Mass, Is.LessThan(2));

            fourth.NetworkLoad.ReceivingPower = fourth.DrawRate;
            Install("GravitySuppressionEmitter", 5, 0);
            plant.Update(.5f);
            gravity.Update(.2f);
            Assert.That(source.StaticProjectors, Is.EqualTo(4));
            Assert.That(source.Mass, Is.LessThan(1.9f));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task MagneticFloorAndPressureResistanceReducePull()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var hole = SEntMan.SpawnEntity("IndustrialMicroBlackHoleGenerator", map.GridCoords);
            Server.System<SharedTransformSystem>().AnchorEntity(hole);
            SEntMan.GetComponent<GravityPowerSourceComponent>(hole).Mass = 1;
            SEntMan.GetComponent<PowerPlantComponent>(hole).FieldCount = 4;
            var coords = new EntityCoordinates(map.Grid.Owner, 6.5f, .5f);
            var tiles = Server.ResolveDependency<ITileDefinitionManager>();
            var maps = Server.System<SharedMapSystem>();
            for (var x = 1; x <= 6; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), new Tile(tiles["FloorSteel"].TileId));
            var magnetic = SEntMan.SpawnEntity("ToolboxMechanical", coords);
            var braced = SEntMan.SpawnEntity("MobHuman", coords);
            var loose = SEntMan.SpawnEntity("NukeDiskFake", coords);
            var unbracedPerson = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 3.5f, .5f));
            var boots = SEntMan.SpawnEntity("ClothingShoesBootsMag", coords);
            Server.System<SharedMagbootsSystem>().UpdateMagbootEffects(braced,
                (boots, SEntMan.GetComponent<MagbootsComponent>(boots)), true);
            Assert.That(Server.System<GravityMagneticSystem>().IsAdhering(magnetic), Is.True);
            Server.System<GravityPowerSystem>().Update(.2f);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(magnetic).LinearVelocity.X, Is.Zero);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(braced).LinearVelocity.X, Is.Zero);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(loose).LinearVelocity.X, Is.Negative);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(unbracedPerson).LinearVelocity.X, Is.LessThan(-3));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task ContainedHorizonEatsBareFloorAndMovesConsumedPlayerToObserver()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var transform = Server.System<SharedTransformSystem>();
            var openFloor = new Vector2i(0, 1);
            var anchoredFloor = new Vector2i(0, -1);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, openFloor, map.Tile.Tile);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, anchoredFloor, map.Tile.Tile);
            SEntMan.SpawnEntity("WallReinforced", new EntityCoordinates(map.Grid.Owner, .5f, -.5f));
            var hole = SEntMan.SpawnEntity("IndustrialMicroBlackHoleGenerator", map.GridCoords);
            transform.AnchorEntity(hole);
            SEntMan.GetComponent<GravityPowerSourceComponent>(hole).Mass = 4;
            SEntMan.GetComponent<PowerPlantComponent>(hole).FieldCount = 4;

            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var minds = Server.System<SharedMindSystem>();
            var mind = minds.GetOrCreateMind(Pair.Player!.UserId);
            minds.TransferTo(mind.Owner, body);
            Server.System<GravityPowerSystem>().Update(.2f);

            Assert.That(SEntMan.IsQueuedForDeletion(body), Is.True);
            Assert.That(mind.Comp.OwnedEntity, Is.Not.Null);
            Assert.That(SEntMan.HasComponent<GhostComponent>(mind.Comp.OwnedEntity!.Value), Is.True);
            Assert.That(transform.GetMapCoordinates(mind.Comp.OwnedEntity.Value).MapId, Is.EqualTo(map.MapId));
            Assert.That(maps.GetTileRef(map.Grid.Owner, map.Grid.Comp, openFloor).Tile.IsEmpty, Is.True);
            Assert.That(maps.GetTileRef(map.Grid.Owner, map.Grid.Comp, anchoredFloor).Tile.IsEmpty, Is.False);
            minds.TransferTo(mind.Owner, null, createGhost: false);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task UnbracedPlayerIsPulledAcrossTheHorizonDuringNormalPhysics()
    {
        var map = await Pair.CreateTestMap();
        var body = EntityUid.Invalid;
        var hole = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var turf = new List<(Vector2i, Tile)>();
            for (var x = -4; x <= 4; x++)
            for (var y = -4; y <= 4; y++)
                turf.Add((new Vector2i(x, y), map.Tile.Tile));
            maps.SetTiles(map.Grid.Owner, map.Grid.Comp, turf);

            hole = SEntMan.SpawnEntity("IndustrialMicroBlackHoleGenerator", map.GridCoords);
            Server.System<SharedTransformSystem>().AnchorEntity(hole);
            SEntMan.GetComponent<GravityPowerSourceComponent>(hole).Mass = 4;
            SEntMan.GetComponent<PowerPlantComponent>(hole).RequiredFields = 0;
            body = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 1.35f, 2.8f));
            var minds = Server.System<SharedMindSystem>();
            minds.TransferTo(minds.GetOrCreateMind(Pair.Player!.UserId).Owner, body);
        });

        await Pair.RunTicksSync(120);
        var consumed = false;
        var observer = false;
        var coreStayedInPlace = false;
        float distance = 0;
        await Server.WaitAssertion(() =>
        {
            consumed = !SEntMan.EntityExists(body);
            observer = Pair.Player!.AttachedEntity is { } attached && SEntMan.HasComponent<GhostComponent>(attached);
            coreStayedInPlace = SEntMan.GetComponent<TransformComponent>(hole).GridUid == map.Grid.Owner;
            if (!consumed)
            {
                var transform = Server.System<SharedTransformSystem>();
                distance = System.Numerics.Vector2.Distance(transform.GetWorldPosition(body), transform.GetWorldPosition(hole));
            }
            var minds = Server.System<SharedMindSystem>();
            if (minds.TryGetMind(Pair.Player.UserId, out var mind))
                minds.TransferTo(mind.Value.Owner, null, createGhost: false);
            SEntMan.DeleteEntity(map.MapUid);
        });

        Assert.That(coreStayedInPlace, Is.True, "Losing bare tiles must not separate the core from the station grid.");
        Assert.That(consumed, Is.True, $"The player is still {distance:F2} tiles from the center after 120 ticks.");
        Assert.That(observer, Is.True);
    }
}
