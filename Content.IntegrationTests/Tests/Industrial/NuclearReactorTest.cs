#nullable enable annotations

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
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceLinking;
using Content.Shared.Radiation.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class NuclearReactorTest : GameTest
{
    [Test]
    public void FissionAndShutdownConserveFiniteFuelEnergy()
    {
        var core = new NuclearReactorComponent { HeatCapacity = 1000, Scrammed = false, NeutronPower = 100000 };
        core.RodGroups[0] = new(0, 0, 4);
        var before = (double) core.Temperature * core.HeatCapacity + core.DecayEnergy;
        var burned = NuclearReactorSystem.StepPhysics(core, .25f, 151.25f, 1);
        var after = (double) core.Temperature * core.HeatCapacity + core.DecayEnergy;
        Assert.That(burned, Is.EqualTo(151.25f), "A nearly spent assembly cannot power a whole step.");
        Assert.That(after - before, Is.EqualTo(burned).Within(.04));
        core.Scrammed = true;
        core.RodGroups[0] = new(1, 1, 4);
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
        var core = new NuclearReactorComponent { Scrammed = false };
        core.RodGroups[0] = new(1, 0, 2);
        core.RodGroups[1] = new(.5f, .5f, 2);
        NuclearReactorSystem.StepPhysics(core, 1, 1000000, 1);
        Assert.That(core.RodGroups[0].Position, Is.EqualTo(.95f).Within(.0001));
        Assert.That(core.RodGroups[1].Position, Is.EqualTo(.5f));
        core.Scrammed = true;
        NuclearReactorSystem.StepPhysics(core, 1, 1000000, 1);
        Assert.That(core.RodGroups[0].Position, Is.EqualTo(1));
        Assert.That(core.RodGroups[1].Position, Is.EqualTo(1));
        Assert.That(core.RodGroups[0].Target, Is.EqualTo(1));
        Assert.That(core.RodGroups[1].Target, Is.EqualTo(1));
        Assert.That(core.Reactivity, Is.LessThan(0));
        var position = core.RodGroups[0].Position;
        NuclearReactorSystem.StepPhysics(core, float.NaN, 1000000, 1);
        Assert.That(core.RodGroups[0].Position, Is.EqualTo(position));
    }

    [Test]
    public void MissingAbsorberChannelsCannotProvideImaginaryShutdownWorth()
    {
        var intact = new NuclearReactorComponent();
        intact.RodGroups[0] = new(1, 1, 4);
        var broken = new NuclearReactorComponent();
        NuclearReactorSystem.StepPhysics(intact, 1, 100000, 1);
        NuclearReactorSystem.StepPhysics(broken, 1, 100000, 1);
        Assert.That(intact.Reactivity, Is.LessThan(0));
        Assert.That(broken.Reactivity, Is.GreaterThan(0));
        Assert.That(broken.ThermalPower, Is.GreaterThan(intact.ThermalPower));
    }

    [Test]
    public void ConditionedMoxPeaksAtNinetyEightPercentHeat()
    {
        Assert.That(NuclearReactorSystem.MoxPowerFactor(.90f, true), Is.GreaterThan(1).And.LessThan(3));
        Assert.That(NuclearReactorSystem.MoxPowerFactor(.98f, true), Is.EqualTo(3).Within(.001));
        Assert.That(NuclearReactorSystem.MoxPowerFactor(.99f, true), Is.EqualTo(3).Within(.001));
        Assert.That(NuclearReactorSystem.MoxPowerFactor(.98f, false), Is.EqualTo(1).Within(.001));
        Assert.That(NuclearReactorSystem.FuelFactor(NuclearFuelKind.Thorium, 0), Is.LessThan(1));
        Assert.That(NuclearReactorSystem.FuelFactor(NuclearFuelKind.Pulse, 3), Is.GreaterThan(
            NuclearReactorSystem.FuelFactor(NuclearFuelKind.Pulse, 0)));
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
    public async Task SteppedHullSupportsOffsetCollectorsAndCoordinateBasedRodAssignment()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var transforms = Server.System<SharedTransformSystem>();
            for (var x = 0; x < 12; x++)
            for (var y = 0; y < 12; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);

            EntityUid Place(string prototype, (int X, int Y) tile)
            {
                var uid = SEntMan.SpawnEntity(prototype, new EntityCoordinates(map.Grid.Owner, tile.X + 5.5f, tile.Y + 5.5f));
                if (!SEntMan.GetComponent<TransformComponent>(uid).Anchored)
                    transforms.AnchorEntity(uid);
                return uid;
            }

            var interior = new HashSet<(int X, int Y)>();
            for (var x = -2; x <= 2; x++)
            for (var y = -2; y <= 3; y++)
                if (x >= 0 || y <= 0)
                    interior.Add((x, y));

            var hull = new HashSet<(int X, int Y)>();
            foreach (var (x, y) in interior)
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    if (!interior.Contains((x + dx, y + dy)))
                        hull.Add((x + dx, y + dy));

            var coreUid = Place("NuclearReactorCore", (0, 0));
            var core = SEntMan.GetComponent<NuclearReactorComponent>(coreUid);
            var reactor = Server.System<NuclearReactorSystem>();
            var rods = new HashSet<(int X, int Y)> { (-1, -1), (1, -1), (1, 1), (1, 2) };
            foreach (var tile in interior)
                if (tile != (0, 0))
                    Place(rods.Contains(tile) ? "NuclearReactorControlChannel" :
                        (tile.X + tile.Y) % 2 == 0 ? "NuclearReactorGraphite" : "NuclearReactorFuelChannel", tile);

            EntityUid inlet = default, outlet = default, breach = default;
            foreach (var tile in hull)
            {
                if (tile == (-3, -1))
                    inlet = Place("NuclearReactorWaterInlet", tile);
                else if (tile == (3, 1))
                    outlet = Place("NuclearReactorSteamOutlet", tile);
                else
                {
                    var shield = Place("NuclearReactorShield", tile);
                    if (tile == (-3, 0))
                        breach = shield;
                }
            }

            var console = Place("NuclearReactorConsole", (-3, 3));
            SEntMan.GetComponent<ApcPowerReceiverComponent>(console).Powered = true;
            reactor.ScanLayout((coreUid, core));
            Assert.That(core.Complete, Is.True);
            Assert.That((core.Width, core.Height), Is.EqualTo((7, 8)));
            Assert.That(core.Cells[0].Kind, Is.EqualTo(NuclearPartKind.Outside));
            Assert.That(core.Inlet, Is.EqualTo(inlet));
            Assert.That(core.Outlet, Is.EqualTo(outlet));
            Assert.That(core.SteamCooling, Is.True);
            var links = Server.System<SharedDeviceLinkSystem>();
            var overlappingConsole = Place("NuclearReactorConsole", (-2, 3));
            links.LinkDefaults(null, overlappingConsole, coreUid);
            Assert.That(links.GetLinkedSinks(overlappingConsole, "NuclearReactorControl"), Is.Empty,
                "An end of the desk reaches into the reactor footprint.");
            Assert.That(SEntMan.GetComponent<DeviceLinkSinkComponent>(coreUid).LinkedSources, Does.Not.Contain(overlappingConsole));
            SEntMan.DeleteEntity(overlappingConsole);
            links.LinkDefaults(null, console, coreUid);
            SEntMan.EventBus.RaiseLocalEvent(console,
                new NuclearReactorCommandMessage(NuclearCommand.AssignGroup, group: 2, cellX: 1, cellY: 2));
            Assert.That(core.RodGroups[2].Count, Is.EqualTo(1));
            SEntMan.DeleteEntity(breach);
            reactor.ScanLayout((coreUid, core));
            Assert.That(core.Complete, Is.False);
            Assert.That(core.HullClosed, Is.False);
            Assert.That(core.Inlet, Is.EqualTo(inlet), "The installed cooling route is still available after scram.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [TestCase(NuclearFuelKind.Uranium, "UraniumDust1", 6, null, 0, null, 0)]
    [TestCase(NuclearFuelKind.Thorium, "NuclearThoriumFeed", 1, null, 0, null, 0)]
    [TestCase(NuclearFuelKind.Mox, "UraniumDust1", 8, "SheetPlasma1", 3, "IndustrialHeatConduitAssembly", 2)]
    [TestCase(NuclearFuelKind.Pulse, "UraniumDust1", 6, "SheetPlasma1", 1, "IndustrialAdvancedBoard", 1)]
    public async Task FuelLineTransfersCellsAndSealsRadioactiveCartridges(NuclearFuelKind kind, string feedstock, int feedCount,
        string? pressPart, int pressCount, string? sealerPart, int sealerCount)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var containers = Server.System<SharedContainerSystem>();
            var processors = Server.System<IndustrialProcessorSystem>();
            var stacks = Server.System<StackSystem>();
            var maps = Server.System<SharedMapSystem>();
            for (var x = 1; x <= 3; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);

            EntityUid Place(string id, int x) => SEntMan.SpawnEntity(id, new EntityCoordinates(map.Grid.Owner, x + .5f, .5f));
            var press = Prepare(Place("NuclearFuelPress", 0), $"PressNuclear{kind}Element");
            var fabricator = Prepare(Place("NuclearCartridgeFabricator", 2), $"AssembleNuclear{kind}Cell");
            var assembler = Prepare(Place("IndustrialAssembler", 3), "AssembleNuclearEmptyCartridges");
            var pipe = Place("LpItemPipe", 1);

            Load(assembler, "SheetPlasteel1", 8);
            Begin(assembler);
            processors.Update(30);
            var casings = containers.GetContainer(assembler, assembler.Comp.OutputContainerId);
            Assert.That(casings.Count, Is.EqualTo(4));
            var casing = casings.ContainedEntities[0];
            var sealerInput = containers.GetContainer(fabricator, fabricator.Comp.InputContainerId);
            Assert.That(containers.Insert(casing, sealerInput), Is.True);
            if (sealerPart != null)
                Load(fabricator, sealerPart, sealerCount);
            Begin(fabricator);

            Load(press, feedstock, feedCount);
            if (pressPart != null)
                Load(press, pressPart, pressCount);
            Begin(press);
            processors.Update(30);
            var pressOutput = containers.GetContainer(press, press.Comp.OutputContainerId);
            Assert.That(pressOutput.Count, Is.EqualTo(1));
            var cell = pressOutput.ContainedEntities[0];
            Assert.That(SEntMan.GetComponent<RadiationSourceComponent>(cell).Intensity, Is.GreaterThan(0));
            Assert.That(SEntMan.HasComponent<NuclearFuelCellComponent>(cell), Is.False);
            var networks = Server.System<ItemPipeNetworkSystem>();
            networks.RebuildNetworkFrom((pipe, SEntMan.GetComponent<ItemPipeComponent>(pipe)));
            networks.Update(1);
            Assert.That(pressOutput.Count, Is.Zero);
            Assert.That(sealerInput.Contains(cell), Is.True);

            processors.Update(30);
            var finished = containers.GetContainer(fabricator, fabricator.Comp.OutputContainerId);
            Assert.That(finished.Count, Is.EqualTo(1));
            Assert.That(sealerInput.Count, Is.Zero);
            var cartridge = finished.ContainedEntities[0];
            Assert.That(SEntMan.GetComponent<RadiationSourceComponent>(cartridge).Intensity, Is.GreaterThan(0));
            Assert.That(SEntMan.GetComponent<NuclearFuelCellComponent>(cartridge).Kind, Is.EqualTo(kind));
            SEntMan.DeleteEntity(map.MapUid);

            Entity<IndustrialProcessorComponent> Prepare(EntityUid uid, string recipe)
            {
                var machine = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
                machine.Enabled = false;
                machine.CanWorkWithoutPower = true;
                machine.SelectedRecipe = new(recipe);
                var knowledge = SEntMan.GetComponent<IndustrialMachineKnowledgeComponent>(uid);
                knowledge.Technologies.Add(new("EnergyFission"));
                knowledge.Technologies.Add(new("EnergyMox"));
                return (uid, machine);
            }

            void Load(Entity<IndustrialProcessorComponent> machine, string id, int count)
            {
                var input = containers.GetContainer(machine, machine.Comp.InputContainerId);
                foreach (var ingredient in stacks.SpawnMultipleAtPosition(new EntProtoId(id), count, map.GridCoords))
                    Assert.That(containers.Insert(ingredient, input), Is.True);
            }

            void Begin(EntityUid uid) => SEntMan.EventBus.RaiseLocalEvent(uid, new IndustrialProcessorToggleMessage { Actor = uid });
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

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task PhysicalCoreRequiresEveryTileAndTripsWhenCoolingOrAShieldIsLost(bool water, bool steam)
    {
        var map = await Pair.CreateTestMap();
        try
        {
            await Server.WaitAssertion(CheckReactor);
        }
        catch (Exception error)
        {
            TestContext.Error.WriteLine(error);
            throw;
        }

        void CheckReactor()
        {
            var maps = Server.System<SharedMapSystem>();
            var transforms = Server.System<SharedTransformSystem>();
            var parts = new Dictionary<(int, int), EntityUid>();
            // Keep the test map's original tile connected to the entire reactor platform.
            for (var y = steam ? -1 : 0; y <= 8; y++)
            for (var x = 0; x <= (steam ? 12 : 8); x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            EntityUid Place(string prototype, int x, int y)
            {
                var entity = SEntMan.SpawnEntity(prototype, new EntityCoordinates(map.Grid.Owner, x + .5f, y + .5f));
                if (!SEntMan.GetComponent<TransformComponent>(entity).Anchored)
                    transforms.AnchorEntity(entity);
                return entity;
            }

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
                    NuclearPartKind.Control => "NuclearReactorControlChannel",
                    NuclearPartKind.Inlet => water ? "NuclearReactorWaterInlet" : "NuclearReactorInlet",
                    NuclearPartKind.Outlet => steam ? "NuclearReactorSteamOutlet" :
                        water ? "NuclearReactorWaterOutlet" : "NuclearReactorOutlet",
                    _ => throw new InvalidOperationException(),
                };
                parts[(x, y)] = Place(id, x + 4, y + 4);
                Assert.That(SEntMan.GetComponent<PhysicsComponent>(parts[(x, y)]).CanCollide, Is.False, id);
            }

            var console = Place("NuclearReactorConsole", 4, 8);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(console).Powered = true;
            var links = Server.System<SharedDeviceLinkSystem>();
            var center = parts[(0, 0)];
            links.LinkDefaults(null, console, center);

            EntityUid turbine = default, condenser = default, returnPipe = default;
            if (steam)
            {
                turbine = Place("NuclearSteamTurbine", 9, 4);
                condenser = Place("NuclearSteamCondenser", 11, 4);
                returnPipe = Place("NuclearCoolantPipe", 12, 4);
                transforms.SetLocalRotation(Place("GasPipeStraight", 8, 4), Angle.FromDegrees(90));
                transforms.SetLocalRotation(Place("GasPipeStraight", 10, 4), Angle.FromDegrees(90));
                Place("CableHV", 9, 4);
                Place("CableHV", 9, 5);
                Place("SubstationBasic", 9, 5);
            }
            Server.System<NodeGroupSystem>().ForceUpdate();
            var reactor = Server.System<NuclearReactorSystem>();
            var core = SEntMan.GetComponent<NuclearReactorComponent>(center);
            reactor.ScanLayout((center, core));
            Assert.That(core.Complete, Is.True);
            Assert.That(core.WaterCooling, Is.EqualTo(water));
            Assert.That(core.SteamCooling, Is.EqualTo(steam));
            Assert.That(core.Cells.Length, Is.EqualTo(49));
            Assert.That(core.Cells[3].Kind, Is.EqualTo(NuclearPartKind.Shield));
            SEntMan.EventBus.RaiseLocalEvent(console, new NuclearReactorCommandMessage(NuclearCommand.AssignGroup, group: 1, cellX: 1, cellY: 1));
            Assert.That(core.RodGroups[1].Count, Is.EqualTo(1));
            var solutions = Server.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetRefillableSolution(center, out var charge, out _));
            Assert.That(solutions.TryAddReagent(charge!.Value, "Water", 160));
            reactor.Update(.25f);
            Assert.That(core.Status, Is.EqualTo("nuclear-status-heavy-water"));
            solutions.SplitSolution(charge.Value, 160);
            var jug = Place("Jug", 4, 7);
            Assert.That(solutions.TryGetDrainableSolution(jug, out var jugSolution, out _));
            Assert.That(solutions.TryAddReagent(jugSolution!.Value, "HeavyWater", 160));
            var poured = Server.System<SolutionTransferSystem>().Transfer(
                new(console, jug, jugSolution.Value, center, charge.Value, 160));
            Assert.That(poured.Float(), Is.EqualTo(160));
            core.Scrammed = false;
            for (var step = 0; step < (water ? 8 : 1); step++)
                reactor.Update(.25f);
            Assert.That(core.Scrammed, Is.True, "A physically complete but empty coolant loop must trip.");
            Assert.That(core.Status, Is.EqualTo("nuclear-status-no-flow"));

            var inletUid = parts[(-3, 0)];
            var outletUid = parts[(3, 0)];
            var nodes = Server.System<NodeContainerSystem>();
            if (water)
            {
                Assert.That(solutions.TryGetSolution(inletUid, "tank", out var inlet, out _));
                Assert.That(solutions.TryAddReagent(inlet!.Value, "Water", 40));
            }
            else
            {
                Assert.That(nodes.TryGetNode(inletUid, "coolant", out PipeNode? inlet));
                Assert.That(nodes.TryGetNode(outletUid, "coolant", out PipeNode? outlet));
                inlet!.Air.SetMoles(Gas.CarbonDioxide, 100);
                outlet!.Air.Clear();
            }
            core.Temperature = 700;
            core.Scrammed = false;
            var slots = Server.System<ItemSlotsSystem>();
            var hatch = parts[(1, 0)];
            Assert.That(slots.GetItemOrNull(hatch, NuclearReactorSystem.FuelSlot), Is.Not.Null);
            Assert.That(slots.TryEject(hatch, NuclearReactorSystem.FuelSlot, null, out _), Is.False);
            SEntMan.EventBus.RaiseLocalEvent(console, new NuclearReactorCommandMessage(NuclearCommand.Rods, .5f, 1));
            Assert.That(core.RodGroups[0].Target, Is.EqualTo(1));
            Assert.That(core.RodGroups[1].Target, Is.EqualTo(.5f));
            reactor.Update(.25f);
            Assert.That(core.Flow, Is.GreaterThan(0));
            Assert.That(core.CoolingPower, Is.GreaterThan(0));
            Assert.That(core.Scrammed, Is.False);
            if (steam)
            {
                Assert.That(nodes.TryGetNode(outletUid, "steam", out PipeNode? live));
                Assert.That(nodes.TryGetNode(condenser, "steam_in", out PipeNode? exhaust));
                Assert.That(nodes.TryGetNode(condenser, "coolant_in", out PipeNode? cold));
                Assert.That(nodes.TryGetNode(condenser, "coolant_out", out PipeNode? hot));
                var peak = 0f;
                for (var step = 0; step < 40; step++)
                {
                    // The bench serves as the heat sink on the condenser's independent cooling loop.
                    cold!.Air.Clear();
                    cold.Air.Temperature = 285;
                    cold.Air.SetMoles(Gas.CarbonDioxide, 12000);
                    hot!.Air.Clear();
                    reactor.Update(.25f);
                    Assert.That(hot.Air.TotalMoles, Is.GreaterThan(0));
                    peak = MathF.Max(peak, SEntMan.GetComponent<PowerSupplierComponent>(turbine).MaxSupply);
                }
                Assert.That(peak, Is.GreaterThan(0));
                Assert.That(solutions.TryGetSolution(condenser, "output", out _, out var condensate));
                Assert.That(condensate!.GetTotalPrototypeQuantity("Water").Float(), Is.GreaterThan(0));
                Assert.That(solutions.TryGetSolution(inletUid, "tank", out _, out var stillInTank));
                Assert.That(solutions.TryGetSolution(outletUid, "output", out _, out var stillInBoiler));
                var inventory = stillInTank!.Volume.Float() + stillInBoiler!.Volume.Float() + condensate.Volume.Float() +
                                (live!.Air.GetMoles(Gas.WaterVapor) + exhaust!.Air.GetMoles(Gas.WaterVapor)) / 50;
                Assert.That(inventory, Is.EqualTo(40).Within(.02), "Boiling and condensation must preserve the water inventory.");
                Server.System<LiquidPipeNetworkSystem>().Update(.5f);
                Assert.That(solutions.TryGetSolution(returnPipe, LiquidPipeComponent.SolutionName, out _, out var returned));
                Assert.That(returned!.GetTotalPrototypeQuantity("Water").Float(), Is.GreaterThan(0));

                core.Temperature = 405;
                core.RodGroups[1] = core.RodGroups[1] with { Position = .5f };
                SEntMan.EventBus.RaiseLocalEvent(console, new NuclearReactorCommandMessage(NuclearCommand.Scram));
                var engineer = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 6.5f, 4.5f));
                var uranium = slots.GetItemOrNull(hatch, NuclearReactorSystem.FuelSlot);
                var movingRods = new InteractHandEvent(engineer, hatch);
                SEntMan.EventBus.RaiseLocalEvent(hatch, movingRods);
                Assert.That(movingRods.Handled, Is.True);
                Assert.That(slots.GetItemOrNull(hatch, NuclearReactorSystem.FuelSlot), Is.EqualTo(uranium));
                core.RodGroups[1] = core.RodGroups[1] with { Position = 1 };
                core.Temperature = 411;
                Assert.That(slots.TryEject(hatch, NuclearReactorSystem.FuelSlot, null, out _), Is.False);
                core.Temperature = 405;
                SEntMan.EventBus.RaiseLocalEvent(hatch, new InteractHandEvent(engineer, hatch));
                Assert.That(slots.GetItemOrNull(hatch, NuclearReactorSystem.FuelSlot), Is.Null);
                Assert.That(SEntMan.GetComponent<NuclearFuelCellComponent>(uranium!.Value).Energy, Is.LessThan(2000000000d));
                var mox = SEntMan.SpawnEntity("NuclearMoxCell", new EntityCoordinates(map.Grid.Owner, 7.5f, 5.5f));
                Assert.That(slots.TryInsert(hatch, NuclearReactorSystem.FuelSlot, mox, null));
                SEntMan.DeleteEntity(parts[(0, 2)]);
                var pulseHatch = Place("NuclearReactorFuelChannel", 4, 6);
                Assert.That(slots.TryEject(pulseHatch, NuclearReactorSystem.FuelSlot, null, out _));
                var pulse = SEntMan.SpawnEntity("NuclearPulseCell", new EntityCoordinates(map.Grid.Owner, 7.5f, 5.5f));
                Assert.That(slots.TryInsert(pulseHatch, NuclearReactorSystem.FuelSlot, pulse, null));
                SEntMan.DeleteEntity(parts[(2, 2)]);
                Place("NuclearReactorHeatSink", 6, 6);
                var station = Place("NuclearReactorConditioner", 4, 0);
                Place("GasPipeStraight", 4, -1);
                var warmPipe = Place("GasPipeStraight", 5, 0);
                transforms.SetLocalRotation(warmPipe, Angle.FromDegrees(90));
                Server.System<NodeGroupSystem>().ForceUpdate();
                reactor.ScanLayout((center, core));
                Assert.That(core.MoxCells, Is.EqualTo(1));
                Assert.That(core.Cells[10].FuelNeighbours, Is.EqualTo(3));
                Assert.That(core.Conditioner, Is.EqualTo(station));
                Assert.That(solutions.TryGetSolution(station, "conditioning", out var chemicals, out _));
                Assert.That(solutions.TryAddReagent(chemicals!.Value, "IndustrialOxidizer", 10));
                Assert.That(nodes.TryGetNode(station, "coolant_in", out PipeNode? auxiliaryFeed));
                Assert.That(nodes.TryGetNode(station, "coolant_out", out PipeNode? auxiliaryReturn));
                Assert.That(solutions.TryGetSolution(inletUid, "tank", out var feedTank, out _));
                Assert.That(solutions.TryAddReagent(feedTank!.Value, "Water", 80));
                live!.Air.Clear();
                exhaust!.Air.Clear();
                core.NoWaterFlowTime = 0;
                core.MaximumHeatTransfer = 0;
                core.Scrammed = false;
                core.Temperature = 772;
                auxiliaryFeed!.Air.SetMoles(Gas.Frezon, 1200);
                reactor.Update(.25f);
                Assert.That(core.MoxBoost, Is.False, "Both reagents are required.");
                Assert.That(core.CoolingContacts, Is.EqualTo(2));
                Assert.That(auxiliaryReturn!.Air.TotalMoles, Is.GreaterThan(0));
                Assert.That(solutions.TryAddReagent(chemicals.Value, "IndustrialSolvent", 10));
                reactor.Update(.25f);
                Assert.That(core.MoxBoost, Is.True,
                    $"Status {core.Status}, temperature {core.Temperature}, aux cooling {core.AuxiliaryCooling}, oxidizer {core.Oxidizer}, solvent {core.Solvent}.");
                Assert.That(core.Oxidizer, Is.LessThan(10));
                Assert.That(core.Solvent, Is.LessThan(10));
                SEntMan.DeleteEntity(warmPipe);
                Server.System<NodeGroupSystem>().ForceUpdate();
                reactor.Update(.25f);
                Assert.That(core.MoxBoost, Is.False, "A disconnected cooling return disables boost.");
                core.Temperature = 776;
                reactor.Update(.25f);
                Assert.That(core.Status, Is.EqualTo("nuclear-status-overheat"));
                Assert.That(core.Scrammed, Is.True);
            }
            else if (water)
            {
                Assert.That(solutions.TryGetSolution(outletUid, "output", out _, out var outlet));
                Assert.That(outlet!.GetTotalPrototypeQuantity("Water").Float(), Is.EqualTo(core.Flow * .25f).Within(.01));
                Assert.That(outlet.Temperature, Is.GreaterThan(293.15));
            }
            else
            {
                Assert.That(nodes.TryGetNode(outletUid, "coolant", out PipeNode? outlet));
                Assert.That(outlet!.Air.TotalMoles, Is.GreaterThan(0));
            }
            Assert.That(solutions.TryGetSolution(center, "charge", out _, out var coreCharge));
            Assert.That(coreCharge!.GetTotalPrototypeQuantity("HeavyWater").Float(), Is.EqualTo(160));
            solutions.SplitSolution(charge.Value, 10);
            reactor.Update(.25f);
            Assert.That(core.Status, Is.EqualTo("nuclear-status-heavy-water"));
            Assert.That(core.Scrammed, Is.True);
            links.RemoveSinkFromSource(console, center);
            reactor.Update(.25f);
            Assert.That(core.Status, Is.EqualTo("nuclear-status-power-loss"));
            Assert.That(core.RodGroups[1].Target, Is.EqualTo(1));
            SEntMan.DeleteEntity(parts[(-3, -3)]);
            reactor.ScanLayout((center, core));
            Assert.That(core.Complete, Is.False);
            reactor.Update(.25f);
            Assert.That(core.Scrammed, Is.True);
            Assert.That(core.Status, Is.EqualTo("nuclear-status-incomplete"));
            if (steam)
            {
                core.Temperature = 780;
                reactor.Update(.25f);
                Assert.That(core.Failed, Is.True, "An inserted MOX cell retains its thermal limit after scram.");
            }
            SEntMan.DeleteEntity(map.MapUid);
        }
    }
}
