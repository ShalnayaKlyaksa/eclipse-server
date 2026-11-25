using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Power;
using Content.Server.Materials;
using Content.Server._Eclipse.Industrial;
using Content.Server.Power.Generator;
using Content.Server.Power.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._Eclipse.Industrial;
using Content.Shared._Eclipse.Power;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Power.Generator;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class FuelPlantTest : GameTest
{
    [Test]
    public async Task FusionUsesAuxiliaryPowerAndTripsAfterCoolantFlowStops()
    {
        var map = await Pair.CreateTestMap();
        EntityUid reactor = default;
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            for (var x = 0; x < 2; x++)
            {
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                SEntMan.SpawnEntity("CableMV", new EntityCoordinates(map.Grid.Owner, x + .5f, .5f));
            }

            var reserve = SEntMan.SpawnEntity("SubstationBasic", new EntityCoordinates(map.Grid.Owner, .5f, .5f));
            var battery = SEntMan.GetComponent<PowerNetworkBatteryComponent>(reserve);
            battery.SupplyRampRate = battery.SupplyRampTolerance = 1000000;
            reactor = SEntMan.SpawnEntity("IndustrialFusionReactor", new EntityCoordinates(map.Grid.Owner, 1.5f, .5f));
            Server.System<SharedTransformSystem>().AnchorEntity(reactor);
            Assert.That(Server.System<MaterialStorageSystem>().TryChangeMaterialAmount(reactor, "EclipseFusionFuel", 100));
        });
        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() =>
        {
            var generators = Server.System<GeneratorSystem>();
            var plants = Server.System<PowerPlantSystem>();
            var plant = SEntMan.GetComponent<PowerPlantComponent>(reactor);
            var supplier = SEntMan.GetComponent<PowerSupplierComponent>(reactor);
            var auxiliary = SEntMan.GetComponent<PowerConsumerComponent>(reactor);
            var nodes = Server.System<NodeContainerSystem>();
            Assert.That(nodes.TryGetNode(reactor, "coolant_in", out PipeNode? inlet));
            Assert.That(nodes.TryGetNode(reactor, "coolant_out", out PipeNode? outlet));
            Assert.That(auxiliary.ReceivedPower, Is.EqualTo(15000).Within(1));

            void Step(float inletMoles)
            {
                // The bench supplies cold gas and empties the return between steps.
                inlet!.Air.Clear();
                outlet!.Air.Clear();
                inlet.Air.Temperature = outlet.Air.Temperature = 293.15f;
                inlet.Air.SetMoles(Gas.CarbonDioxide, inletMoles);
                plants.Update(.5f);
                generators.Update(.5f);
            }

            generators.SetFuelGeneratorOn(reactor, true);
            Step(0);
            Assert.That(generators.IsRunning(reactor), Is.False);
            Assert.That(plant.Condition, Is.EqualTo(PowerPlantCondition.NoCoolant));

            Step(120);
            generators.SetFuelGeneratorOn(reactor, true);
            Step(120);
            Assert.That(supplier.MaxSupply, Is.Zero);
            Assert.That(generators.GetFuel(reactor), Is.EqualTo(1));
            for (var i = 0; i < 24; i++) Step(120);
            Assert.That(supplier.MaxSupply, Is.EqualTo(140000).Within(1));
            Assert.That(generators.GetFuel(reactor), Is.LessThan(1));

            SEntMan.EventBus.RaiseLocalEvent(reactor, new PortableGeneratorSetTargetPowerMessage(240));
            Step(120);
            Assert.That(supplier.MaxSupply, Is.EqualTo(240000).Within(1));
            for (var i = 0; i < 6; i++) Step(24);
            Assert.That(supplier.MaxSupply, Is.InRange(140000, 239000));
            Assert.That(plant.Condition, Is.EqualTo(PowerPlantCondition.Limited));

            for (var i = 0; i < 8; i++) Step(0);
            Assert.That(plant.Tripped, Is.True);
            Assert.That(plant.Condition, Is.EqualTo(PowerPlantCondition.NoCoolant));
            Assert.That(supplier.Enabled, Is.False);
            Assert.That(generators.IsRunning(reactor), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [TestCase("IndustrialCoalGenerator")]
    [TestCase("IndustrialLiquidFuelGenerator")]
    [TestCase("IndustrialGasGenerator")]
    [TestCase("IndustrialFuelCellGenerator")]
    [TestCase("IndustrialGeothermalGenerator")]
    [TestCase("IndustrialMicroBlackHoleGenerator")]
    public async Task CoolingAndFuelPortsDoNotRemoveTheConfiguredElectricalOutputs(string prototype)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity(prototype, map.GridCoords);
            var nodes = SEntMan.GetComponent<Content.Shared.NodeContainer.NodeContainerComponent>(uid).Nodes;
            Assert.That(nodes.ContainsKey("output_hv"), Is.EqualTo(prototype != "IndustrialMicroBlackHoleGenerator"), prototype);
            Assert.That(nodes.ContainsKey("output_mv"), Is.EqualTo(prototype != "IndustrialMicroBlackHoleGenerator"), prototype);
            Assert.That(nodes.ContainsKey("coolant_in"), Is.True, prototype);
            Assert.That(nodes.ContainsKey("coolant_out"), Is.True, prototype);
            if (prototype == "IndustrialGasGenerator")
            {
                Assert.That(nodes.ContainsKey("fuel"), Is.True);
                Assert.That(nodes.ContainsKey("exhaust"), Is.True);
            }
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task ChargedAuxiliaryBusCanStartAndSustainTheMassConverterWithFourRealFieldLoads()
    {
        try { await CheckMassConverterStartup(); }
        catch (System.Exception error)
        {
            TestContext.Error.WriteLine(error);
            Assert.Fail(error.ToString());
        }
    }

    private async Task CheckMassConverterStartup()
    {
        var map = await Pair.CreateTestMap();
        EntityUid generator = default, source = default;
        var ticksPerSecond = 1;
        await Server.WaitAssertion(() =>
        {
            ticksPerSecond = Server.ResolveDependency<Robust.Shared.Timing.IGameTiming>().TickRate;
            var maps = Server.System<SharedMapSystem>();
            for (var x = 0; x < 11; x++)
            for (var y = 0; y < 11; y++)
            {
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                var at = new EntityCoordinates(map.Grid.Owner, x + .5f, y + .5f);
                SEntMan.SpawnEntity("CableHV", at);
                SEntMan.SpawnEntity("CableMV", at);
            }
            // Two separate voltage networks on a connected platform, joined only by the substation.
            source = SEntMan.SpawnEntity("SubstationBasic", new EntityCoordinates(map.Grid.Owner, .5f, .5f));
            var reserve = SEntMan.GetComponent<PowerNetworkBatteryComponent>(source);
            reserve.MaxChargeRate = 150000;
            // Boundary condition: the auxiliary converter has already been brought up to operating current.
            reserve.SupplyRampRate = 100000000;
            reserve.SupplyRampTolerance = 100000000;
            generator = SEntMan.SpawnEntity("IndustrialMicroBlackHoleGenerator", new EntityCoordinates(map.Grid.Owner, 5.5f, 5.5f));
            var transforms = Server.System<SharedTransformSystem>();
            transforms.AnchorEntity(generator);
            Server.System<Content.Server.Materials.MaterialStorageSystem>().TryChangeMaterialAmount(generator, "Steel", 1000);
            foreach (var offset in new[] { new Vector2i(-3, 0), new Vector2i(3, 0), new Vector2i(0, -3), new Vector2i(0, 3) })
            {
                var field = SEntMan.SpawnEntity("PowerPlantFieldStabilizer", new EntityCoordinates(map.Grid.Owner, 5.5f + offset.X, 5.5f + offset.Y));
                transforms.AnchorEntity(field);
            }
            foreach (var offset in new[] { new Vector2i(-3, -3), new Vector2i(3, -3), new Vector2i(-3, 3), new Vector2i(3, 3) })
            {
                var collector = SEntMan.SpawnEntity("GravitationalKineticGenerator", new EntityCoordinates(map.Grid.Owner, 5.5f + offset.X, 5.5f + offset.Y));
                transforms.AnchorEntity(collector);
            }
        });
        await Pair.RunTicksSync(ticksPerSecond * 2);
        float fuelBefore = 0;
        await Server.WaitAssertion(() =>
        {
            var plant = SEntMan.GetComponent<PowerPlantComponent>(generator);
            Assert.That(plant.FieldCount, Is.EqualTo(4));
            var generators = Server.System<GeneratorSystem>();
            fuelBefore = generators.GetFuel(generator);
            generators.SetFuelGeneratorOn(generator, true);
        });
        double rejectedHeat = 0;
        float settledTemperature = 0;
        // Ten simulated minutes include thermal settling, not only the electrical startup ramp.
        for (var i = 0; i < 1200; i++)
        {
            await Server.WaitAssertion(() =>
            {
                var nodes = Server.System<NodeContainerSystem>();
                Assert.That(nodes.TryGetNode(generator, "coolant_in", out PipeNode? inlet));
                Assert.That(nodes.TryGetNode(generator, "coolant_out", out PipeNode? outlet));
                Assert.That(ReferenceEquals(inlet!.Air, outlet!.Air), Is.False);
                var atmos = Server.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>();
                // An external pumped heat sink is the test boundary, not a source of electrical energy.
                // Restore its finite CO2 inventory; account for the heat removed from the returned gas.
                rejectedHeat += atmos.GetHeatCapacity(inlet.Air, true) * (inlet.Air.Temperature - 293.15f) +
                    atmos.GetHeatCapacity(outlet.Air, true) * (outlet.Air.Temperature - 293.15f);
                inlet.Air.Clear();
                outlet.Air.Clear();
                inlet.Air.Temperature = outlet.Air.Temperature = 293.15f;
                inlet.Air.SetMoles(Gas.CarbonDioxide, 3000);
            });
            await Pair.RunTicksSync(System.Math.Max(1, ticksPerSecond / 2));
            if (i == 1140) await Server.WaitAssertion(() =>
            {
                settledTemperature = SEntMan.GetComponent<PowerPlantComponent>(generator).Temperature;
            });
        }
        await Server.WaitAssertion(() =>
        {
            var plant = SEntMan.GetComponent<PowerPlantComponent>(generator);
            Assert.That(Server.System<GeneratorSystem>().IsRunning(generator), Is.True);
            Assert.That(plant.Tripped, Is.False);
            Assert.That(plant.FieldCount, Is.EqualTo(4));
            Assert.That(plant.Temperature, Is.LessThan(plant.ShutdownTemperature));
            Assert.That(plant.Warmup, Is.EqualTo(1));
            Assert.That(plant.Temperature, Is.EqualTo(settledTemperature).Within(2), "Temperature must settle after warmup, not just remain below the trip threshold for a short test.");
            Assert.That(rejectedHeat, Is.GreaterThan(0));
            Assert.That(Server.System<GeneratorSystem>().GetFuel(generator), Is.LessThan(fuelBefore));
            Assert.That(SEntMan.GetComponent<PowerSupplierComponent>(generator).MaxSupply, Is.Zero);
            var gravity = SEntMan.GetComponent<GravityPowerSourceComponent>(generator);
            Assert.That(gravity.Collectors, Is.EqualTo(4));
            Assert.That(gravity.HarvestedPower, Is.GreaterThan(1000000).And.LessThanOrEqualTo(gravity.AvailablePower + 1));
            Assert.That(Server.System<Content.Server.Power.EntitySystems.BatterySystem>().GetCharge(source), Is.GreaterThan(0));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task FieldStabilizerDrawsItsThirtyKilowattsDirectlyFromTheMediumVoltageNetwork()
    {
        var map = await Pair.CreateTestMap();
        EntityUid stabilizer = default, source = default;
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            for (var x = 0; x < 2; x++)
            {
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                SEntMan.SpawnEntity("CableMV", new EntityCoordinates(map.Grid.Owner, x + .5f, .5f));
            }
            source = SEntMan.SpawnEntity("SubstationBasic", new EntityCoordinates(map.Grid.Owner, .5f, .5f));
            var battery = SEntMan.GetComponent<PowerNetworkBatteryComponent>(source);
            battery.SupplyRampRate = 100000000;
            battery.SupplyRampTolerance = 100000000;
            stabilizer = SEntMan.SpawnEntity("PowerPlantFieldStabilizer", new EntityCoordinates(map.Grid.Owner, 1.5f, .5f));
            Server.System<SharedTransformSystem>().AnchorEntity(stabilizer);
            Assert.That(SEntMan.HasComponent<ApcPowerReceiverComponent>(stabilizer), Is.False);
        });
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<PowerConsumerComponent>(stabilizer).ReceivedPower, Is.EqualTo(30000).Within(1));
            SEntMan.GetComponent<PowerNetworkBatteryComponent>(source).MaxSupply = 0;
        });
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<PowerConsumerComponent>(stabilizer).ReceivedPower, Is.Zero);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task FuelCellPreservesPaidFractionsAndStopsWhenReactantsAreGone()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("IndustrialFuelCellGenerator", map.GridCoords);
            var solutions = Server.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(uid, "tank", out var tank, out var input));
            Assert.That(solutions.TryGetSolution(uid, "output", out _, out var output));
            solutions.TryAddReagent(tank!.Value, "Hydrogen", FixedPoint2.New(2));
            solutions.TryAddReagent(tank.Value, "Oxygen", FixedPoint2.New(1));
            var generators = Server.System<GeneratorSystem>();
            Assert.That(generators.GetFuel(uid), Is.EqualTo(1));
            for (var i = 0; i < 1000; i++) SEntMan.EventBus.RaiseLocalEvent(uid, new GeneratorUseFuel(.001f));
            Assert.That(input!.Volume.Float(), Is.Zero.Within(.031f));
            Assert.That(output!.GetTotalPrototypeQuantity("Water").Float(), Is.EqualTo(2).Within(.021f));
            Assert.That(generators.GetFuel(uid), Is.Zero.Within(.011f));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task GasGeneratorRequiresOxidizerAndAFreeExhaust()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("IndustrialGasGenerator", map.GridCoords);
            Server.System<SharedTransformSystem>().AnchorEntity(uid);
            Server.System<NodeGroupSystem>().ForceUpdate();
            var nodes = Server.System<NodeContainerSystem>();
            Assert.That(nodes.TryGetNode(uid, "fuel", out PipeNode? inlet));
            Assert.That(nodes.TryGetNode(uid, "exhaust", out PipeNode? outlet));
            Assert.That(ReferenceEquals(inlet!.Air, outlet!.Air), Is.False, "The test must fill two real, separate pipe networks.");
            var generators = Server.System<GeneratorSystem>();
            inlet!.Air.SetMoles(Gas.Plasma, 1);
            Assert.That(generators.GetFuel(uid), Is.Zero);
            inlet.Air.SetMoles(Gas.Oxygen, 2);
            Assert.That(generators.GetFuel(uid), Is.EqualTo(10).Within(.001));
            SEntMan.EventBus.RaiseLocalEvent(uid, new GeneratorUseFuel(5));
            Assert.That(inlet.Air[(int) Gas.Plasma], Is.EqualTo(.5f).Within(.001));
            Assert.That(inlet.Air[(int) Gas.Oxygen], Is.EqualTo(1).Within(.001));
            Assert.That(outlet!.Air[(int) Gas.CarbonDioxide], Is.EqualTo(.5f).Within(.001));
            outlet.Air.SetMoles(Gas.CarbonDioxide, 1000);
            Assert.That(generators.GetFuel(uid), Is.Zero, "A blocked exhaust must prevent generation.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task GeneratorTankAcceptsFuelThroughItsPhysicalLiquidPipeAndRejectsContamination()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(0, 1), map.Tile.Tile);
            var generator = SEntMan.SpawnEntity("IndustrialLiquidFuelGenerator", new EntityCoordinates(map.Grid.Owner, .5f, 1.5f));
            Server.System<SharedTransformSystem>().AnchorEntity(generator);
            var pipe = SEntMan.SpawnEntity("BasicLiquidPipe", new EntityCoordinates(map.Grid.Owner, .5f, .5f));
            var solutions = Server.System<SharedSolutionContainerSystem>();
            solutions.TryGetSolution(pipe, LiquidPipeComponent.SolutionName, out var source, out var stored);
            solutions.TryGetSolution(generator, "tank", out _, out var tank);
            solutions.TryAddReagent(source!.Value, "WeldingFuel", FixedPoint2.New(5));
            var network = Server.System<LiquidPipeNetworkSystem>();
            for (var i = 0; i < 8; i++) network.Update(.25f);
            Assert.That(tank!.Volume.Float(), Is.GreaterThan(0));
            Assert.That(tank.Volume.Float() + stored!.Volume.Float(), Is.EqualTo(5).Within(.001));
            solutions.TryAddReagent(source.Value, "Water", FixedPoint2.New(1));
            var before = tank.Volume;
            for (var i = 0; i < 8; i++) network.Update(.25f);
            Assert.That(tank.Volume, Is.EqualTo(before), "An endpoint must not separate fuel out of a contaminated mixture for free.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task GeothermalExtractionUsesOneFinitePassableFloorReserve()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var deposit = SEntMan.SpawnEntity("IndustrialGeothermalDeposit", map.GridCoords);
            Assert.That(SEntMan.HasComponent<Robust.Shared.Physics.Components.PhysicsComponent>(deposit), Is.False);
            var generator = SEntMan.SpawnEntity("IndustrialGeothermalGenerator", map.GridCoords);
            Server.System<SharedTransformSystem>().AnchorEntity(generator);
            var reserve = SEntMan.GetComponent<GeothermalDepositComponent>(deposit);
            reserve.RemainingEnergy = 6000000;
            var generators = Server.System<GeneratorSystem>();
            Assert.That(generators.GetFuel(generator), Is.EqualTo(2));
            SEntMan.EventBus.RaiseLocalEvent(generator, new GeneratorUseFuel(1.5f));
            Assert.That(reserve.RemainingEnergy, Is.EqualTo(1500000));
            SEntMan.EventBus.RaiseLocalEvent(generator, new GeneratorUseFuel(.5f));
            Assert.That(generators.GetFuel(generator), Is.Zero);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
