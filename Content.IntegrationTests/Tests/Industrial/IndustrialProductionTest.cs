#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Industrial;
using Content.Server.Stack;
using Content.Server.Temperature.Systems;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class IndustrialProductionTest : GameTest
{
    [TestCase("CopperOre1", "DirectSmeltCopper")]
    [TestCase("CopperDust1", "SmeltCopperDust")]
    [TestCase("SheetCopper1", "IndustrialRemeltCopperSheets")]
    public async Task CopperSmeltsIntoIngotsAndFormsSheetsWithoutMultiplyingMaterial(string source, string recipe)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var containers = Server.System<SharedContainerSystem>();
            var resources = Server.System<IndustrialResourceSystem>();
            EntityUid Process(string machine, string recipeId, EntityUid item, string product)
            {
                var uid = SEntMan.SpawnEntity(machine, map.GridCoords);
                var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
                comp.Enabled = false;
                comp.CanWorkWithoutPower = true;
                comp.SelectedRecipe = new ProtoId<Content.Shared._Eclipse.Industrial.Prototypes.IndustrialRecipePrototype>(recipeId);
                Assert.That(containers.Insert(item, containers.GetContainer(uid, comp.InputContainerId)), Is.True);
                SEntMan.EventBus.RaiseLocalEvent(uid, new IndustrialProcessorToggleMessage { Actor = uid });
                Assert.That(comp.IsWorking, Is.True, recipeId);
                Server.System<IndustrialProcessorSystem>().Update(30);
                var output = containers.GetContainer(uid, comp.OutputContainerId);
                Assert.That(resources.Count(output.ContainedEntities, product), Is.EqualTo(1), recipeId);
                var result = output.ContainedEntities.Single();
                Assert.That(containers.Remove(result, output), Is.True);
                return result;
            }
            var ingot = Process("BasicIndustrialSmelter", recipe, SEntMan.SpawnEntity(source, map.GridCoords), "IngotCopper");
            Assert.That(resources.Matches(ingot, "SheetCopper"), Is.False, "Ingots and sheets must have distinct stack identities.");
            var sheet = Process("BasicIndustrialAssembler", "IndustrialFormCopperSheets", ingot, "SheetCopper");
            Process("BasicIndustrialSmelter", "IndustrialRemeltCopperSheets", sheet, "IngotCopper");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task AssemblyDoesNotMultiplyRecyclableMaterials()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            Dictionary<string, long> Composition(Dictionary<string, int> ingredients)
            {
                var result = new Dictionary<string, long>();
                foreach (var (id, amount) in ingredients)
                {
                    var proto = prototypes.Index<EntityPrototype>(id);
                    if (!proto.TryGetComponent<Content.Shared.Materials.PhysicalCompositionComponent>(out var composition,
                            SEntMan.ComponentFactory))
                        continue;
                    foreach (var (material, units) in composition.MaterialComposition)
                        result[material] = result.GetValueOrDefault(material) + (long) units * amount;
                }
                return result;
            }
            Assert.Multiple(() =>
            {
            foreach (var recipe in prototypes.EnumeratePrototypes<Content.Shared._Eclipse.Industrial.Prototypes.IndustrialRecipePrototype>())
            {
                if (recipe.Processor != IndustrialProcessorType.Assembler)
                    continue;
                var input = Composition(recipe.Inputs);
                foreach (var (material, amount) in Composition(recipe.Outputs))
                    Assert.That(amount, Is.LessThanOrEqualTo(input.GetValueOrDefault(material)),
                        $"{recipe.ID} must not create recyclable {material} from assembly and recycling.");
            }
            });
        });
    }

    [TestCase("SteelOre1", "SteelOre")]
    [TestCase("SpaceQuartz1", "SpaceQuartz")]
    [TestCase("CopperOre1", "CopperOre")]
    [TestCase("IngotCopper1", "IngotCopper")]
    [TestCase("SheetSteel1", "SheetSteel")]
    public async Task StackVariantsAndPartialStacksUseOneResource(string single, string full)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var resources = Server.System<IndustrialResourceSystem>();
            var stacks = Server.System<StackSystem>();
            var items = new[] { SEntMan.SpawnEntity(single, map.GridCoords), SEntMan.SpawnEntity(full, map.GridCoords) };
            stacks.SetCount((items[0], SEntMan.GetComponent<StackComponent>(items[0])), 10);
            stacks.SetCount((items[1], SEntMan.GetComponent<StackComponent>(items[1])), 5);
            Assert.That(resources.Count(items, full), Is.EqualTo(15));
            Assert.That(resources.TryPlan(items, new Dictionary<string, int> { [full] = 15 }, out var plan), Is.True);
            Assert.That(plan.Sum(part => part.Count), Is.EqualTo(15));
            Assert.That(resources.CanAfford(items, new Dictionary<string, int> { [full] = 10, [single] = 10 }), Is.False,
                "Aliased ingredient names must not spend the same stack twice.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task ColdLoadedCentrifugeStartsWhenHeated()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var containers = Server.System<SharedContainerSystem>();
            var processor = Server.System<IndustrialProcessorSystem>();
            var uid = SEntMan.SpawnEntity("BasicIndustrialThermalCentrifuge", map.GridCoords);
            var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
            var input = containers.GetContainer(uid, comp.InputContainerId);
            var ore = SEntMan.SpawnEntity("CrushedIronOre1", map.GridCoords);
            Server.System<StackSystem>().SetCount((ore, SEntMan.GetComponent<StackComponent>(ore)), 2);
            Assert.That(containers.Insert(ore, input), Is.True);
            Assert.That(comp.IsWorking, Is.False);
            Server.System<TemperatureSystem>().ForceChangeTemperature(uid, 600);
            Assert.That(comp.IsWorking, Is.True, "No manual reinsertion should be required after warm-up.");
            processor.Update(10f);
            var output = containers.GetContainer(uid, comp.OutputContainerId);
            var resources = Server.System<IndustrialResourceSystem>();
            Assert.That(resources.Count(output.ContainedEntities, "SievedIronOre"), Is.EqualTo(2));
            Assert.That(resources.Count(output.ContainedEntities, "QuartzDust"), Is.EqualTo(1));
            Assert.That(input.Count, Is.Zero);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task FullOutputPreservesIngredientsAndResumesAfterStackSpaceIsFreed()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var containers = Server.System<SharedContainerSystem>();
            var stacks = Server.System<StackSystem>();
            var processor = Server.System<IndustrialProcessorSystem>();
            var uid = SEntMan.SpawnEntity("BasicIndustrialCrusher", map.GridCoords);
            var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
            comp.CanWorkWithoutPower = true;
            comp.MaxOutputSlots = 1;
            var input = containers.GetContainer(uid, comp.InputContainerId);
            var output = containers.GetContainer(uid, comp.OutputContainerId);
            Assert.That(containers.Insert(SEntMan.SpawnEntity("SteelOre1", map.GridCoords), input), Is.True);
            Assert.That(comp.IsWorking, Is.True);
            var existing = SEntMan.SpawnEntity("CrushedIronOre1", map.GridCoords);
            stacks.SetCount((existing, SEntMan.GetComponent<StackComponent>(existing)), 29);
            Assert.That(containers.Insert(existing, output), Is.True);
            processor.Update(10f);
            Assert.That(input.Count, Is.EqualTo(1), "A blocked result must not consume its inputs.");
            Assert.That(comp.ProcessingAccumulator, Is.EqualTo(comp.ProcessingTime));
            stacks.TryUse((existing, SEntMan.GetComponent<StackComponent>(existing)), 1);
            processor.Update(0.1f);
            Assert.That(input.Count, Is.Zero);
            Assert.That(SEntMan.GetComponent<StackComponent>(existing).Count, Is.EqualTo(30));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task FullContainerNeverAddsAnUnmergedEntity()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var containers = Server.System<SharedContainerSystem>();
            var uid = SEntMan.SpawnEntity("BasicIndustrialCrusher", map.GridCoords);
            var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
            comp.AutoStart = false;
            comp.MaxInputSlots = 1;
            var input = containers.GetContainer(uid, comp.InputContainerId);
            var first = SEntMan.SpawnEntity("SteelOre1", map.GridCoords);
            var second = SEntMan.SpawnEntity("SteelOre1", map.GridCoords);
            Assert.That(containers.Insert(first, input), Is.True);
            Assert.That(containers.Insert(second, input), Is.False);
            Assert.That(input.Count, Is.EqualTo(1));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task TransfersPreserveDevicesAndSkipBlockedProductTypes()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var containers = Server.System<SharedContainerSystem>();
            var processor = Server.System<IndustrialProcessorSystem>();
            var sourceUid = SEntMan.SpawnEntity("IndustrialBuffer", map.GridCoords);
            var sinkUid = SEntMan.SpawnEntity("IndustrialBuffer", map.GridCoords);
            var source = SEntMan.GetComponent<IndustrialProcessorComponent>(sourceUid);
            var sink = SEntMan.GetComponent<IndustrialProcessorComponent>(sinkUid);
            var output = containers.GetContainer(sourceUid, source.OutputContainerId);
            var input = containers.GetContainer(sinkUid, sink.InputContainerId);
            var cell = SEntMan.SpawnEntity("PowerCellMediumPrinted", map.GridCoords);
            Assert.That(containers.Insert(cell, output), Is.True);
            Assert.That(processor.TryPipeTransfer((sourceUid, source), (sinkUid, sink)), Is.True);
            Assert.That(input.Contains(cell), Is.True, "Moving a device must preserve its entity, charge and contents.");

            var crusherUid = SEntMan.SpawnEntity("BasicIndustrialCrusher", map.GridCoords);
            var crusher = SEntMan.GetComponent<IndustrialProcessorComponent>(crusherUid);
            crusher.AutoStart = false;
            Assert.That(containers.Insert(SEntMan.SpawnEntity("SheetSteel1", map.GridCoords), output), Is.True);
            Assert.That(containers.Insert(SEntMan.SpawnEntity("SteelOre1", map.GridCoords), output), Is.True);
            Assert.That(processor.TryPipeTransfer((sourceUid, source), (crusherUid, crusher)), Is.True,
                "An incompatible first output must not hide a transferable second product.");
            Assert.That(Server.System<IndustrialResourceSystem>().Count(output.ContainedEntities, "SheetSteel"), Is.EqualTo(1));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task PoweredAssemblerConsumesSplitStacksAndProducesEveryNonStackItem()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var containers = Server.System<SharedContainerSystem>();
            var processor = Server.System<IndustrialProcessorSystem>();
            var uid = SEntMan.SpawnEntity("BasicIndustrialAssembler", map.GridCoords);
            var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
            comp.CanWorkWithoutPower = true;
            SEntMan.EnsureComponent<IndustrialMachineKnowledgeComponent>(uid).Technologies.Add("IndustrialAutomation");
            comp.SelectedRecipe = new ProtoId<Content.Shared._Eclipse.Industrial.Prototypes.IndustrialRecipePrototype>("IndustrialSyringes");
            var input = containers.GetContainer(uid, comp.InputContainerId);
            Assert.That(containers.Insert(SEntMan.SpawnEntity("SheetGlass1", map.GridCoords), input), Is.True);
            Assert.That(containers.Insert(SEntMan.SpawnEntity("SheetSteel1", map.GridCoords), input), Is.True);
            processor.Update(10f);
            var output = containers.GetContainer(uid, comp.OutputContainerId);
            Assert.That(output.Count, Is.EqualTo(2), "Non-stack outputs must be spawned individually.");
            Assert.That(input.Count, Is.Zero);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task AutomatedOreLineDeliversDoubleYieldAndRetainsByproducts()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var containers = Server.System<SharedContainerSystem>();
            var networks = Server.System<ItemPipeNetworkSystem>();
            var processor = Server.System<IndustrialProcessorSystem>();
            for (var x = -1; x <= 10; x++)
            {
                for (var y = 0; y <= 2; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }
            var pipes = new List<EntityUid>();
            for (var x = -1; x <= 8; x++)
            {
                pipes.Add(SEntMan.SpawnEntity("LpItemPipe", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f)));
                pipes.Add(SEntMan.SpawnEntity("LpItemPipe", new EntityCoordinates(map.Grid, x + 0.5f, 2.5f)));
            }
            pipes.Add(SEntMan.SpawnEntity("LpItemPipe", new EntityCoordinates(map.Grid, -0.5f, 1.5f)));
            Entity<IndustrialProcessorComponent> Machine(string prototype, int x, string? recipe)
            {
                var uid = SEntMan.SpawnEntity(prototype, new EntityCoordinates(map.Grid, x + 0.5f, 1.5f));
                var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
                comp.CanWorkWithoutPower = true;
                foreach (var direction in SharedIndustrialProcessorSystem.CardinalDirections)
                    comp.SetFacePort(direction, FacePortState.Disabled);
                comp.SetFacePort(Direction.North, FacePortState.ItemOutput);
                comp.SetFacePort(Direction.South, FacePortState.ItemInput);
                if (recipe != null)
                    comp.SelectedRecipe = new ProtoId<Content.Shared._Eclipse.Industrial.Prototypes.IndustrialRecipePrototype>(recipe);
                return (uid, comp);
            }
            var receiving = Machine("IndustrialBuffer", 0, null);
            receiving.Comp.SetFacePort(Direction.South, FacePortState.Disabled);
            Machine("BasicIndustrialCrusher", 2, "CrushSteelOre");
            var centrifuge = Machine("BasicIndustrialThermalCentrifuge", 4, "ThermalCentrifugeCrushedIronOre");
            Server.System<TemperatureSystem>().ForceChangeTemperature(centrifuge.Owner, 800);
            Machine("BasicIndustrialCrusher", 6, "CrushSievedIronOre");
            var smelter = Machine("BasicIndustrialSmelter", 8, "SmeltIronDust");
            smelter.Comp.SetFacePort(Direction.North, FacePortState.Disabled);
            smelter.Comp.SetFacePort(Direction.East, FacePortState.ItemOutput);
            var finished = Machine("IndustrialBuffer", 10, null);
            finished.Comp.SetFacePort(Direction.North, FacePortState.Disabled);
            finished.Comp.SetFacePort(Direction.South, FacePortState.Disabled);
            finished.Comp.SetFacePort(Direction.West, FacePortState.ItemInput);
            var finishedPipe = SEntMan.SpawnEntity("LpItemPipe", new EntityCoordinates(map.Grid, 9.5f, 1.5f));
            var ore = SEntMan.SpawnEntity("SteelOre1", map.GridCoords);
            Server.System<StackSystem>().SetCount((ore, SEntMan.GetComponent<StackComponent>(ore)), 2);
            Assert.That(containers.Insert(ore, containers.GetContainer(receiving, receiving.Comp.InputContainerId)), Is.True);
            networks.RebuildNetworkFrom((pipes[0], SEntMan.GetComponent<ItemPipeComponent>(pipes[0])));
            networks.RebuildNetworkFrom((finishedPipe, SEntMan.GetComponent<ItemPipeComponent>(finishedPipe)));
            for (var i = 0; i < 120; i++)
            {
                processor.Update(1f);
                networks.Update(1f);
            }
            var resources = Server.System<IndustrialResourceSystem>();
            Assert.That(resources.Count(containers.GetContainer(finished, finished.Comp.InputContainerId).ContainedEntities,
                "IndustrialIronIngot"), Is.EqualTo(4), "Two mined ore units must deliver four iron ingots; steel is a separate alloy operation.");
            Assert.That(resources.Count(containers.GetContainer(centrifuge, centrifuge.Comp.OutputContainerId).ContainedEntities,
                "QuartzDust"), Is.EqualTo(2), "Recovered impurities must remain available for another production branch.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
