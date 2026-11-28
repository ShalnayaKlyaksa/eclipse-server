using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Industrial;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class IndustrialBulkTankTest : GameTest
{
    [TestCase(2, "IndustrialBulkBarrel", 4000)]
    [TestCase(3, "IndustrialBulkReservoir", 9000)]
    public async Task CompleteFloorSquareAssemblesAndStoresLiquid(int side, string prototype, int volume)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            for (var x = 0; x <= side; x++)
            for (var y = -1; y < side; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);

            EntityUid Spawn(string id, int x, int y) => SEntMan.SpawnEntity(id, new EntityCoordinates(map.Grid, x + .5f, y + .5f));
            var origin = new Vector2i(side - 1, 0);
            var corner = Spawn("IndustrialTankSection", origin.X, origin.Y);
            for (var x = 0; x < side; x++)
            for (var y = 0; y < side; y++)
            {
                var tile = new Vector2i(x, y);
                if (tile != origin && tile != new Vector2i(0, side - 1))
                    Spawn("IndustrialTankSection", x, y);
            }

            var tanks = Server.System<IndustrialBulkTankSystem>();
            Assert.That(tanks.TryAssemble(corner, side), Is.False);
            Spawn("IndustrialTankSection", 0, side - 1);
            Assert.That(tanks.TryAssemble(corner, side), Is.True);
            Assert.That(tanks.TryAssemble(corner, side), Is.False);

            var tank = SEntMan.AllEntityQueryEnumerator<IndustrialBulkTankComponent>();
            Assert.That(tank.MoveNext(out var uid, out var comp));
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID, Is.EqualTo(prototype));
            Assert.That(comp.Supports.Count, Is.EqualTo(side * side - 1));
            Assert.That(comp.Supports.All(support => SEntMan.GetComponent<TransformComponent>(support).Anchored));
            Assert.That(comp.Supports.Contains(uid), Is.False);

            var solutions = Server.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(uid, "tank", out var storage, out var contents));
            Assert.That(contents.MaxVolume, Is.EqualTo(FixedPoint2.New(volume)));
            var inlet = Spawn("PerfectLiquidPipe", origin.X, -1);
            Assert.That(solutions.TryGetSolution(inlet, LiquidPipeComponent.SolutionName, out var feed, out _));
            Assert.That(solutions.TryAddReagent(feed!.Value, "Water", FixedPoint2.New(20)));
            var network = Server.System<LiquidPipeNetworkSystem>();
            for (var tick = 0; tick < 16; tick++)
                network.Update(.25f);
            Assert.That(contents.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(20)));
            Assert.That(tanks.TryDisassemble((uid, comp)), Is.False);

            var outlet = Spawn("PerfectLiquidPipe", side, 0);
            Assert.That(solutions.TryGetSolution(outlet, LiquidPipeComponent.SolutionName, out _, out var drawn));
            for (var tick = 0; tick < 16; tick++)
                network.Update(.25f);
            Assert.That(drawn.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(20)));
            Assert.That(contents.Volume, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(tanks.TryDisassemble((uid, comp)), Is.True);
            Assert.That(comp.Supports.All(SEntMan.Deleted));
            Assert.That(SEntMan.EntityQuery<IndustrialTankSectionComponent>().Count(), Is.EqualTo(side * side));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
