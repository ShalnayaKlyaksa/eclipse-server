using System;
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Industrial;
using Content.Server.Power.Generator;
using Content.Server.Stack;
using Content.Server.Temperature.Systems;
using Content.Shared._Eclipse.Industrial;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Research.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Content.Shared.Temperature.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class IndustrialRecipeAuditTest : GameTest
{
    private static readonly ProtoId<IndustrialRecipePrototype> ElectrolysisFuelBlend = "IndustrialElectrolysisFuelBlend";
    private static readonly EntProtoId FuelCellGenerator = "IndustrialFuelCellGenerator";
    private static readonly EntProtoId Electrolyzer = "IndustrialElectrolyzer";

    private string _currentRecipe = "initialization";

    [Test]
    public async Task IndustrialSuppliesHaveAnAssemblerRecipeAndNoLathePack()
    {
        await Audit(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            var resources = Server.System<IndustrialResourceSystem>();
            var assembled = prototypes.EnumeratePrototypes<IndustrialRecipePrototype>()
                .Where(recipe => recipe.Processor == IndustrialProcessorType.Assembler)
                .SelectMany(recipe => recipe.Outputs.Keys).ToHashSet();
            var printable = prototypes.EnumeratePrototypes<LatheRecipePackPrototype>()
                .SelectMany(pack => pack.Recipes)
                .Select(id => prototypes.Index<LatheRecipePrototype>(id).Result)
                .Where(result => result != null)
                .Select(result => resources.Key(result!.Value.Id)).ToHashSet();
            var supplies = prototypes.EnumeratePrototypes<CargoProductPrototype>()
                .Where(product => product.Category == "cargoproduct-category-name-industrial").ToArray();
            Assert.That(supplies, Has.Length.EqualTo(257));
            foreach (var supply in supplies)
            {
                Assert.That(supply.Cost, Is.Positive, supply.ID);
                Assert.That(assembled, Does.Contain(supply.Product.Id), supply.ID);
                Assert.That(printable, Does.Not.Contain(resources.Key(supply.Product.Id)), supply.ID);
            }
        });
    }

    [Test]
    public async Task StarterKitContainsThePartsForOneAssembler()
    {
        var map = await Pair.CreateTestMap();
        await Audit(() =>
        {
            var kit = SEntMan.SpawnEntity("BoxIndustrialAssemblerKit", map.GridCoords);
            var items = SEntMan.GetComponent<StorageComponent>(kit).Container.ContainedEntities;
            var resources = Server.System<IndustrialResourceSystem>();
            Assert.That(resources.Count(items, "IndustrialChassisBlueprint"), Is.EqualTo(1));
            Assert.That(resources.Count(items, "IndustrialIronPlate"), Is.EqualTo(8));
            Assert.That(resources.Count(items, "IndustrialIronRod"), Is.EqualTo(4));
            Assert.That(resources.Count(items, "IndustrialUpgradeModuleAssembler"), Is.EqualTo(1));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    private async Task Audit(Action body)
    {
        try { await Server.WaitAssertion(body); }
        catch (Exception error)
        {
            TestContext.Error.WriteLine(_currentRecipe + ": " + error);
            Assert.Fail(_currentRecipe + ": " + error);
        }
    }
    [Test]
    public async Task EveryMachineRecipeFitsItsRealBuffersAndCommitsExactlyOneBatch()
    {
        var map = await Pair.CreateTestMap();
        await Audit(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            var resources = Server.System<IndustrialResourceSystem>();
            var containers = Server.System<SharedContainerSystem>();
            var stacks = Server.System<StackSystem>();
            var solutions = Server.System<SharedSolutionContainerSystem>();
            var machines = prototypes.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract && !p.TryGetComponent<IndustrialManualProcessorComponent>(out _, SEntMan.ComponentFactory) &&
                            p.TryGetComponent<IndustrialProcessorComponent>(out _, SEntMan.ComponentFactory)).ToArray();
            foreach (var recipe in prototypes.EnumeratePrototypes<IndustrialRecipePrototype>().OrderBy(p => p.ID))
            {
                _currentRecipe = recipe.ID;
                var selected = machines.Select(p => (Prototype: p, Processor: (IndustrialProcessorComponent) p.Components["IndustrialProcessor"].Component))
                    .Where(p => p.Processor.ProcessorType == recipe.Processor && p.Processor.Tier >= recipe.MinimumTier)
                    .OrderBy(p => p.Processor.Tier).ThenBy(p => p.Prototype.ID).First();
                var uid = SEntMan.SpawnEntity(selected.Prototype.ID, map.GridCoords);
                var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
                comp.Enabled = false;
                comp.AutoStart = false;
                comp.CanWorkWithoutPower = true;
                comp.SelectedRecipe = new ProtoId<IndustrialRecipePrototype>(recipe.ID);
                if (recipe.RequiredTechnology is { } technology)
                    SEntMan.GetComponent<IndustrialMachineKnowledgeComponent>(uid).Technologies.Add(technology);
                SEntMan.GetComponent<RoomSanitationComponent>(uid).Grade = 2;
                if (SEntMan.HasComponent<TemperatureComponent>(uid))
                    Server.System<TemperatureSystem>().ForceChangeTemperature(uid, 1000);
                var input = containers.GetContainer(uid, comp.InputContainerId);
                foreach (var (id, quantity) in recipe.Inputs)
                {
                    var remaining = quantity;
                    while (remaining > 0)
                    {
                        var item = SEntMan.SpawnEntity(id, map.GridCoords);
                        var count = 1;
                        if (SEntMan.TryGetComponent<StackComponent>(item, out var stack))
                        {
                            count = Math.Min(remaining, stacks.GetMaxCount(stack));
                            stacks.SetCount((item, stack), count);
                        }
                        Assert.That(containers.Insert(item, input), Is.True, recipe.ID + ": ingredient exceeds the real input buffer: " + id);
                        remaining -= count;
                    }
                }
                if (recipe.FluidInputs.Count > 0 || recipe.Bath.Count > 0)
                {
                    var fluid = SEntMan.GetComponent<IndustrialFluidProcessorComponent>(uid);
                    Assert.That(solutions.TryGetSolution(uid, fluid.InputSolution, out var tank, out _), Is.True, recipe.ID);
                    foreach (var reagent in recipe.FluidInputs.Keys.Union(recipe.Bath.Keys))
                        Assert.That(solutions.TryAddReagent(tank.Value, reagent,
                            recipe.FluidInputs.GetValueOrDefault(reagent) + recipe.Bath.GetValueOrDefault(reagent)), Is.True, recipe.ID);
                }
                comp.AutoStart = true;
                SEntMan.EventBus.RaiseLocalEvent(uid, new IndustrialProcessorToggleMessage { Actor = uid });
                Assert.That(comp.IsWorking, Is.True, recipe.ID + ": a reachable recipe must start in its real machine.");
                comp.AutoStart = false;
                Server.System<IndustrialProcessorSystem>().Update(recipe.Time + 2);
                var output = containers.GetContainer(uid, comp.OutputContainerId);
                Assert.That(input.Count, Is.Zero, recipe.ID + ": paid ingredients were not consumed.");
                foreach (var product in output.ContainedEntities)
                    Assert.That(SEntMan.HasComponent<Content.Shared.Item.ItemComponent>(product), Is.True,
                        recipe.ID + ": production must deliver inventory that a player can remove.");
                foreach (var (id, amount) in recipe.Outputs)
                    Assert.That(resources.Count(output.ContainedEntities, id), Is.EqualTo(amount), recipe.ID + ": " + id);
                if (SEntMan.TryGetComponent<IndustrialFluidProcessorComponent>(uid, out var fluids))
                {
                    solutions.TryGetSolution(uid, fluids.OutputSolution, out _, out var products);
                    foreach (var (id, amount) in recipe.FluidOutputs)
                        Assert.That(products!.GetTotalPrototypeQuantity(id), Is.EqualTo(amount), recipe.ID);
                    solutions.TryGetSolution(uid, fluids.InputSolution, out _, out var bath);
                    Assert.That(bath!.Volume, Is.EqualTo(recipe.Bath.Values.Aggregate(Content.Shared.FixedPoint.FixedPoint2.Zero, (a, b) => a + b)), recipe.ID);
                }
                SEntMan.DeleteEntity(uid);
            }
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task EveryWorkbenchRecipeConsumesItsIngredientsAndProducesTheAdvertisedQuantity()
    {
        var map = await Pair.CreateTestMap();
        await Audit(() =>
        {
            var actor = SEntMan.SpawnEntity(null, new Robust.Shared.Map.EntityCoordinates(map.Grid.Owner, .5f, -.5f));
            SEntMan.EnsureComponent<DoAfterComponent>(actor);
            SEntMan.EnsureComponent<HandsComponent>(actor);
            Server.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>().AddHand(
                (actor, SEntMan.GetComponent<HandsComponent>(actor)), "right", HandLocation.Right);
            SEntMan.EnsureComponent<TagComponent>(actor);
            Server.System<TagSystem>().AddTag(actor, new ProtoId<TagPrototype>("InstantDoAfters"));
            var containers = Server.System<SharedContainerSystem>();
            var stacks = Server.System<StackSystem>();
            var resources = Server.System<IndustrialResourceSystem>();
            foreach (var recipe in Server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<IndustrialFabricationRecipePrototype>())
            {
                _currentRecipe = recipe.ID;
                var bench = SEntMan.SpawnEntity(recipe.MinimumWorkbenchTier > 1 ? "IndustrialWorkbenchTier2" : "IndustrialWorkbench", map.GridCoords);
                if (recipe.RequiredTechnology is { } technology)
                    SEntMan.GetComponent<IndustrialMachineKnowledgeComponent>(bench).Technologies.Add(technology);
                SEntMan.GetComponent<RoomSanitationComponent>(bench).Grade = 2;
                var storage = SEntMan.GetComponent<StorageComponent>(bench);
                foreach (var (id, quantity) in recipe.Ingredients)
                {
                    var remaining = quantity;
                    while (remaining > 0)
                    {
                        var item = SEntMan.SpawnEntity(id, map.GridCoords);
                        var count = 1;
                        if (SEntMan.TryGetComponent<StackComponent>(item, out var stack))
                        {
                            count = Math.Min(remaining, stacks.GetMaxCount(stack));
                            stacks.SetCount((item, stack), count);
                        }
                        Assert.That(containers.Insert(item, storage.Container), Is.True, recipe.ID);
                        remaining -= count;
                    }
                }
                Assert.That(resources.CanAfford(storage.Container.ContainedEntities, recipe.Ingredients), Is.True,
                    recipe.ID + ": the physical ingredient stacks must cover the recipe.");
                Assert.That(Server.System<Content.Shared.Interaction.SharedInteractionSystem>().InRangeAndAccessible(actor, bench, 2), Is.True,
                    recipe.ID + ": the operator must stand outside the workbench fixture.");
                SEntMan.EventBus.RaiseLocalEvent(bench, new IndustrialWorkbenchCraftBuiMessage(recipe.ID, 1) { Actor = actor });
                foreach (var ingredient in recipe.Ingredients.Keys)
                    Assert.That(resources.Count(storage.Container.ContainedEntities, ingredient), Is.Zero,
                        recipe.ID + ": consumed stacks may await end-of-tick deletion, but cannot retain material.");
                var results = new List<EntityUid>();
                var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
                while (query.MoveNext(out var uid, out var meta))
                    if (meta.EntityPrototype?.ID == recipe.Result.Id) results.Add(uid);
                Assert.That(resources.Count(results, recipe.Result.Id), Is.EqualTo(recipe.ResultCount), recipe.ID);
                foreach (var result in results) SEntMan.DeleteEntity(result);
                SEntMan.DeleteEntity(bench);
            }
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task FuelCellWaterCycleConsumesMoreElectricityThanItCanReturnAtEveryMachineTier()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            var recipe = prototypes.Index(ElectrolysisFuelBlend);
            var cell = prototypes.Index(FuelCellGenerator);
            var fuel = (Content.Shared.Power.Generator.FuelGeneratorComponent) cell.Components["FuelGenerator"].Component;
            var chemistry = (Content.Server._Eclipse.Power.ReagentFuelCellComponent) cell.Components["ReagentFuelCell"].Component;
            var thermal = (Content.Server._Eclipse.Power.PowerPlantComponent) cell.Components["PowerPlant"].Component;
            var electrolyzer = prototypes.Index(Electrolyzer);
            var load = ((Content.Server.Power.Components.ApcPowerReceiverComponent) electrolyzer.Components["ApcPowerReceiver"].Component).Load;
            var packets = chemistry.Inputs.Min(p => recipe.FluidOutputs[p.Key].Float() / p.Value);
            var maximumReturnedEnergy = packets * (fuel.OptimalPower / fuel.OptimalBurnRate + thermal.HeatPerFuel);
            Assert.That(packets * chemistry.Outputs[new("Water")], Is.EqualTo(recipe.FluidInputs[new("Water")].Float()));
            foreach (var tier in new[] { MachineTier.Industrial, MachineTier.Perfect })
            {
                var spec = MachineTierHelper.GetSpecs(tier);
                var energy = load * spec.PowerMultiplier * recipe.Time / spec.ProcessingSpeedMultiplier;
                Assert.That(energy, Is.GreaterThan(maximumReturnedEnergy * 2), "Electrolysis must not be a perpetual energy source.");
            }
        });
    }
}
