using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Industrial;
using Content.Shared._Eclipse.Industrial;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class PipeNetworkRebuildTest : GameTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RebuildRemovesSupersededNetworks(bool liquid)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var ents = Server.EntMan;
            var items = Server.System<ItemPipeNetworkSystem>();
            var liquids = Server.System<LiquidPipeNetworkSystem>();
            var pipes = new List<EntityUid>();
            var oldIds = new HashSet<int>();

            int NetworkId(EntityUid pipe) => liquid
                ? ents.GetComponent<LiquidPipeComponent>(pipe).NetworkId
                : ents.GetComponent<ItemPipeComponent>(pipe).NetworkId;

            bool NetworkExists(int id) => liquid
                ? liquids.TryGetNetwork(id, out _)
                : items.TryGetNetwork(id, out _);

            void Rebuild(EntityUid pipe)
            {
                if (liquid)
                    liquids.RebuildNetworkFrom((pipe, ents.GetComponent<LiquidPipeComponent>(pipe)));
                else
                    items.RebuildNetworkFrom((pipe, ents.GetComponent<ItemPipeComponent>(pipe)));
            }

            for (var i = 0; i < 3; i++)
            {
                Server.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(i, 0), map.Tile.Tile);
                var pipe = ents.SpawnEntity(liquid ? "BasicLiquidPipe" : "LpItemPipe",
                    new EntityCoordinates(map.Grid, i + 0.5f, 0.5f));
                pipes.Add(pipe);
                Rebuild(pipe);
                oldIds.Add(NetworkId(pipe));
            }

            for (var i = 0; i < 10; i++)
            {
                Rebuild(pipes[0]);
                oldIds.Add(NetworkId(pipes[0]));
            }

            var currentId = NetworkId(pipes[0]);
            foreach (var pipe in pipes)
                Assert.That(NetworkId(pipe), Is.EqualTo(currentId));

            foreach (var oldId in oldIds)
                Assert.That(NetworkExists(oldId), Is.EqualTo(oldId == currentId),
                    $"Superseded network {oldId} must not keep processing the same pipes.");

            ents.DeleteEntity(pipes[1]);
            // Topology changes are coalesced at the next system update, before any transfer.
            items.Update(0);
            liquids.Update(0);
            Assert.That(NetworkId(pipes[0]), Is.Not.EqualTo(NetworkId(pipes[2])),
                "Removing the middle pipe must split the network.");
            Assert.That(NetworkExists(currentId), Is.False);

            ents.DeleteEntity(map.MapUid);
        });
    }
}
