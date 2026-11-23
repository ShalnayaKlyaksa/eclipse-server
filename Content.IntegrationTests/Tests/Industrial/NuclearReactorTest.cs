using System;
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Power;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.Generation.Teg;
using Content.Server.Power.Generator;
using Content.Server.Materials;
using Content.Shared._Eclipse.Industrial;
using Content.Server._Eclipse.Industrial;
using Content.Server.Stack;
using Content.Shared.Stacks;
using Content.Shared.Interaction;
using Content.Shared.Construction.Components;
using Robust.Shared.Containers;
using Content.Shared._Eclipse.Power;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class NuclearReactorTest : GameTest
{
    [Test]
    public void FissionAndShutdownConserveFiniteFuelEnergy()
    {
        var core = new NuclearReactorComponent { HeatCapacity = 1000, Scrammed = false, RodPosition = 0, RodTarget = 0, NeutronPower = 100000 };
        var before = (double) core.Temperature * core.HeatCapacity + core.DecayEnergy;
        var burned = NuclearReactorSystem.StepPhysics(core, .25f, 151.25f, 1);
        var after = (double) core.Temperature * core.HeatCapacity + core.DecayEnergy;
        Assert.That(burned, Is.EqualTo(151.25f), "A nearly spent assembly cannot power a whole step.");
        Assert.That(after - before, Is.EqualTo(burned).Within(.04));
        core.Scrammed = true;
        core.RodPosition = 1;
        before = (double) core.Temperature * core.HeatCapacity + core.DecayEnergy;
        var temperature = core.Temperature;
        NuclearReactorSystem.StepPhysics(core, 1, 0, 1);
        after = (double) core.Temperature * core.HeatCapacity + core.DecayEnergy;
        Assert.That(core.ThermalPower, Is.Zero);
        Assert.That(core.DecayPower, Is.GreaterThan(0));
        Assert.That(core.Temperature, Is.GreaterThan(temperature), "Shutdown does not remove decay heat.");
        Assert.That(after, Is.EqualTo(before).Within(.04));
    }

    [Test]
    public void RodsTakeTimeToMoveAndScramOverridesWithdrawal()
    {
        var core = new NuclearReactorComponent { Scrammed = false, RodTarget = 0 };
        NuclearReactorSystem.StepPhysics(core, 1, 1000000, 1);
        Assert.That(core.RodPosition, Is.EqualTo(.95f).Within(.0001));
        core.Scrammed = true;
        core.RodTarget = 0;
        NuclearReactorSystem.StepPhysics(core, 1, 1000000, 1);
        Assert.That(core.RodPosition, Is.EqualTo(1));
        Assert.That(core.Reactivity, Is.LessThan(0));
        var position = core.RodPosition;
        NuclearReactorSystem.StepPhysics(core, float.NaN, 1000000, 1);
        Assert.That(core.RodPosition, Is.EqualTo(position));
    }

    [Test]
    public void MissingAbsorberChannelsCannotProvideImaginaryShutdownWorth()
    {
        var intact = new NuclearReactorComponent();
        var broken = new NuclearReactorComponent { ControlAvailability = 0 };
        NuclearReactorSystem.StepPhysics(intact, 1, 100000, 1);
        NuclearReactorSystem.StepPhysics(broken, 1, 100000, 1);
        Assert.That(intact.Reactivity, Is.LessThan(0));
        Assert.That(broken.Reactivity, Is.GreaterThan(0));
        Assert.That(broken.ThermalPower, Is.GreaterThan(intact.ThermalPower));
    }

    [Test]
    public void CoolantReceivesExactlyTheHeatLostByTheCore()
    {
        var core = new NuclearReactorComponent { Temperature = 900, HeatCapacity = 10000, MaximumHeatTransfer = 100000 };
        var before = core.Temperature;
        var heat = NuclearReactorSystem.ExchangeHeat(core, 300, 1000, .25f);
        Assert.That(heat, Is.EqualTo(25000));
        Assert.That((before - core.Temperature) * core.HeatCapacity, Is.EqualTo(heat).Within(.1));
        before = core.Temperature;
        Assert.That(NuclearReactorSystem.ExchangeHeat(core, 300, 0, 1), Is.Zero);
        Assert.That(core.Temperature, Is.EqualTo(before));
    }

    [Test]
    public void TegCannotCreateHeatOrElectricityWithoutATemperatureDifference()
    {
        var hot = new GasMixture(100) { Temperature = 1000 };
        var cold = new GasMixture(100) { Temperature = 300 };
        hot.SetMoles(Gas.CarbonDioxide, 10);
        cold.SetMoles(Gas.CarbonDioxide, 10);
        var before = hot.Temperature * 1000 + cold.Temperature * 1000;
        var output = TegSystem.ExchangeHeat(hot, cold, 1000, 1000, .5f, 1, 12000);
        Assert.That(output, Is.GreaterThan(0).And.LessThanOrEqualTo(12000));
        Assert.That(before - (hot.Temperature * 1000 + cold.Temperature * 1000), Is.EqualTo(output).Within(.25));
        cold.Temperature = hot.Temperature;
        Assert.That(TegSystem.ExchangeHeat(hot, cold, 1000, 1000, .5f, 1, 12000), Is.Zero);
    }

    [Test]
    public async Task MassConverterNeedsFourPoweredPhysicalStabilizersAndStopsBurningOnFieldLoss()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var transform = Server.System<SharedTransformSystem>();
            for (var x = -2; x < 6; x++)
            for (var y = -2; y < 6; y++) maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            var generator = SEntMan.SpawnEntity("IndustrialMicroBlackHoleGenerator", new EntityCoordinates(map.Grid.Owner, 1.5f, 1.5f));
            transform.AnchorEntity(generator);
            var plant = SEntMan.GetComponent<PowerPlantComponent>(generator);
            var generators = Server.System<GeneratorSystem>();
            Server.System<MaterialStorageSystem>().TryChangeMaterialAmount(generator, "Steel", 1000);
            var stabilizers = new List<EntityUid>();
            foreach (var offset in new[] { new Vector2i(-3, 0), new Vector2i(3, 0), new Vector2i(0, -3), new Vector2i(0, 3) })
            {
                var stabilizer = SEntMan.SpawnEntity("PowerPlantFieldStabilizer", new EntityCoordinates(map.Grid.Owner, 1.5f + offset.X, 1.5f + offset.Y));
                transform.AnchorEntity(stabilizer);
                SEntMan.GetComponent<PowerConsumerComponent>(stabilizer).NetworkLoad.ReceivingPower = 30000;
                stabilizers.Add(stabilizer);
            }
            var plants = Server.System<PowerPlantSystem>();
            plants.Update(.5f);
            Assert.That(plant.FieldCount, Is.EqualTo(4));
            generators.SetFuelGeneratorOn(generator, true);
            var before = generators.GetFuel(generator);
            generators.Update(1);
            Assert.That(SEntMan.GetComponent<PowerSupplierComponent>(generator).MaxSupply, Is.Zero);
            Assert.That(SEntMan.GetComponent<GravityPowerSourceComponent>(generator).PendingPower, Is.GreaterThan(0));
            Assert.That(generators.GetFuel(generator), Is.LessThan(before));
            SEntMan.GetComponent<PowerConsumerComponent>(stabilizers[0]).NetworkLoad.ReceivingPower = 0;
            plants.Update(.5f);
            before = generators.GetFuel(generator);
            generators.Update(1);
            Assert.That(plant.Tripped, Is.True);
            Assert.That(plant.Condition, Is.EqualTo(PowerPlantCondition.MissingField));
            Assert.That(generators.IsRunning(generator), Is.False);
            Assert.That(generators.GetFuel(generator), Is.EqualTo(before).Within(.00001));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task StartingBeforeTheFieldIsReadyWaitsWithoutBurningFuelOrTripping()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("IndustrialMicroBlackHoleGenerator", map.GridCoords);
            Server.System<SharedTransformSystem>().AnchorEntity(uid);
            Server.System<MaterialStorageSystem>().TryChangeMaterialAmount(uid, "Steel", 1000);
            var generators = Server.System<GeneratorSystem>();
            var plant = SEntMan.GetComponent<PowerPlantComponent>(uid);
            var fuel = generators.GetFuel(uid);
            generators.SetFuelGeneratorOn(uid, true);
            generators.Update(1);
            Assert.That(plant.Tripped, Is.False);
            Assert.That(plant.Warmup, Is.Zero);
            Assert.That(plant.Condition, Is.EqualTo(PowerPlantCondition.MissingField));
            Assert.That(generators.GetFuel(uid), Is.EqualTo(fuel));
            Assert.That(SEntMan.GetComponent<PowerSupplierComponent>(uid).MaxSupply, Is.Zero);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task EarlySupplyDoesNotRequireTheLateDepartmentEquipmentBranches()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>();
            foreach (var id in new[] { "IndustrialEarlyFlashlights", "IndustrialEarlySmallCells", "IndustrialEarlyNetworkTools",
                         "IndustrialEarlyHandheldRadios", "IndustrialProduceSalvageDrill", "IndustrialProduceSecurityHelmet" })
            {
                var recipe = prototypes.Index<Content.Shared._Eclipse.Industrial.Prototypes.IndustrialRecipePrototype>(id);
                Assert.That(recipe.MinimumTier, Is.EqualTo(MachineTier.Basic), id);
                Assert.That(recipe.RequiredTechnology!.Value.Id, Is.AnyOf("IndustrialElectronics", "IndustrialAutomation"), id);
            }
        });
    }

    [Test]
    public async Task ReactorSectionsCanBeManufacturedTransportedAndUnpackedWithAWrench()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var assembler = SEntMan.SpawnEntity("IndustrialAssembler", map.GridCoords);
            var machine = SEntMan.GetComponent<IndustrialProcessorComponent>(assembler);
            machine.Enabled = false;
            machine.CanWorkWithoutPower = true;
            machine.SelectedRecipe = new("AssembleNuclearReactorShield");
            SEntMan.GetComponent<IndustrialMachineKnowledgeComponent>(assembler).Technologies.Add(new("EnergyFission"));
            var containers = Server.System<SharedContainerSystem>();
            var input = containers.GetContainer(assembler, machine.InputContainerId);
            foreach (var (id, amount) in new[] { ("SheetSteel1", 8), ("SheetUranium1", 2) })
            {
                var material = SEntMan.SpawnEntity(id, map.GridCoords);
                Server.System<StackSystem>().SetCount((material, SEntMan.GetComponent<StackComponent>(material)), amount);
                Assert.That(containers.Insert(material, input), Is.True);
            }
            SEntMan.EventBus.RaiseLocalEvent(assembler, new IndustrialProcessorToggleMessage { Actor = assembler });
            Server.System<IndustrialProcessorSystem>().Update(30);
            var output = containers.GetContainer(assembler, machine.OutputContainerId);
            Assert.That(output.Count, Is.EqualTo(1));
            var flatpack = output.ContainedEntities[0];
            Assert.That(SEntMan.GetComponent<FlatpackComponent>(flatpack).Entity!.Value.Id, Is.EqualTo("NuclearReactorShield"));
            Assert.That(SEntMan.HasComponent<Content.Shared.Item.ItemComponent>(flatpack), Is.True);
            Assert.That(containers.Remove(flatpack, output), Is.True);
            var position = new EntityCoordinates(map.Grid.Owner, 2.5f, 2.5f);
            Server.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(2, 2), map.Tile.Tile);
            Server.System<SharedTransformSystem>().SetCoordinates(flatpack, position);
            var actor = SEntMan.SpawnEntity(null, position);
            var wrench = SEntMan.SpawnEntity("Wrench", position);
            var interaction = new InteractUsingEvent(actor, wrench, flatpack, position);
            SEntMan.EventBus.RaiseLocalEvent(flatpack, interaction);
            Assert.That(interaction.Handled, Is.True);
            var found = false;
            var query = SEntMan.EntityQueryEnumerator<NuclearReactorPartComponent>();
            while (query.MoveNext(out _, out var part)) if (part.Kind == NuclearPartKind.Shield) found = true;
            Assert.That(found, Is.True, "Unpacking must create the physical section, not just consume the package.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task DestroyingAHotCoreDoesNotDeleteItsStoredHeatAndDecayEnergy()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("NuclearReactorCore", map.GridCoords);
            var reactor = SEntMan.GetComponent<NuclearReactorComponent>(uid);
            reactor.Temperature = 950;
            reactor.DecayEnergy = 123456;
            SEntMan.DeleteEntity(uid);
            var count = 0;
            var query = SEntMan.EntityQueryEnumerator<NuclearCoriumComponent>();
            while (query.MoveNext(out _, out var corium))
            {
                count++;
                Assert.That(corium.Temperature, Is.EqualTo(950));
                Assert.That(corium.DecayEnergy, Is.EqualTo(123456));
                Assert.That(corium.HeatCapacity, Is.EqualTo(reactor.HeatCapacity));
            }
            Assert.That(count, Is.EqualTo(1));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task PhysicalCoreRequiresEveryTileAndTripsWhenCoolingOrAShieldIsLost()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var transforms = Server.System<SharedTransformSystem>();
            var parts = new Dictionary<(int, int), EntityUid>();
            // Keep the test map's original tile connected to the entire reactor platform.
            for (var y = 0; y <= 8; y++)
            for (var x = 0; x <= 8; x++) maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            for (var y = -3; y <= 3; y++)
            for (var x = -3; x <= 3; x++)
            {
                var kind = NuclearReactorSystem.RequiredPart(x, y);
                if (kind == NuclearPartKind.Fuel)
                    kind = Math.Abs(x) == 1 && Math.Abs(y) == 1 ? NuclearPartKind.Control :
                        (x + y) % 2 == 0 ? NuclearPartKind.Graphite : NuclearPartKind.Fuel;
                var id = kind switch
                {
                    NuclearPartKind.Core => "NuclearReactorCore", NuclearPartKind.Shield => "NuclearReactorShield",
                    NuclearPartKind.Fuel => "NuclearReactorFuelChannel", NuclearPartKind.Graphite => "NuclearReactorGraphite",
                    NuclearPartKind.Control => "NuclearReactorControlChannel", NuclearPartKind.Inlet => "NuclearReactorInlet",
                    NuclearPartKind.Outlet => "NuclearReactorOutlet", _ => "NuclearReactorConsole",
                };
                var uid = SEntMan.SpawnEntity(id, new EntityCoordinates(map.Grid.Owner, x + 4.5f, y + 4.5f));
                transforms.AnchorEntity(uid);
                parts[(x, y)] = uid;
            }
            Server.System<NodeGroupSystem>().ForceUpdate();
            var reactor = Server.System<NuclearReactorSystem>();
            var center = parts[(0, 0)];
            var core = SEntMan.GetComponent<NuclearReactorComponent>(center);
            reactor.ScanLayout((center, core));
            Assert.That(core.Complete, Is.True);
            Assert.That(core.Cells.Length, Is.EqualTo(49));
            SEntMan.GetComponent<ApcPowerReceiverComponent>(parts[(0, 3)]).Powered = true;
            core.Scrammed = false;
            reactor.Update(.25f);
            Assert.That(core.Scrammed, Is.True, "A physically complete but empty coolant loop must trip.");
            Assert.That(core.Status, Is.EqualTo("nuclear-status-no-flow"));

            var nodes = Server.System<NodeContainerSystem>();
            Assert.That(nodes.TryGetNode(parts[(-3, 0)], "coolant", out PipeNode? inlet), Is.True);
            Assert.That(nodes.TryGetNode(parts[(3, 0)], "coolant", out PipeNode? outlet), Is.True);
            inlet!.Air.SetMoles(Gas.CarbonDioxide, 100);
            outlet!.Air.Clear();
            core.Temperature = 700;
            core.Scrammed = false;
            reactor.Update(.25f);
            Assert.That(core.Flow, Is.GreaterThan(0));
            Assert.That(core.CoolingPower, Is.GreaterThan(0));
            Assert.That(core.Scrammed, Is.False);
            Assert.That(outlet.Air.TotalMoles, Is.GreaterThan(0));
            SEntMan.DeleteEntity(parts[(-3, -3)]);
            reactor.ScanLayout((center, core));
            Assert.That(core.Complete, Is.False);
            reactor.Update(.25f);
            Assert.That(core.Scrammed, Is.True);
            Assert.That(core.Status, Is.EqualTo("nuclear-status-incomplete"));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
