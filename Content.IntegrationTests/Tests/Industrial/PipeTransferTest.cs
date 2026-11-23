using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Industrial;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class PipeTransferTest : GameTest
{
    [Test]
    public async Task TransferSkipsIncompatibleSourcesAndFullSinks()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var ents = Server.EntMan;
            var maps = Server.System<SharedMapSystem>();
            var containers = Server.System<SharedContainerSystem>();
            var networks = Server.System<ItemPipeNetworkSystem>();
            var pipes = new List<EntityUid>();
            for (var x = 0; x < 5; x++)
            {
                for (var y = -1; y <= 1; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                pipes.Add(ents.SpawnEntity("LpItemPipe", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f)));
            }

            Entity<IndustrialProcessorComponent> Machine(int x, int y, Direction face, FacePortState port)
            {
                var uid = ents.SpawnEntity("BasicIndustrialCrusher", new EntityCoordinates(map.Grid, x + 0.5f, y + 0.5f));
                var comp = ents.GetComponent<IndustrialProcessorComponent>(uid);
                comp.AutoStart = false;
                foreach (var direction in SharedIndustrialProcessorSystem.CardinalDirections)
                    comp.SetFacePort(direction, FacePortState.Disabled);
                comp.SetFacePort(face, port);
                return (uid, comp);
            }

            var incompatible = Machine(0, 1, Direction.South, FacePortState.ItemOutput);
            var source = Machine(2, 1, Direction.South, FacePortState.ItemOutput);
            var full = Machine(1, -1, Direction.North, FacePortState.ItemInput);
            var sink = Machine(3, -1, Direction.North, FacePortState.ItemInput);
            full.Comp.MaxInputSlots = 0;

            var badOutput = containers.GetContainer(incompatible, incompatible.Comp.OutputContainerId);
            var sourceOutput = containers.GetContainer(source, source.Comp.OutputContainerId);
            Assert.That(containers.Insert(ents.SpawnEntity("SheetSteel", map.GridCoords), badOutput), Is.True);
            var ore = ents.SpawnEntity("SteelOre", map.GridCoords);
            var count = ents.GetComponent<StackComponent>(ore).Count;
            Assert.That(containers.Insert(ore, sourceOutput), Is.True);

            networks.RebuildNetworkFrom((pipes[0], ents.GetComponent<ItemPipeComponent>(pipes[0])));
            networks.Update(1f);

            var sinkInput = containers.GetContainer(sink, sink.Comp.InputContainerId);
            Assert.That(sinkInput.Count, Is.EqualTo(1));
            Assert.That(ents.GetComponent<StackComponent>(sinkInput.ContainedEntities[0]).StackTypeId, Is.EqualTo(new Robust.Shared.Prototypes.ProtoId<Content.Shared.Stacks.StackPrototype>("SteelOre")));
            Assert.That(ents.GetComponent<StackComponent>(ore).Count, Is.EqualTo(count - 1));
            Assert.That(badOutput.Count, Is.EqualTo(1));
            Assert.That(containers.GetContainer(full, full.Comp.InputContainerId).Count, Is.Zero);

            networks.Update(0.1f);
            Assert.That(ents.GetComponent<StackComponent>(ore).Count, Is.EqualTo(count - 1), "The transfer delay must still apply.");
            ents.DeleteEntity(map.MapUid);
        });
    }
}
