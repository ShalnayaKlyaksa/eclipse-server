using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Botany;
using Content.Server.Botany.Components;
using Content.Server.Botany.Systems;
using Content.Server.Eclipse.Botany.Systems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server._Eclipse.Beekeeping;
using Content.Server._Eclipse.Industrial;
using Content.Shared._Eclipse.Beekeeping;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Eclipse.Botany.SeedDna.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Kitchen.Components;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Botany;

public sealed class GardenAndBarTest : GameTest
{
    // The client-side static field validator does not load server-only seed prototypes.
    private readonly ProtoId<SeedPrototype> _tomatoSeed = "tomato";
    private readonly ProtoId<SeedPrototype> _chiliSeed = "chili";
    private readonly ProtoId<SeedPrototype> _energyFlowerSeed = "energyFlower";

    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task BreedingIsGradualFertileAndCanDiscoverRareSpeciesWithoutMutagen()
    {
        await Server.WaitAssertion(() =>
        {
            Server.ResolveDependency<IRobustRandom>().SetSeed(5021);
            var mutation = Server.System<MutationSystem>();
            var tomato = SProtoMan.Index(_tomatoSeed).Clone();
            var donor = tomato.Clone();
            donor.Potency = 80;
            for (var i = 0; i < 50; i++)
            {
                var child = mutation.Cross(donor, tomato);
                Assert.That(child.Potency, Is.InRange(27.5f, 30.5f));
                Assert.That(child.Seedless, Is.False);
                Assert.That(child.PowerProduction, Is.Zero);
            }
            Assert.That(tomato.Potency, Is.EqualTo(10));
            Assert.That(donor.Potency, Is.EqualTo(80));
            var exceptional = tomato.Clone();
            exceptional.Potency = 100;
            exceptional.Endurance = 200;
            exceptional.WaterConsumption = 0;
            exceptional.Production = 1;
            exceptional.Lifespan = 100;
            for (var i = 0; i < 50; i++)
            {
                var child = mutation.Cross(exceptional, exceptional);
                Assert.That(child.Potency, Is.EqualTo(100));
                Assert.That(child.Endurance, Is.EqualTo(200));
                Assert.That(child.WaterConsumption, Is.Zero);
                Assert.That(child.Production, Is.EqualTo(1));
                Assert.That(child.Lifespan, Is.EqualTo(100));
            }
            var chili = SProtoMan.Index(_chiliSeed);
            var bombs = 0;
            for (var i = 0; i < 5000; i++)
            {
                var child = mutation.Cross(chili, tomato);
                if (child.PacketPrototype != "BombomatoSeeds") continue;
                bombs++;
                Assert.That(child.Yield, Is.EqualTo(1));
                Assert.That(child.Seedless, Is.False);
            }
            Assert.That(bombs, Is.InRange(1, 80), "Bombomatos must be possible but very rare.");
            var flower = SProtoMan.Index(_energyFlowerSeed);
            Assert.That(mutation.Cross(flower, tomato).PowerProduction, Is.Zero,
                "Species-specific electricity cannot be copied to an ordinary tomato.");
        });
    }

