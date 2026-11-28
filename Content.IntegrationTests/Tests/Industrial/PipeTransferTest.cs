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
    public async Task ResourceStorageAcceptsAndSuppliesPipesWithoutOverfilling()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var ents = Server.EntMan;
            var maps = Server.System<SharedMapSystem>();
            var containers = Server.System<SharedContainerSystem>();
            var networks = Server.System<ItemPipeNetworkSystem>();
            for (var x = 0; x < 5; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);

            EntityUid Spawn(string prototype, int x) => ents.SpawnEntity(prototype, new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));

            var source = Spawn("BasicIndustrialCrusher", 0);
            var inputPipe = Spawn("LpItemPipe", 1);
            var storage = Spawn("IndustrialBuffer", 2);
            var outputPipe = Spawn("LpItemPipe", 3);
            var sink = Spawn("BasicIndustrialCrusher", 4);
            var sourceComp = ents.GetComponent<IndustrialProcessorComponent>(source);
            var storageComp = ents.GetComponent<IndustrialProcessorComponent>(storage);
            var sinkComp = ents.GetComponent<IndustrialProcessorComponent>(sink);
            sourceComp.SetFacePort(Direction.East, FacePortState.ItemOutput);
            Assert.That(storageComp.GetFacePort(Direction.West), Is.EqualTo(FacePortState.ItemInput));
            storageComp.SetFacePort(Direction.East, FacePortState.ItemOutput);
            Assert.That(sinkComp.GetFacePort(Direction.West), Is.EqualTo(FacePortState.ItemInput));
            sinkComp.AutoStart = false;

            var stock = containers.GetContainer(storage, storageComp.InputContainerId);
            Assert.That(containers.GetContainer(storage, storageComp.OutputContainerId), Is.SameAs(stock));
            Assert.That(storageComp.MaxInputSlots, Is.EqualTo(32));
            var user = Spawn("MobHuman", 2);
            ents.EventBus.RaiseLocalEvent(storage, new IndustrialProcessorRecipeMessage("SmeltIronDust") { Actor = user });
            Assert.That(storageComp.SelectedRecipe, Is.Null);
            var ore = ents.SpawnEntity("SteelOre1", map.GridCoords);
            var sourceOutput = containers.GetContainer(source, sourceComp.OutputContainerId);
            Assert.That(containers.Insert(ore, sourceOutput), Is.True);
            for (var i = 0; i < 32; i++)
                Assert.That(containers.Insert(ents.SpawnEntity("PowerCellMediumPrinted", map.GridCoords), stock), Is.True);

            networks.RebuildNetworkFrom((inputPipe, ents.GetComponent<ItemPipeComponent>(inputPipe)));
            networks.RebuildNetworkFrom((outputPipe, ents.GetComponent<ItemPipeComponent>(outputPipe)));
            networks.Update(1f);
            Assert.That(sourceOutput.Contains(ore), Is.True);
            Assert.That(stock.Count, Is.EqualTo(32));

            var removed = stock.ContainedEntities[31];
            var withdraw = new IndustrialProcessorSlotMessage(true, 31, ents.GetNetEntity(removed)) { Actor = user };
            ents.EventBus.RaiseLocalEvent(storage, withdraw);
            Assert.That(stock.Count, Is.EqualTo(31));

            // Keep the ore in storage while testing a click sent before the slot was replaced.
            storageComp.SetFacePort(Direction.East, FacePortState.Disabled);
            networks.InvalidateProcessorConnections((storage, storageComp));
            networks.Update(1f);
            Assert.That(stock.Contains(ore), Is.True);
            ents.EventBus.RaiseLocalEvent(storage, withdraw);
            Assert.That(stock.Contains(ore), Is.True, "A stale click must leave the replacement item in storage.");

            storageComp.SetFacePort(Direction.East, FacePortState.ItemOutput);
            networks.InvalidateProcessorConnections((storage, storageComp));
            networks.Update(1f);
            Assert.That(containers.GetContainer(sink, sinkComp.InputContainerId).Contains(ore), Is.True);
            Assert.That(stock.Count, Is.EqualTo(31));
            ents.DeleteEntity(map.MapUid);
        });
    }

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
