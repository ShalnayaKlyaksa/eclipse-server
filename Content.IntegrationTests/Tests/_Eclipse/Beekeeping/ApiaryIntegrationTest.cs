using System.Linq;
using Content.Client._Eclipse.Beekeeping;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Beekeeping;
using Content.Server._Eclipse.Industrial;
using Content.Server.Temperature.Systems;
using Content.Shared._Eclipse.Beekeeping;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Inventory;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Eclipse.Beekeeping;

public sealed class ApiaryIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task ColonyWorksOnlyWhenSuppliedAndCompletesExactlyOnce()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var system = SEntMan.System<ApiarySystem>();
            var containers = SEntMan.System<SharedContainerSystem>();
            var hive = SEntMan.SpawnEntity("EclipseApiary", map.MapCoords);
            var person = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var apiary = SEntMan.GetComponent<ApiaryComponent>(hive);
            var separationRecipes = SProtoMan.EnumeratePrototypes<IndustrialRecipePrototype>()
                .Where(recipe => recipe.Processor == IndustrialProcessorType.ThermalCentrifuge &&
                    recipe.ID.StartsWith("EclipseSeparate")).ToArray();
            foreach (var species in SProtoMan.EnumeratePrototypes<BeeSpeciesPrototype>())
            {
                Assert.That(species.RareChance, Is.InRange(0, 0.1f));
                Assert.That(species.CycleSeconds, Is.GreaterThanOrEqualTo(120));
                Assert.That(SProtoMan.HasIndex(species.Comb), Is.True);
                if (species.RareProduct != null) Assert.That(SProtoMan.HasIndex(species.RareProduct.Value), Is.True);
                if (species.Byproduct != null) Assert.That(SProtoMan.HasIndex(species.Byproduct.Value), Is.True);
                Assert.That(species.MinTemperature, Is.LessThan(species.MaxTemperature));
                Assert.That(separationRecipes.Count(recipe => recipe.Inputs.ContainsKey(species.Comb.Id)),
                    Is.EqualTo(1), species.ID);
                foreach (var caste in new[] { BeeCaste.Princess, BeeCaste.Drone })
                {
                    var variant = species.ID == "Meadow" ? $"EclipseBee{caste}" : $"EclipseBee{species.ID}{caste}";
                    var prototype = SProtoMan.Index<EntityPrototype>(variant);
                    Assert.That(prototype.Abstract, Is.False, variant);
                    Assert.That(prototype.TryGetComponent<BeeSpecimenComponent>(out var specimen, SEntMan.ComponentFactory),
                        Is.True, variant);
                    Assert.That(specimen!.Caste, Is.EqualTo(caste), variant);
                    Assert.That(specimen.Genome.Dominant, Is.EqualTo(species.ID), variant);
                    Assert.That(specimen.Genome.Recessive, Is.EqualTo(species.ID), variant);
                }
            }
            foreach (var id in new[] { "EclipseBuildApiaryFrame", "EclipseBuildAdvancedApiaryBoard", "EclipsePressOrdinaryHoneycomb", "EclipsePressHerbalHoneycomb", "EclipsePressEnergeticHoneycomb", "EclipsePressRoyalHoneycomb", "EclipseBuildSelectionFrame", "EclipseBuildNursingFrame", "EclipseBuildInsulatedFrame" })
                Assert.That(SProtoMan.HasIndex<IndustrialRecipePrototype>(id), Is.True, id);
            var first = system.CreateBee(hive, new BeeGenome(), BeeCaste.Princess);
            var second = system.CreateBee(hive, new BeeGenome { Dominant = "Forest", Recessive = "Forest" }, BeeCaste.Drone);
            containers.Insert(first, apiary.Input); containers.Insert(second, apiary.Input);
            Assert.That(system.Start((hive, apiary)), Is.True);
            system.AdvanceActive((hive, apiary), 120);
            Assert.That(apiary.Progress, Is.Zero, "Starved colonies cannot accumulate time or roll rewards.");

            void Use(EntityUid item, EntityUid target)
            {
                var action = new InteractUsingEvent(person, item, target, SEntMan.GetComponent<TransformComponent>(target).Coordinates);
                SEntMan.EventBus.RaiseLocalEvent(target, action);
                Assert.That(action.Handled, Is.True);
            }
            var flower = SEntMan.SpawnEntity("FoodLily", map.MapCoords);
            Use(flower, hive);
            Assert.That(apiary.Nectar, Is.GreaterThan(0), "Fresh flowers, including inedible produce, must feed bees.");
            var beaker = SEntMan.SpawnEntity("LargeBeaker", map.MapCoords);
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var drain = SEntMan.GetComponent<DrainableSolutionComponent>(beaker);
            solutions.TryGetSolution(beaker, drain.Solution, out var liquid);
            solutions.TryAddReagent(liquid.Value, "Water", FixedPoint2.New(20));
            Use(beaker, hive);
            Assert.That(apiary.Water, Is.EqualTo(20));
            var smoker = SEntMan.SpawnEntity("EclipseBeeSmoker", map.MapCoords);
            Use(smoker, hive);
            Assert.That(apiary.SmokeSeconds, Is.EqualTo(60));
            Assert.That(SEntMan.GetComponent<BeeSmokerComponent>(smoker).Charges, Is.EqualTo(9));
            apiary.Nectar = apiary.Water = 200;
            system.AdvanceActive((hive, apiary), 119);
            Assert.That(apiary.CompletedCycles, Is.Zero);
            apiary.Enabled = false;
            system.AdvanceActive((hive, apiary), 1000);
            Assert.That(apiary.Progress, Is.EqualTo(119));
            apiary.Enabled = true;
            system.AdvanceActive((hive, apiary), 1);
            Assert.That(apiary.CompletedCycles, Is.EqualTo(1));
            var outputCount = apiary.Output.ContainedEntities.Count;
            Assert.That(outputCount, Is.GreaterThanOrEqualTo(4));
            foreach (var uid in apiary.Output.ContainedEntities)
                if (SEntMan.TryGetComponent<BeeSpecimenComponent>(uid, out var child))
                {
                    Assert.That(child.Genome.Generation, Is.EqualTo(1));
                    Assert.That(child.Genome.LineageSeconds, Is.EqualTo(120));
                }
            system.AdvanceActive((hive, apiary), 100000);
            Assert.That(apiary.CompletedCycles, Is.EqualTo(1));
            Assert.That(apiary.Output.ContainedEntities.Count, Is.EqualTo(outputCount), "An ended queen cannot reroll.");

            var fresh = system.CreateBee(hive, new BeeGenome { Dominant = "Forest", Generation = 0 }, BeeCaste.Drone);
            var experienced = system.CreateBee(hive, new BeeGenome { Dominant = "Forest", Generation = 3, LineageSeconds = 600 }, BeeCaste.Drone);
            var princess = system.CreateBee(hive, new BeeGenome { Dominant = "Meadow", Generation = 3, LineageSeconds = 600 }, BeeCaste.Princess);
            foreach (var bee in new[] { fresh, experienced, princess }) containers.Insert(bee, apiary.Output);
            Use(princess, hive);
            Use(fresh, hive);
            Use(experienced, hive);
            Assert.That(apiary.Input.ContainedEntities, Is.EquivalentTo(new[] { princess, fresh }), "Only the player's manually chosen pair may occupy parent slots; a stronger drone must not replace it.");
            foreach (var bee in apiary.Input.ContainedEntities.ToArray()) containers.Insert(bee, apiary.Output);

            var quantum = new BeeGenome { Dominant = "Quantum", Recessive = "Quantum" };
            var q1 = system.CreateBee(hive, quantum, BeeCaste.Princess);
            var q2 = system.CreateBee(hive, quantum, BeeCaste.Drone);
            containers.Insert(q1, apiary.Input); containers.Insert(q2, apiary.Input);
            Assert.That(system.Start((hive, apiary)), Is.False, "Late species require an advanced apiary.");
            Assert.That(apiary.Input.ContainedEntities.Count, Is.EqualTo(2));
            apiary.ControlledClimate = true;
            apiary.Minerals = 5;
            Assert.That(system.Start((hive, apiary)), Is.True);
            system.AdvanceActive((hive, apiary), 480);
            Assert.That(apiary.CompletedCycles, Is.EqualTo(2));
            Assert.That(apiary.Output.ContainedEntities.Any(uid => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "IndustrialAdvancedProcessor"), Is.False,
                "A new quantum genome with zero lineage time cannot bypass the rare-item gate.");

            Assert.That(system.Protected(person), Is.False);
            var inventory = SEntMan.System<InventorySystem>();
            foreach (var (id, slot) in new[] { ("EclipseBeekeeperVeil", "head"), ("EclipseBeekeeperSuit", "outerClothing"), ("EclipseBeekeeperGloves", "gloves") })
            {
                var gear = SEntMan.SpawnEntity(id, map.MapCoords);
                Assert.That(inventory.TryEquip(person, gear, slot, force: true), Is.True, id);
            }
            Assert.That(system.Protected(person), Is.True);
        });
        await Client.WaitAssertion(() =>
        {
            using var window = new ApiaryWindow();
            window.Apply(new ApiaryState("apiary-status-working", "Meadow", "test", "test", "test", 0.5f, 100, 100, 5, 10, 1, true));
        });
    }

    [Test]
    public async Task SpeciesTraitsAffectTheHiveAndCopperCombsYieldMaterialsAfterHeating()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var apiaries = Server.System<ApiarySystem>();
            var containers = Server.System<SharedContainerSystem>();
            var hive = SEntMan.SpawnEntity("EclipseApiary", map.GridCoords);
            var colony = SEntMan.GetComponent<ApiaryComponent>(hive);
            colony.Nectar = colony.Water = 100;

            void StartColony(string species)
            {
                var genome = new BeeGenome { Dominant = species, Recessive = species };
                var princess = apiaries.CreateBee(hive, genome, BeeCaste.Princess);
                var drone = apiaries.CreateBee(hive, genome, BeeCaste.Drone);
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(princess).EntityPrototype?.ID,
                    Is.EqualTo($"EclipseBee{species}Princess"));
                Assert.That(containers.Insert(princess, colony.Input), Is.True);
                Assert.That(containers.Insert(drone, colony.Input), Is.True);
                Assert.That(apiaries.Start((hive, colony)), Is.True);
            }

            StartColony("Industrious");
            apiaries.AdvanceActive((hive, colony), 180);
            Assert.That(colony.Nectar, Is.EqualTo(91).Within(0.01));
            var resources = Server.System<IndustrialResourceSystem>();
            Assert.That(resources.Count(colony.Output.ContainedEntities, "EclipseFoodEnergeticHoneycomb"), Is.EqualTo(3));

            colony.Minerals = 1;
            StartColony("Copper");
            apiaries.AdvanceActive((hive, colony), 180);
            Assert.That(colony.Minerals, Is.Zero);
            var combs = colony.Output.ContainedEntities.Where(item => resources.Matches(item, "EclipseCopperHoneycomb")).ToArray();
            Assert.That(combs, Has.Length.EqualTo(2));

            var centrifuge = SEntMan.SpawnEntity("BasicIndustrialThermalCentrifuge", map.GridCoords);
            var machine = SEntMan.GetComponent<IndustrialProcessorComponent>(centrifuge);
            var input = containers.GetContainer(centrifuge, machine.InputContainerId);
            foreach (var comb in combs)
                Assert.That(containers.Insert(comb, input), Is.True);
            Assert.That(machine.IsWorking, Is.False);
            Server.System<TemperatureSystem>().ForceChangeTemperature(centrifuge, 600);
            Assert.That(machine.CurrentRecipe?.Id, Is.EqualTo("EclipseSeparateCopperHoneycomb"));
            Server.System<IndustrialProcessorSystem>().Update(15);
            var output = containers.GetContainer(centrifuge, machine.OutputContainerId);
            Assert.That(input.Count, Is.Zero);
            Assert.That(resources.Count(output.ContainedEntities, "CableApcStack1"), Is.EqualTo(2));
            Assert.That(resources.Count(output.ContainedEntities, "EclipseBeeswax"), Is.EqualTo(1));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
