using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Industrial;
using Content.Server.Stack;
using Content.Server.Temperature.Systems;
using Content.Shared._Eclipse.Industrial;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class IndustrialAdvancedProductionTest : GameTest
{
    private static readonly ProtoId<IndustrialRecipePrototype> CrushQuartzOre = "CrushQuartzOre";

    // This fixture exercises material/solution transactions; licensing and room failures are tested separately.
    private void PrepareProductionEnvironment(EntityUid uid)
    {
        var knowledge = SEntMan.EnsureComponent<IndustrialMachineKnowledgeComponent>(uid);
        foreach (var recipe in Server.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<IndustrialRecipePrototype>())
            if (recipe.RequiredTechnology is { } technology) knowledge.Technologies.Add(technology);
        SEntMan.EnsureComponent<RoomSanitationComponent>(uid).Grade = 2;
    }

    [Test]
    public async Task ThirtyFiveOperationsProduceAUsableProcessorWithoutInventingIntermediateInputs()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            var containers = Server.System<SharedContainerSystem>();
            var solutions = Server.System<SharedSolutionContainerSystem>();
            var processor = Server.System<IndustrialProcessorSystem>();
            var stocks = new Dictionary<string, int>
            {
                ["SpaceQuartz"] = 1, ["QuartzDust"] = 2, ["SilverDust"] = 1, ["PlasmaDust"] = 2,
                ["IronDust"] = 1, ["CopperDust"] = 1, ["IngotGold"] = 1, ["SheetSteel"] = 1, ["SheetCopper"] = 2,
            };
            var fluids = new Dictionary<string, FixedPoint2>
            {
                ["Water"] = FixedPoint2.New(200), ["SulfuricAcid"] = FixedPoint2.New(25),
                ["Hydrogen"] = FixedPoint2.New(30), ["Chlorine"] = FixedPoint2.New(10),
                ["Oxygen"] = FixedPoint2.New(5), ["Ethanol"] = FixedPoint2.New(20),
                ["Acetone"] = FixedPoint2.New(20), ["SodiumHydroxide"] = FixedPoint2.New(5),
            };
            var machineTypes = new HashSet<IndustrialProcessorType>();
            void Run(IndustrialRecipePrototype recipe)
            {
                var machineId = recipe.Processor switch
                {
                    IndustrialProcessorType.ThermalCentrifuge => "BasicIndustrialThermalCentrifuge",
                    _ => "Industrial" + recipe.Processor,
                };
                var uid = SEntMan.SpawnEntity(machineId, map.GridCoords);
                var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
            PrepareProductionEnvironment(uid);
                comp.AutoStart = false;
                comp.Enabled = false;
                comp.CanWorkWithoutPower = true;
                comp.SelectedRecipe = new ProtoId<IndustrialRecipePrototype>(recipe.ID);
                var input = containers.GetContainer(uid, comp.InputContainerId);
                foreach (var (id, amount) in recipe.Inputs)
                {
                    Assert.That(stocks.GetValueOrDefault(id), Is.GreaterThanOrEqualTo(amount), recipe.ID + ": " + id);
                    stocks[id] -= amount;
                    var item = SEntMan.SpawnEntity(id, map.GridCoords);
                    if (SEntMan.TryGetComponent<StackComponent>(item, out var stack))
                        Server.System<StackSystem>().SetCount((item, stack), amount);
                    else
                        Assert.That(amount, Is.EqualTo(1));
                    Assert.That(containers.Insert(item, input), Is.True, recipe.ID);
                }
                if (recipe.FluidInputs.Count > 0 || recipe.Bath.Count > 0)
                {
                    var fluid = SEntMan.GetComponent<IndustrialFluidProcessorComponent>(uid);
                    Assert.That(solutions.TryGetSolution(uid, fluid.InputSolution, out var tank, out _));
                    foreach (var reagent in recipe.FluidInputs.Keys.Union(recipe.Bath.Keys))
                    {
                        var amount = recipe.FluidInputs.GetValueOrDefault(reagent) + recipe.Bath.GetValueOrDefault(reagent);
                        Assert.That(fluids.GetValueOrDefault(reagent.Id), Is.GreaterThanOrEqualTo(amount), recipe.ID + ": " + reagent);
                        fluids[reagent.Id] -= amount;
                        Assert.That(solutions.TryAddReagent(tank.Value, reagent.Id, amount), Is.True);
                    }
                }
                if (recipe.Heat > 0)
                    Server.System<TemperatureSystem>().ForceChangeTemperature(uid, 800);
                comp.AutoStart = true;
                SEntMan.EventBus.RaiseLocalEvent(uid, new IndustrialProcessorToggleMessage { Actor = uid });
                Assert.That(comp.IsWorking, Is.True, recipe.ID + " must start with its actual ingredients.");
                processor.Update(120);
                var output = containers.GetContainer(uid, comp.OutputContainerId);
                foreach (var (id, amount) in recipe.Outputs)
                {
                    Assert.That(Server.System<IndustrialResourceSystem>().Count(output.ContainedEntities, id), Is.EqualTo(amount), recipe.ID);
                    stocks[id] = stocks.GetValueOrDefault(id) + amount;
                }
                Assert.That(input.Count, Is.Zero, recipe.ID + " must consume its solid inputs.");
                if (SEntMan.TryGetComponent<IndustrialFluidProcessorComponent>(uid, out var fluidComp))
                {
                    solutions.TryGetSolution(uid, fluidComp.InputSolution, out _, out var remaining);
                    solutions.TryGetSolution(uid, fluidComp.OutputSolution, out _, out var products);
                    foreach (var (id, amount) in recipe.FluidOutputs)
                        Assert.That(products!.GetTotalPrototypeQuantity(id), Is.EqualTo(amount), recipe.ID);
                    Assert.That(remaining!.Volume, Is.EqualTo(recipe.Bath.Values.Aggregate(FixedPoint2.Zero, (a, b) => a + b)), recipe.ID);
                    foreach (var quantity in remaining.Contents.Concat(products!.Contents))
                        fluids[quantity.Reagent.Prototype] = fluids.GetValueOrDefault(quantity.Reagent.Prototype) + quantity.Quantity;
                }
                machineTypes.Add(recipe.Processor);
                SEntMan.DeleteEntity(uid);
            }
            var sideChains = new[] { "MakeChlorinatingAgent", "MakeCrystalMedium", "MakeOxidizer", "MakeDopant", "MakeSolvent", "MakeSolvent",
                "MakePhotoresist", "MakeDeveloper", "MakeEtchant", "MakeCopperElectrolyte", "MakeEncapsulant",
                "DrawBondingWire", "StampChipFrames", "CutHeatSpreaders", "FormContacts" };
            foreach (var name in sideChains)
                Run(prototypes.Index<IndustrialRecipePrototype>("Industrial" + name));
            Run(prototypes.Index(CrushQuartzOre));
            var main = prototypes.EnumeratePrototypes<IndustrialRecipePrototype>()
                .Where(r => r.ID.StartsWith("Advanced")).OrderBy(r => r.ID).ToArray();
            Assert.That(main, Has.Length.EqualTo(34));
            foreach (var recipe in main)
                Run(recipe);
            Assert.That(stocks["IndustrialAdvancedProcessor"], Is.EqualTo(1));
            Assert.That(fluids["IndustrialCrystalMedium"], Is.EqualTo(FixedPoint2.New(25)), "The crystal bath must survive processing.");
            Assert.That(machineTypes.Count, Is.GreaterThanOrEqualTo(18), "Stages must use distinct processing roles.");
            Assert.That(prototypes.EnumeratePrototypes<IndustrialRecipePrototype>()
                .Any(r => !r.ID.StartsWith("Advanced") && r.Inputs.ContainsKey("IndustrialAdvancedProcessor")), Is.True,
                "The final processor must have equipment recipes, not just be a trophy.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AFullFluidOutputBlocksWithoutSpendingInputsAndResumesWhenDrained(bool fillAfterStart)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("IndustrialOreWasher", map.GridCoords);
            var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
            PrepareProductionEnvironment(uid);
            comp.CanWorkWithoutPower = true;
            comp.SelectedRecipe = new ProtoId<IndustrialRecipePrototype>("Advanced02WashQuartz");
            var solutions = Server.System<SharedSolutionContainerSystem>();
            var fluid = SEntMan.GetComponent<IndustrialFluidProcessorComponent>(uid);
            solutions.TryGetSolution(uid, fluid.InputSolution, out var source, out var inputFluid);
            solutions.TryGetSolution(uid, fluid.OutputSolution, out var target, out var outputFluid);
            solutions.TryAddReagent(source.Value, "Water", FixedPoint2.New(10));
            if (!fillAfterStart)
                solutions.TryAddReagent(target.Value, "IndustrialDirtyWater", FixedPoint2.New(250));
            var containers = Server.System<SharedContainerSystem>();
            var ore = SEntMan.SpawnEntity("CrushedQuartz1", map.GridCoords);
            Server.System<StackSystem>().SetCount((ore, SEntMan.GetComponent<StackComponent>(ore)), 2);
            var input = containers.GetContainer(uid, comp.InputContainerId);
            Assert.That(containers.Insert(ore, input));
            if (fillAfterStart)
            {
                Assert.That(comp.IsWorking);
                solutions.TryAddReagent(target.Value, "IndustrialDirtyWater", FixedPoint2.New(250));
            }
            var processors = Server.System<IndustrialProcessorSystem>();
            processors.Update(30);
            Assert.That(input.Contains(ore));
            Assert.That(inputFluid!.Volume, Is.EqualTo(FixedPoint2.New(10)));
            solutions.SplitSolution(target.Value, FixedPoint2.New(10));
            processors.Update(30);
            Assert.That(input.Count, Is.Zero);
            Assert.That(inputFluid.Volume, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(outputFluid!.Volume, Is.EqualTo(FixedPoint2.New(250)));
            Assert.That(Server.System<IndustrialResourceSystem>().Count(containers.GetContainer(uid, comp.OutputContainerId).ContainedEntities,
                "IndustrialWashedQuartz"), Is.EqualTo(2));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task LiquidMachineEndpointsConserveMixturesRespectDirectionAndRefreshConnectors()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            for (var x = 0; x <= 2; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            var source = SEntMan.SpawnEntity("IndustrialFluidReservoir", map.GridCoords);
            var pipe = SEntMan.SpawnEntity("PerfectLiquidPipe", new EntityCoordinates(map.Grid, 1.5f, .5f));
            var target = SEntMan.SpawnEntity("IndustrialFluidReservoir", new EntityCoordinates(map.Grid, 2.5f, .5f));
            var sourceComp = SEntMan.GetComponent<IndustrialProcessorComponent>(source);
            var targetComp = SEntMan.GetComponent<IndustrialProcessorComponent>(target);
            sourceComp.SetFacePort(Direction.East, FacePortState.LiquidOutput);
            targetComp.SetFacePort(Direction.West, FacePortState.LiquidInput);
            var network = Server.System<LiquidPipeNetworkSystem>();
            network.InvalidateProcessorConnections(source);
            network.InvalidateProcessorConnections(target);
            network.Update(0);
            var pipeComp = SEntMan.GetComponent<LiquidPipeComponent>(pipe);
            Assert.That(pipeComp.MachineOutputDirections, Is.EqualTo(PipeDirection.West));
            Assert.That(pipeComp.MachineInputDirections, Is.EqualTo(PipeDirection.East));
            var solutions = Server.System<SharedSolutionContainerSystem>();
            solutions.TryGetSolution(source, "industrial_fluid_input", out var sourceTank, out var first);
            solutions.TryGetSolution(target, "industrial_fluid_input", out _, out var last);
            solutions.TryGetSolution(pipe, LiquidPipeComponent.SolutionName, out _, out var middle);
            solutions.TryAddReagent(sourceTank.Value, "Water", FixedPoint2.New(30));
            solutions.TryAddReagent(sourceTank.Value, "Ethanol", FixedPoint2.New(10));
            for (var i = 0; i < 80; i++)
                network.Update(.25f);
            Assert.That(last!.Volume, Is.GreaterThan(FixedPoint2.Zero));
            foreach (var (id, amount) in new[] { ("Water", 30), ("Ethanol", 10) })
                Assert.That(first!.GetTotalPrototypeQuantity(id) + middle!.GetTotalPrototypeQuantity(id) + last.GetTotalPrototypeQuantity(id),
                    Is.EqualTo(FixedPoint2.New(amount)), "Pumping must preserve each reagent.");
            targetComp.SetFacePort(Direction.West, FacePortState.Disabled);
            network.InvalidateProcessorConnections(target);
            network.Update(0);
            Assert.That(pipeComp.MachineInputDirections, Is.EqualTo(PipeDirection.None));
            var before = last.Volume;
            for (var i = 0; i < 8; i++)
                network.Update(.25f);
            Assert.That(last.Volume, Is.EqualTo(before));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task SeparateFluidFeedsRunARealReactorAndDrainItsLiquidProduct()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            for (var x = -2; x <= 0; x++)
                for (var y = -2; y <= 2; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            EntityUid Spawn(string id, int x, int y) => SEntMan.SpawnEntity(id, new EntityCoordinates(map.Grid, x + .5f, y + .5f));
            var reactor = Spawn("IndustrialChemicalReactor", 0, 0);
            var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(reactor);
            PrepareProductionEnvironment(reactor);
            comp.CanWorkWithoutPower = true;
            comp.SelectedRecipe = new ProtoId<IndustrialRecipePrototype>("Advanced06LeachSilica");
            comp.SetFacePort(Direction.West, FacePortState.LiquidInput);
            var west = Spawn("IndustrialFluidReservoir", -2, 0);
            var north = Spawn("IndustrialFluidReservoir", 0, 2);
            var south = Spawn("IndustrialFluidReservoir", 0, -2);
            SEntMan.GetComponent<IndustrialProcessorComponent>(west).SetFacePort(Direction.East, FacePortState.LiquidOutput);
            SEntMan.GetComponent<IndustrialProcessorComponent>(north).SetFacePort(Direction.South, FacePortState.LiquidOutput);
            SEntMan.GetComponent<IndustrialProcessorComponent>(south).SetFacePort(Direction.North, FacePortState.LiquidInput);
            var pipes = new[] { Spawn("PerfectLiquidPipe", -1, 0), Spawn("PerfectLiquidPipe", 0, 1), Spawn("PerfectLiquidPipe", 0, -1) };
            var solutions = Server.System<SharedSolutionContainerSystem>();
            solutions.TryGetSolution(west, "industrial_fluid_input", out var feedA, out _);
            solutions.TryGetSolution(north, "industrial_fluid_input", out var feedB, out _);
            solutions.TryGetSolution(south, "industrial_fluid_input", out _, out var product);
            solutions.TryAddReagent(feedA.Value, "IndustrialQuartzSlurry", FixedPoint2.New(20));
            solutions.TryAddReagent(feedB.Value, "SulfuricAcid", FixedPoint2.New(10));
            var network = Server.System<LiquidPipeNetworkSystem>();
            foreach (var machine in new[] { reactor, west, north, south })
                network.InvalidateProcessorConnections(machine);
            var processors = Server.System<IndustrialProcessorSystem>();
            for (var i = 0; i < 400; i++)
            {
                network.Update(.25f);
                processors.Update(.25f);
            }
            Assert.That(product!.GetTotalPrototypeQuantity("IndustrialSilicaLeachate"), Is.EqualTo(FixedPoint2.New(25)));
            Assert.That(product.Volume, Is.EqualTo(FixedPoint2.New(25)));
            foreach (var pipe in pipes)
            {
                solutions.TryGetSolution(pipe, LiquidPipeComponent.SolutionName, out _, out var contents);
                Assert.That(contents!.Volume, Is.EqualTo(FixedPoint2.Zero));
            }
            solutions.TryGetSolution(reactor, "industrial_fluid_input", out _, out var remaining);
            Assert.That(remaining!.Volume, Is.EqualTo(FixedPoint2.Zero));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task RemovingTheBathDuringAProcessCancelsWithoutConsumingTheSolidAndAllowsRestart()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("IndustrialCrystallizer", map.GridCoords);
            var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
            PrepareProductionEnvironment(uid);
            comp.CanWorkWithoutPower = true;
            comp.SelectedRecipe = new ProtoId<IndustrialRecipePrototype>("Advanced15GrowCrystal");
            var containers = Server.System<SharedContainerSystem>();
            var input = containers.GetContainer(uid, comp.InputContainerId);
            var output = containers.GetContainer(uid, comp.OutputContainerId);
            var silicon = SEntMan.SpawnEntity("IndustrialZoneSilicon", map.GridCoords);
            Server.System<StackSystem>().SetCount((silicon, SEntMan.GetComponent<StackComponent>(silicon)), 2);
            containers.Insert(silicon, input);
            var solutions = Server.System<SharedSolutionContainerSystem>();
            solutions.TryGetSolution(uid, "industrial_fluid_input", out var tank, out var bath);
            solutions.TryAddReagent(tank.Value, "IndustrialCrystalMedium", FixedPoint2.New(25));
            Assert.That(comp.IsWorking);
            var processor = Server.System<IndustrialProcessorSystem>();
            processor.Update(1);
            var removed = solutions.SplitSolution(tank.Value, FixedPoint2.New(25));
            Assert.That(comp.IsWorking, Is.False, "The cycle must stop as soon as its required bath is removed.");
            processor.Update(30);
            Assert.That(comp.IsWorking, Is.False);
            Assert.That(input.Contains(silicon));
            Assert.That(output.Count, Is.Zero);
            solutions.TryAddSolution(tank.Value, removed);
            Assert.That(comp.IsWorking);
            processor.Update(30);
            Assert.That(input.Count, Is.Zero);
            Assert.That(Server.System<IndustrialResourceSystem>().Count(output.ContainedEntities, "IndustrialSiliconBoule"), Is.EqualTo(1));
            Assert.That(bath!.GetTotalPrototypeQuantity("IndustrialCrystalMedium"), Is.EqualTo(FixedPoint2.New(25)));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task RemovingAMachineClearsItsItemCouplingsImmediately()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(-1, 0), map.Tile.Tile);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            var machine = SEntMan.SpawnEntity("BasicIndustrialCrusher", map.GridCoords);
            var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(machine);
            PrepareProductionEnvironment(machine);
            comp.SetFacePort(Direction.West, FacePortState.ItemInput);
            comp.SetFacePort(Direction.East, FacePortState.ItemOutput);
            var left = SEntMan.SpawnEntity("LpItemPipe", new EntityCoordinates(map.Grid, -.5f, .5f));
            var right = SEntMan.SpawnEntity("LpItemPipe", new EntityCoordinates(map.Grid, 1.5f, .5f));
            var pipes = Server.System<ItemPipeSystem>();
            var a = SEntMan.GetComponent<ItemPipeComponent>(left);
            var b = SEntMan.GetComponent<ItemPipeComponent>(right);
            pipes.UpdateConnections((left, a));
            pipes.UpdateConnections((right, b));
            Assert.That(a.MachineInputDirections, Is.EqualTo(PipeDirection.East));
            Assert.That(b.MachineOutputDirections, Is.EqualTo(PipeDirection.West));
            SEntMan.DeleteEntity(machine);
            Assert.That(a.MachineInputDirections | a.ConnectedDirections | b.MachineOutputDirections | b.ConnectedDirections,
                Is.EqualTo(PipeDirection.None), "Removed machines must not leave sockets or dangling arms.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
