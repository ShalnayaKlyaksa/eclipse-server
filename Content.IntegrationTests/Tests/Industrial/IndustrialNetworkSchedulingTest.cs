using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Industrial;
using Content.Server.Stack;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class IndustrialNetworkSchedulingTest : GameTest
{
    [Test]
    public async Task BuildingAPipeLineCoalescesTopologyChanges()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var networks = Server.System<ItemPipeNetworkSystem>();
            var before = networks.TopologyRebuilds;
            for (var x = 0; x < 32; x++)
            {
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                SEntMan.SpawnEntity("LpItemPipe", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            }
            networks.Update(0);
            Assert.That(networks.TopologyRebuilds - before, Is.EqualTo(1),
                "One construction batch must rebuild the connected component once, not once per pipe or neighbour.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task SourcesShareCapacityAndPipeModesControlEndpoints()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var pipes = new EntityUid[3];
            for (var x = 0; x < 3; x++)
            {
                for (var y = -1; y <= 1; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                pipes[x] = SEntMan.SpawnEntity("LpItemPipe", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            }
            var containers = Server.System<SharedContainerSystem>();
            EntityUid Source(int x)
            {
                var uid = SEntMan.SpawnEntity("IndustrialBuffer", new EntityCoordinates(map.Grid, x + 0.5f, 1.5f));
                var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
                foreach (var direction in SharedIndustrialProcessorSystem.CardinalDirections)
                    comp.SetFacePort(direction, FacePortState.Disabled);
                comp.SetFacePort(Direction.South, FacePortState.ItemOutput);
                var ore = SEntMan.SpawnEntity("SteelOre", map.GridCoords);
                Server.System<StackSystem>().SetCount((ore, SEntMan.GetComponent<StackComponent>(ore)), 4);
                Assert.That(containers.Insert(ore, containers.GetContainer(uid, comp.OutputContainerId)), Is.True);
                return ore;
            }
            var first = Source(0);
            var second = Source(2);
            var sink = SEntMan.SpawnEntity("IndustrialBuffer", new EntityCoordinates(map.Grid, 1.5f, -0.5f));
            var receiver = SEntMan.GetComponent<IndustrialProcessorComponent>(sink);
            foreach (var direction in SharedIndustrialProcessorSystem.CardinalDirections)
                receiver.SetFacePort(direction, FacePortState.Disabled);
            receiver.SetFacePort(Direction.North, FacePortState.ItemInput);
            var networks = Server.System<ItemPipeNetworkSystem>();
            networks.RebuildNetworkFrom((pipes[0], SEntMan.GetComponent<ItemPipeComponent>(pipes[0])));
            networks.Update(1);
            networks.Update(1);
            Assert.That(SEntMan.GetComponent<StackComponent>(first).Count, Is.EqualTo(3));
            Assert.That(SEntMan.GetComponent<StackComponent>(second).Count, Is.EqualTo(3));
            var pipe = SEntMan.GetComponent<ItemPipeComponent>(pipes[0]);
            pipe.TransferMode = PipeTransferMode.Insert;
            networks.InvalidateEndpoints(pipe.NetworkId);
            networks.Update(1);
            Assert.That(SEntMan.GetComponent<StackComponent>(first).Count, Is.EqualTo(3),
                "An insert-only pipe must not extract from its neighbouring source.");
            Assert.That(SEntMan.GetComponent<StackComponent>(second).Count, Is.EqualTo(2));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [TestCase("LpItemPipe", 1)]
    [TestCase("MpItemPipe", 3)]
    [TestCase("HpItemPipe", 6)]
    public async Task NetworkReachesThroughputAndCachesUnchangedEndpoints(string prototype, int throughput)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            for (var y = -1; y <= 1; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(0, y), map.Tile.Tile);
            var pipe = SEntMan.SpawnEntity(prototype, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            Entity<IndustrialProcessorComponent> Machine(int y, FacePortState port)
            {
                var uid = SEntMan.SpawnEntity("IndustrialBuffer", new EntityCoordinates(map.Grid, 0.5f, y + 0.5f));
                var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
                foreach (var direction in SharedIndustrialProcessorSystem.CardinalDirections)
                    comp.SetFacePort(direction, FacePortState.Disabled);
                comp.SetFacePort(y > 0 ? Direction.South : Direction.North, port);
                comp.MaxAutoTransferPerSecond = 10;
                return (uid, comp);
            }
            var source = Machine(1, FacePortState.ItemOutput);
            var sink = Machine(-1, FacePortState.ItemInput);
            var containers = Server.System<SharedContainerSystem>();
            var input = containers.GetContainer(sink, sink.Comp.InputContainerId);
            var output = containers.GetContainer(source, source.Comp.OutputContainerId);
            var ore = SEntMan.SpawnEntity("SteelOre", map.GridCoords);
            Assert.That(containers.Insert(ore, output), Is.True);
            var networks = Server.System<ItemPipeNetworkSystem>();
            networks.RebuildNetworkFrom((pipe, SEntMan.GetComponent<ItemPipeComponent>(pipe)));
            var id = SEntMan.GetComponent<ItemPipeComponent>(pipe).NetworkId;
            Assert.That(networks.TryGetNetwork(id, out var network), Is.True);
            networks.Update(1f);
            Assert.That(Server.System<IndustrialResourceSystem>().Count(input.ContainedEntities, "SteelOre"), Is.EqualTo(throughput));
            var rebuilds = network.EndpointRebuilds;
            SEntMan.DeleteEntity(ore);
            for (var i = 0; i < 200; i++)
                networks.Update(0.01f);
            Assert.That(network.EndpointRebuilds, Is.EqualTo(rebuilds),
                "Empty-network retries must not rescan its pipes and rebuild endpoint lists.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task UnanchoringMiddlePipeSplitsItsNetwork()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var pipes = new EntityUid[3];
            for (var x = 0; x < 3; x++)
            {
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                pipes[x] = SEntMan.SpawnEntity("LpItemPipe", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            }
            var networks = Server.System<ItemPipeNetworkSystem>();
            networks.RebuildNetworkFrom((pipes[0], SEntMan.GetComponent<ItemPipeComponent>(pipes[0])));
            Server.System<SharedTransformSystem>().Unanchor(pipes[1]);
            networks.Update(0);
            Assert.That(SEntMan.GetComponent<ItemPipeComponent>(pipes[0]).NetworkId,
                Is.Not.EqualTo(SEntMan.GetComponent<ItemPipeComponent>(pipes[2]).NetworkId));
            Assert.That(SEntMan.GetComponent<ItemPipeComponent>(pipes[1]).NetworkId, Is.EqualTo(-1));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