    [Test]
    public async Task EnergyFlowersShareFinitePowerAndStopWhenThePlantDies()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            for (var x = 0; x < 5; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            var bed = SEntMan.SpawnEntity("hydroponicsSoil", new EntityCoordinates(map.Grid.Owner, .5f, .5f));
            var plant = SEntMan.GetComponent<PlantHolderComponent>(bed);
            plant.Seed = SProtoMan.Index(_energyFlowerSeed).Clone();
            plant.Age = 20;
            plant.Health = 100;
            var receivers = new ApcPowerReceiverComponent[3];
            for (var i = 0; i < receivers.Length; i++)
            {
                var machine = SEntMan.SpawnEntity("EclipseApiary", new EntityCoordinates(map.Grid.Owner, i + 1.5f, .5f));
                receivers[i] = SEntMan.GetComponent<ApcPowerReceiverComponent>(machine);
                receivers[i].NeedsPower = true;
                receivers[i].Load = i == 0 ? 50 : 500;
            }
            var system = Server.System<EnergyFlowerSystem>();
            var power = Server.System<PowerNetSystem>();
            system.RefreshSupply();
            Assert.That(receivers.Sum(r => r.BotanicalPower), Is.LessThanOrEqualTo(150));
            Assert.That(power.IsPoweredCalculate(receivers[0]), Is.True);
            Assert.That(power.IsPoweredCalculate(receivers[1]), Is.False);
            Assert.That(receivers[2].BotanicalPower, Is.Zero, "Out of wireless range.");
            plant.Dead = true;
            system.RefreshSupply();
            Assert.That(receivers.Sum(r => r.BotanicalPower), Is.Zero);
            Assert.That(power.IsPoweredCalculate(receivers[0]), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task BeesAddOneFruitWithoutChangingGenesAndProduceOneSpecialCombPerCycle()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            Server.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            var bed = SEntMan.SpawnEntity("hydroponicsSoil", map.GridCoords);
            var plant = SEntMan.GetComponent<PlantHolderComponent>(bed);
            plant.Seed = SProtoMan.Index(_energyFlowerSeed).Clone();
            plant.Age = 20;
            plant.Health = 100;
            var hive = SEntMan.SpawnEntity("EclipseApiary", new EntityCoordinates(map.Grid.Owner, 1.5f, .5f));
            var apiary = SEntMan.GetComponent<ApiaryComponent>(hive);
            apiary.Mother = new BeeGenome();
            apiary.Father = new BeeGenome();
            apiary.Enabled = true;
            apiary.CycleSeconds = 120;
            apiary.Nectar = apiary.Water = 100;
            apiary.Status = "apiary-status-working";
            var pollination = Server.System<GardenPollinationSystem>();
            pollination.Pollinate();
            pollination.Pollinate();
            Assert.That(plant.BeePollinated, Is.True);
            Assert.That(apiary.Nectar, Is.GreaterThan(100));
            var originalYield = plant.Seed.Yield;
            var products = Server.System<BotanySystem>().GenerateProduct(plant.Seed, map.GridCoords, bonusYield: 1).ToArray();
            Assert.That(products.Length, Is.EqualTo(originalYield + 1));
            Assert.That(products.All(p => SEntMan.GetComponent<ProduceComponent>(p).Seed!.Yield == originalYield), Is.True);
            var system = Server.System<ApiarySystem>();
            system.AdvanceActive((hive, apiary), 120);
            Assert.That(apiary.Output.ContainedEntities.Count(e => SEntMan.GetComponent<MetaDataComponent>(e).EntityPrototype!.ID == "GardenChargedHoneycomb"), Is.EqualTo(1));
            var count = apiary.Output.ContainedEntities.Count;
            system.AdvanceActive((hive, apiary), 120);
            Assert.That(apiary.Output.ContainedEntities.Count, Is.EqualTo(count));
            plant.Harvest = true;
            Server.System<PlantHolderSystem>().AutoHarvest(bed, plant);
            Assert.That(plant.BeePollinated, Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task FlowerChoiceAndForageFrameSupplyTheHandPressAndBar()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            EntityCoordinates At(int x) => new(map.Grid.Owner, x + .5f, .5f);
            for (var x = 0; x <= 4; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            for (var x = 0; x < 3; x++)
            {
                var bed = SEntMan.SpawnEntity("hydroponicsSoil", At(x));
                var plant = SEntMan.GetComponent<PlantHolderComponent>(bed);
                plant.Seed = SProtoMan.Index<SeedPrototype>(x == 2 ? "poppy" : "lily").Clone();
                plant.Age = 20;
                plant.Health = 100;
            }

            var hive = SEntMan.SpawnEntity("EclipseApiary", At(3));
            var colony = SEntMan.GetComponent<ApiaryComponent>(hive);
            colony.Mother = new BeeGenome();
            colony.Father = new BeeGenome();
            colony.Enabled = true;
            colony.Status = "apiary-status-working";
            colony.CycleSeconds = 120;
            colony.Nectar = colony.Water = 100;
            colony.FrameKind = "Forage";
            colony.FrameDurability = 1;
            Server.System<GardenPollinationSystem>().Pollinate();
            Assert.That(colony.Nectar, Is.EqualTo(102.4f).Within(0.01f));
            Assert.That(GardenPollinationSystem.PredictComb(colony)?.Id, Is.EqualTo("GardenLilyHoneycomb"));
            Server.System<ApiarySystem>().AdvanceActive((hive, colony), 120);
            var resources = Server.System<IndustrialResourceSystem>();
            var lilies = colony.Output.ContainedEntities.Where(item => resources.Matches(item, "GardenLilyHoneycomb")).ToArray();
            Assert.That(lilies, Has.Length.EqualTo(2), "The last use of the frame must still double the dominant comb.");
            Assert.That(colony.ForageSamples, Is.Empty);
            Assert.That(colony.FrameDurability, Is.Zero);

            var press = SEntMan.SpawnEntity("EclipseManualHoneyPress", At(4));
            var machine = SEntMan.GetComponent<IndustrialProcessorComponent>(press);
            var containers = Server.System<SharedContainerSystem>();
            var input = containers.GetContainer(press, machine.InputContainerId);
            Assert.That(containers.Insert(lilies[0], input), Is.True);
            var processors = Server.System<IndustrialProcessorSystem>();
            processors.Update(30);
            Assert.That(input.Count, Is.EqualTo(1), "The hand press must wait for an operator.");
            var actor = SEntMan.SpawnEntity(null, At(4));
            SEntMan.EnsureComponent<DoAfterComponent>(actor);
            var hands = SEntMan.EnsureComponent<HandsComponent>(actor);
            Server.System<SharedHandsSystem>().AddHand((actor, hands), "right", HandLocation.Right);
            Server.System<TagSystem>().AddTag(actor, new ProtoId<TagPrototype>("InstantDoAfters"));
            var manual = SEntMan.GetComponent<IndustrialManualProcessorComponent>(press);
            Assert.That(processors.StartManual((press, manual), actor), Is.True);
            Assert.That(input.Count, Is.Zero);
            var output = containers.GetContainer(press, machine.OutputContainerId);
            Assert.That(resources.Count(output.ContainedEntities, "EclipseBeeswax"), Is.EqualTo(1));

            var solutions = Server.System<SharedSolutionContainerSystem>();
            var fluid = SEntMan.GetComponent<IndustrialFluidProcessorComponent>(press);
            Assert.That(solutions.TryGetSolution(press, fluid.OutputSolution, out _, out var liquid), Is.True);
            Assert.That(liquid!.GetTotalPrototypeQuantity("EclipseLilyHoney"), Is.EqualTo(FixedPoint2.New(8)));
            var glass = SEntMan.SpawnEntity("DrinkGlass", At(4));
            Assert.That(solutions.TryGetSolution(glass, "drink", out var drink, out var mixed), Is.True);
            Assert.That(solutions.TryTransferSolution(drink!.Value, liquid!, FixedPoint2.New(1)), Is.True);
            solutions.TryAddReagent(drink.Value, "JuiceLemon", FixedPoint2.New(1));
            solutions.TryAddReagent(drink.Value, "SodaWater", FixedPoint2.New(2));
            var spoon = SEntMan.SpawnEntity("BarSpoon", At(4));
            Server.System<ReactionMixerSystem>().TryMix(spoon, glass);
            Assert.That(mixed!.GetTotalPrototypeQuantity("LilyHoneyLemonade"), Is.EqualTo(FixedPoint2.New(4)));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task BarBatchesRequireTheRightToolAndGarnishDoesNotAcceptGeneDisks()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            // Validation needs the entire inheritance tree, including BaseDrink and BaseItem.
            var errors = SProtoMan.ValidateDirectory(new ResPath("/Prototypes"), out _);
            var gardenErrors = errors.Where(entry => entry.Key.Replace('\\', '/').Contains("/Eclipse/Botany/"))
                .SelectMany(entry => entry.Value.Select(error => $"{entry.Key}: {error.ErrorReason}"));
            Assert.That(gardenErrors, Is.Empty, "New plant, bar and hybrid prototypes must validate.");
            var glass = SEntMan.SpawnEntity("DrinkGlass", map.GridCoords);
            var muddler = SEntMan.SpawnEntity("BarMuddler", map.GridCoords);
            var spoon = SEntMan.SpawnEntity("BarSpoon", map.GridCoords);
            var solutions = Server.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(glass, "drink", out var solution, out var liquid), Is.True);
            solutions.TryAddReagent(solution.Value, "JuiceBerry", FixedPoint2.New(10));
            solutions.TryAddReagent(solution.Value, "Sugar", FixedPoint2.New(5));
            solutions.TryAddReagent(solution.Value, "Water", FixedPoint2.New(5));
            var mixer = Server.System<ReactionMixerSystem>();
            mixer.TryMix(spoon, glass);
            Assert.That(liquid.GetTotalPrototypeQuantity("GardenBerrySyrup"), Is.EqualTo(FixedPoint2.Zero));
            mixer.TryMix(muddler, glass);
            Assert.That(liquid.GetTotalPrototypeQuantity("GardenBerrySyrup"), Is.EqualTo(FixedPoint2.New(20)));
            var infuser = SEntMan.SpawnEntity("BarInfuser", map.GridCoords);
            solutions.TryGetSolution(infuser, "drink", out var infusion, out var infusedLiquid);
            solutions.TryAddReagent(infusion.Value, "GardenMintExtract", FixedPoint2.New(10));
            solutions.TryAddReagent(infusion.Value, "Sugar", FixedPoint2.New(10));
            solutions.TryAddReagent(infusion.Value, "Water", FixedPoint2.New(10));
            mixer.TryMix(infuser, infuser);
            Assert.That(infusedLiquid.GetTotalPrototypeQuantity("GardenMintCordial"), Is.EqualTo(FixedPoint2.New(30)));
            var melonSlice = SEntMan.SpawnEntity("FoodDemonicWatermelonSlice", map.GridCoords);
            var extract = SEntMan.GetComponent<ExtractableComponent>(melonSlice);
            Assert.That(extract.JuiceSolution!.GetTotalPrototypeQuantity("InfernalJuice"), Is.EqualTo(FixedPoint2.New(4)));
            var slots = Server.System<ItemSlotsSystem>();
            var mint = SEntMan.SpawnEntity("FoodGardenMint", map.GridCoords);
            Assert.That(slots.TryInsert(glass, "garnish", mint, null), Is.True);
            var analyzer = SEntMan.SpawnEntity("ComputerBotanicalSeedDnaManipulator", map.GridCoords);
            var disk = SEntMan.SpawnEntity("BotanicalSeedDnaDisk", map.GridCoords);
            Assert.That(slots.TryInsert(analyzer, SeedDnaConsoleComponent.DnaDiskSlotId, disk, null), Is.False);
            Assert.That(slots.TryInsert(glass, "garnish", disk, null), Is.False);
            var seeds = SEntMan.SpawnEntity("TomatoSeeds", map.GridCoords);
            Assert.That(slots.TryInsert(analyzer, SeedDnaConsoleComponent.SeedSlotId, seeds, null), Is.True);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
