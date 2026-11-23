using System;
using System.Collections.Generic;
using System.Linq;
using IndustrialDiskWriterBoundUserInterface = Content.Client._Eclipse.Industrial.IndustrialDiskWriterBoundUserInterface;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Industrial;
using Content.Server.Power.Components;
using Content.Server.Stack;
using Content.Shared._Eclipse.Industrial;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Access.Components;
using Content.Shared.Access;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class IndustrialProgressionTest : GameTest
{
    [TestCase("IndustrialProducePipeLP", "BasicLiquidPipe", "SheetSteel1")]
    [TestCase("IndustrialProducePipeGasStraight", "GasPipeStraight", "SheetSteel1")]
    [TestCase("IndustrialProduceItemPipeLP", "LpItemPipe", "IndustrialIronPlate1")]
    public async Task PipeFabricatorProducesPortableDisconnectedPipes(string recipe, string product, string material)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("IndustrialPipeFabricator", map.GridCoords);
            var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
            comp.Enabled = false;
            comp.CanWorkWithoutPower = true;
            comp.SelectedRecipe = new ProtoId<IndustrialRecipePrototype>(recipe);
            SEntMan.GetComponent<IndustrialMachineKnowledgeComponent>(uid).Technologies.Add(new("IndustrialAutomation"));
            var containers = Server.System<SharedContainerSystem>();
            Assert.That(containers.Insert(SEntMan.SpawnEntity(material, map.GridCoords), containers.GetContainer(uid, comp.InputContainerId)), Is.True);
            SEntMan.EventBus.RaiseLocalEvent(uid, new IndustrialProcessorToggleMessage { Actor = uid });
            Server.System<IndustrialProcessorSystem>().Update(30);
            var output = containers.GetContainer(uid, comp.OutputContainerId);
            Assert.That(output.Count, Is.EqualTo(2));
            foreach (var entity in output.ContainedEntities)
            {
                Assert.That(SEntMan.GetComponent<MetaDataComponent>(entity).EntityPrototype?.ID, Is.EqualTo(product));
                Assert.That(SEntMan.GetComponent<TransformComponent>(entity).Anchored, Is.False);
                Assert.That(SEntMan.HasComponent<Content.Shared.Item.ItemComponent>(entity), Is.True);
            }
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task PrecisionFactoryAndDepartmentEquipmentCanBeReachedWithoutPerfectTechnology()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            var resources = Server.System<IndustrialResourceSystem>();
            var recipes = prototypes.EnumeratePrototypes<IndustrialRecipePrototype>().ToArray();
            var crafts = prototypes.EnumeratePrototypes<IndustrialFabricationRecipePrototype>().ToArray();
            var technologies = prototypes.EnumeratePrototypes<TechnologyPrototype>()
                .Where(x => x.ID.StartsWith("Industrial", StringComparison.Ordinal)).ToDictionary(x => x.ID);
            var visiting = new HashSet<string>();
            var visited = new HashSet<string>();
            void Visit(string id)
            {
                if (visited.Contains(id)) return;
                Assert.That(visiting.Add(id), Is.True, "Research cycle at " + id);
                foreach (var parent in technologies[id].TechnologyPrerequisites)
                    if (technologies.ContainsKey(parent.Id)) Visit(parent.Id);
                visiting.Remove(id); visited.Add(id);
            }
            foreach (var id in technologies.Keys) Visit(id);
            var research = technologies.Values.Where(x => x.Tier < 4).Select(x => x.ID).ToHashSet();
            research.Add("Powercells"); research.Add("PowercellsAdvanced");
            var owned = new HashSet<string>();
            bool Has(string id) => owned.Contains(resources.Key(id));
            bool Add(string id)
            {
                var prototype = prototypes.Index<EntityPrototype>(id);
                var changed = owned.Add(resources.Key(id));
                if (prototype.TryGetComponent<Content.Shared.Construction.Components.FlatpackComponent>(out var pack, SEntMan.ComponentFactory) &&
                    pack.Entity is { } machine) changed |= owned.Add(resources.Key(machine.Id));
                return changed;
            }
            foreach (var id in new[] { "SteelOre1", "CopperOre1", "SpaceQuartz1", "Coal1", "GoldOre1", "SilverOre1", "PlasmaOre1", "UraniumOre1",
                         "SheetSteel1", "SheetGlass1", "MaterialWoodPlank1", "SheetPaper1", "PartRodMetal1", "MaterialCloth1" }) Add(id);
            var fluids = recipes.SelectMany(x => x.FluidInputs.Keys.Concat(x.Bath.Keys))
                .Where(x => !x.Id.StartsWith("Industrial", StringComparison.Ordinal)).Select(x => x.Id).ToHashSet();
            var machines = new Dictionary<IndustrialProcessorType, MachineTier>
                { [IndustrialProcessorType.Crusher] = MachineTier.Basic, [IndustrialProcessorType.Smelter] = MachineTier.Basic };
            var benchTier = 0; var roomGrade = 0;
            var entities = prototypes.EnumeratePrototypes<EntityPrototype>().Where(x => !x.Abstract && x.TryGetComponent<IndustrialUpgradeModuleComponent>(out _, SEntMan.ComponentFactory)).ToArray();
            for (var iteration = 0; iteration < 100; iteration++)
            {
                var changed = false;
                if (benchTier == 0 && Has("IndustrialIronPlate") && Has("MaterialWoodPlank1")) { benchTier = 1; changed = true; }
                if (Has("IndustrialWorkbenchTier2") && benchTier < 2) { benchTier = 2; changed = true; }
                if (Has("IndustrialAdvancedCleanroomController") && roomGrade < 2) { roomGrade = 2; changed = true; }
                else if (Has("IndustrialCleanroomController") && roomGrade < 1) { roomGrade = 1; changed = true; }
                if (Has("IndustrialChassisBlueprint") && Has("IndustrialIronPlate") && Has("IndustrialIronRod")) changed |= Add("IndustrialMachineChassis");
                foreach (var craft in crafts)
                {
                    if (benchTier < craft.MinimumWorkbenchTier || roomGrade < craft.CleanroomGrade ||
                        craft.RequiredTechnology is { } technology && !research.Contains(technology.Id) || !craft.Ingredients.Keys.All(Has)) continue;
                    changed |= Add(craft.Result.Id);
                }
                foreach (var entity in entities)
                {
                    if (!Has("IndustrialMachineChassis") || !Has(entity.ID) ||
                        !entity.TryGetComponent<IndustrialUpgradeModuleComponent>(out var module, SEntMan.ComponentFactory)) continue;
                    var target = prototypes.Index(module.ResultMachine);
                    Assert.That(target.TryGetComponent<IndustrialProcessorComponent>(out var machine, SEntMan.ComponentFactory), Is.True);
                    if (!machines.TryGetValue(machine!.ProcessorType, out var tier) || machine.Tier > tier)
                    { machines[machine.ProcessorType] = machine.Tier; changed = true; }
                }
                foreach (var recipe in recipes)
                {
                    if (!machines.TryGetValue(recipe.Processor, out var tier) || tier < recipe.MinimumTier || roomGrade < recipe.CleanroomGrade ||
                        recipe.RequiredTechnology is { } technology && !research.Contains(technology.Id) ||
                        !recipe.Inputs.Keys.All(Has) || !recipe.FluidInputs.Keys.Concat(recipe.Bath.Keys).All(x => fluids.Contains(x.Id))) continue;
                    foreach (var output in recipe.Outputs.Keys) changed |= Add(output);
                    foreach (var output in recipe.FluidOutputs.Keys) changed |= fluids.Add(output.Id);
                }
                if (!changed) break;
            }
            foreach (var id in new[] { "IndustrialCircuitCore", "IndustrialAdvancedProcessor", "IndustrialAdvancedBoard", "IndustrialWorkbenchTier2",
                         "IndustrialUpgradeModuleAssemblerIndustrial", "IndustrialDrill", "IndustrialPipeFabricator", "MiningDrill",
                         "IndustrialAdvancedArmor", "IndustrialAdvancedLaserCarbine", "PowerCellHighPrinted", "IndustrialResearchSampleElectronics" })
                Assert.That(Has(id) || id == "IndustrialPipeFabricator" && Has("IndustrialUpgradeModulePipeFabricator"), Is.True,
                    id + " must have a bootstrap path without perfect research or an already-built advanced assembler.");
            Assert.That(Has("IndustrialPerfectBoard"), Is.False);
        });
    }

    [Test]
    public async Task BlueprintPlacesOneStagedFrameAndScienceStudiesConsumedFactorySamples()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var actor = InstantActor(map.GridCoords);
            var plan = SEntMan.SpawnEntity("IndustrialChassisBlueprint", map.GridCoords);
            var placement = new AfterInteractEvent(actor, plan, null, map.GridCoords, true);
            SEntMan.EventBus.RaiseLocalEvent(plan, placement);
            Assert.That(placement.Handled, Is.True);
            Assert.That(SEntMan.Deleted(plan), Is.True);
            var duplicate = SEntMan.SpawnEntity("IndustrialChassisBlueprint", map.GridCoords);
            var repeated = new AfterInteractEvent(actor, duplicate, null, map.GridCoords, true);
            SEntMan.EventBus.RaiseLocalEvent(duplicate, repeated);
            Assert.That(repeated.Handled, Is.False, "A second blueprint must not overlap an unfinished frame.");
            Assert.That(SEntMan.Deleted(duplicate), Is.False);

            var console = SEntMan.SpawnEntity("ComputerResearchAndDevelopment", map.GridCoords);
            var server = SEntMan.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(console).Powered = true;
            SEntMan.GetComponent<ApcPowerReceiverComponent>(server).Powered = true;
            SEntMan.GetComponent<ResearchClientComponent>(console).Server = server;
            var research = Server.System<Content.Server.Research.Systems.ResearchSystem>();
            research.AddTechnology(server, "IndustrialElectronics");
            research.AddTechnology(server, "IndustrialAutomation");
            research.AddTechnology(server, "IndustrialMetallurgy");
            research.AddTechnology(server, "IndustrialResourceExtraction");
            var disk = SEntMan.SpawnEntity("IndustrialProductionDisk", map.GridCoords);
            var data = SEntMan.GetComponent<IndustrialDataDiskComponent>(disk);
            var progression = Server.System<IndustrialProgressionSystem>();
            var hands = Server.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(actor, disk), Is.True);
            ProtoId<TechnologyPrototype>[] selection = [new("IndustrialElectronics"), new("IndustrialAutomation"), new("IndustrialMetallurgy")];
            Assert.That(progression.TryWriteDisk((disk, data), console, actor, selection), Is.False, "Anonymous users cannot export science data.");
            SEntMan.EnsureComponent<AccessComponent>(actor).Tags.Add(new("Research"));
            var account = research.IssueCrewAccount(server, actor, "Scientist", "Factory operator");
            Assert.That(research.AuthenticateTerminal(console, actor, account.Username, account.Password, false), Is.True);
            var ui = Server.System<SharedUserInterfaceSystem>();
            var useDisk = new AfterInteractEvent(actor, disk, console, map.GridCoords, true);
            SEntMan.EventBus.RaiseLocalEvent(disk, useDisk);
            Assert.That(useDisk.Handled, Is.True);
            Assert.That(ui.TryGetUiState<IndustrialDiskWriterState>(disk, IndustrialDiskUiKey.Write, out var writer), Is.True);
            Assert.That(writer.Available, Is.SupersetOf(selection));
            SEntMan.EventBus.RaiseLocalEvent(disk, new IndustrialWriteDiskMessage(selection) { Actor = actor });
            Assert.That(data.Technologies, Is.EquivalentTo(selection));
            SEntMan.EventBus.RaiseLocalEvent(console, new IndustrialOpenDiskWriterMessage { Actor = actor });
            Assert.That(ui.TryGetUiState<IndustrialDiskWriterState>(disk, IndustrialDiskUiKey.Write, out writer), Is.True);
            Assert.That(writer.Recorded, Is.EquivalentTo(selection));
            Assert.That(progression.TryWriteDisk((disk, data), console, actor, [.. selection, new("IndustrialResourceExtraction")]), Is.False);
            Assert.That(progression.TryWriteDisk((disk, data), console, actor, [new("IndustrialHeavyExtraction")]), Is.False);
            Assert.That(progression.TryWriteDisk((disk, data), console, actor, [selection[0], selection[0]]), Is.False);
            Assert.That(data.Technologies, Is.EquivalentTo(selection));
            Assert.That(progression.TryWriteDisk((disk, data), console, actor, [selection[1]]), Is.True);
            Assert.That(data.Technologies, Is.EquivalentTo(new[] { selection[1] }));
            Assert.That(hands.TryDrop(actor, disk), Is.True);
            Assert.That(progression.TryWriteDisk((disk, data), console, actor, selection), Is.False);
            var sample = SEntMan.SpawnEntity("IndustrialResearchSampleGeology", map.GridCoords);
            Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(actor, sample), Is.True);
            var before = SEntMan.GetComponent<ResearchServerComponent>(server).Points;
            SEntMan.EventBus.RaiseLocalEvent(sample, new AfterInteractEvent(actor, sample, console, map.GridCoords, true));
            Assert.That(SEntMan.Deleted(sample), Is.True);
            Assert.That(SEntMan.GetComponent<ResearchServerComponent>(server).Points, Is.EqualTo(before + 750));
            foreach (var award in new[] { 375, 250, 187, 0 })
            {
                sample = SEntMan.SpawnEntity("IndustrialResearchSampleGeology", map.GridCoords);
                Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(actor, sample), Is.True);
                var points = SEntMan.GetComponent<ResearchServerComponent>(server).Points;
                SEntMan.EventBus.RaiseLocalEvent(sample, new AfterInteractEvent(actor, sample, console, map.GridCoords, true));
                Assert.That(SEntMan.GetComponent<ResearchServerComponent>(server).Points, Is.EqualTo(points + award));
                Assert.That(SEntMan.Deleted(sample), Is.EqualTo(award > 0), "An exhausted sample must remain in the scientist's hand.");
            }
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    private EntityUid InstantActor(EntityCoordinates coordinates)
    {
        var actor = SEntMan.SpawnEntity(null, coordinates);
        SEntMan.EnsureComponent<DoAfterComponent>(actor);
        var hands = SEntMan.EnsureComponent<HandsComponent>(actor);
        Server.System<SharedHandsSystem>().AddHand((actor, hands), "right", HandLocation.Right);
        SEntMan.EnsureComponent<TagComponent>(actor);
        Server.System<TagSystem>().AddTag(actor, new ProtoId<TagPrototype>("InstantDoAfters"));
        return actor;
    }

    [Test]
    public async Task HeldProductionDiskOpensWriterOnClientAndRecordsSelection()
    {
        var map = await Pair.CreateTestMap();
        NetEntity netDisk = default;
        var technology = new ProtoId<TechnologyPrototype>("IndustrialAutomation");
        await Server.WaitAssertion(() =>
        {
            var actor = InstantActor(map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, actor);
            SEntMan.EnsureComponent<AccessComponent>(actor).Tags.Add(new("Research"));
            var console = SEntMan.SpawnEntity("ComputerResearchAndDevelopment", map.GridCoords);
            var server = SEntMan.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
            foreach (var entity in new[] { console, server })
            {
                var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(entity);
                power.NeedsPower = false;
                power.Powered = true;
            }
            SEntMan.GetComponent<ResearchClientComponent>(console).Server = server;
            var research = Server.System<Content.Server.Research.Systems.ResearchSystem>();
            research.AddTechnology(server, technology.Id);
            var account = research.IssueCrewAccount(server, actor, "Scientist", "Factory operator");
            Assert.That(research.AuthenticateTerminal(console, actor, account.Username, account.Password, false), Is.True);

            var disk = SEntMan.SpawnEntity("IndustrialProductionDisk", map.GridCoords);
            netDisk = SEntMan.GetNetEntity(disk);
            Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(actor, disk), Is.True);
            SEntMan.EventBus.RaiseLocalEvent(console, new IndustrialOpenDiskWriterMessage { Actor = actor });
            Assert.That(Server.System<SharedUserInterfaceSystem>().IsUiOpen(disk, IndustrialDiskUiKey.Write, actor), Is.True);
        });

        await Pair.RunTicksSync(20);
        await Client.WaitAssertion(() =>
        {
            var disk = CEntMan.GetEntity(netDisk);
            var ui = Client.System<SharedUserInterfaceSystem>();
            Assert.That(ui.TryGetOpenUi<IndustrialDiskWriterBoundUserInterface>(disk, IndustrialDiskUiKey.Write, out var writer), Is.True);
            Assert.That(writer.IsOpened, Is.True);
            writer.SendMessage(new IndustrialWriteDiskMessage([technology]));
        });

        await Pair.RunTicksSync(20);
        await Server.WaitAssertion(() =>
        {
            var disk = SEntMan.GetEntity(netDisk);
            Assert.That(SEntMan.GetComponent<IndustrialDataDiskComponent>(disk).Technologies, Is.EquivalentTo(new[] { technology }));
            Assert.That(Server.System<SharedUserInterfaceSystem>().IsUiOpen(disk, IndustrialDiskUiKey.Write), Is.False);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, null);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task ManualCrusherRequiresOneCompletedActionPerCycle()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("IndustrialManualCrusher", map.GridCoords);
            var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
            comp.SelectedRecipe = new ProtoId<IndustrialRecipePrototype>("IndustrialCrushIngotCopper");
            var containers = Server.System<SharedContainerSystem>();
            var input = containers.GetContainer(uid, comp.InputContainerId);
            var output = containers.GetContainer(uid, comp.OutputContainerId);
            var ingots = SEntMan.SpawnEntity("IngotCopper1", map.GridCoords);
            Server.System<StackSystem>().SetCount((ingots, SEntMan.GetComponent<StackComponent>(ingots)), 2);
            Assert.That(containers.Insert(ingots, input), Is.True);
            var system = Server.System<IndustrialProcessorSystem>();
            system.Update(100);
            Assert.That(output.Count, Is.Zero, "A manual crusher cannot run unattended.");
            var actor = InstantActor(map.GridCoords);
            var manual = SEntMan.GetComponent<IndustrialManualProcessorComponent>(uid);
            Assert.That(system.StartManual((uid, manual), actor), Is.True);
            Assert.That(Server.System<IndustrialResourceSystem>().Count(output.ContainedEntities, "CopperDust"), Is.EqualTo(1));
            Assert.That(manual.Busy, Is.False, "Completion must release the operator lock, including instantaneous do-afters.");
            Assert.That(system.StartManual((uid, manual), actor), Is.True);
            Assert.That(Server.System<IndustrialResourceSystem>().Count(output.ContainedEntities, "CopperDust"), Is.EqualTo(2));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task InstalledResearchSurvivesDiskRemovalAndDirtyRoomPausesChipProduction()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("BasicIndustrialAssembler", map.GridCoords);
            var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
            comp.CanWorkWithoutPower = true;
            comp.SelectedRecipe = new ProtoId<IndustrialRecipePrototype>("AutoFabricateIndustrialCircuitCore");
            var room = SEntMan.GetComponent<RoomSanitationComponent>(uid);
            room.Grade = 1;
            var containers = Server.System<SharedContainerSystem>();
            var input = containers.GetContainer(uid, comp.InputContainerId);
            var output = containers.GetContainer(uid, comp.OutputContainerId);
            foreach (var (id, amount) in new[] { ("IndustrialSiliconDioxide1", 2), ("IndustrialPrintedBoard1", 1) })
            {
                var item = SEntMan.SpawnEntity(id, map.GridCoords);
                Server.System<StackSystem>().SetCount((item, SEntMan.GetComponent<StackComponent>(item)), amount);
                Assert.That(containers.Insert(item, input), Is.True);
            }
            var processors = Server.System<IndustrialProcessorSystem>();
            processors.Update(30);
            Assert.That(output.Count, Is.Zero, "Even a clean powered assembler needs science data.");
            var disk = SEntMan.SpawnEntity("IndustrialProductionDisk", map.GridCoords);
            var data = SEntMan.GetComponent<IndustrialDataDiskComponent>(disk);
            data.Technologies.Add(new("IndustrialElectronics"));
            Server.System<IndustrialProgressionSystem>().InstallDisk(uid, data);
            data.Technologies.Clear();
            SEntMan.DeleteEntity(disk);
            processors.Update(1);
            Assert.That(comp.IsWorking, Is.True);
            room.Grade = 0;
            processors.Update(30);
            Assert.That(output.Count, Is.Zero);
            Assert.That(input.Count, Is.EqualTo(2), "Loss of cleanliness cannot spend ingredients or produce defective free outputs.");
            room.Grade = 1;
            processors.Update(30);
            Assert.That(Server.System<IndustrialResourceSystem>().Count(output.ContainedEntities, "IndustrialCircuitCore"), Is.EqualTo(2));
            Assert.That(SEntMan.GetComponent<IndustrialMachineKnowledgeComponent>(uid).Technologies.Contains(new("IndustrialElectronics")), Is.True);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task PassableFloorVeinHasOneFiniteReserveForManualAndAutomaticMining()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var vein = SEntMan.SpawnEntity("IndustrialDepositCopper", map.GridCoords);
            Assert.That(SEntMan.HasComponent<Robust.Shared.Physics.Components.PhysicsComponent>(vein), Is.False,
                "A vein is an overlay in the floor, never an impassable wall or block.");
            var stock = SEntMan.GetComponent<IndustrialResourceDepositComponent>(vein);
            stock.Remaining = 3;
            var actor = InstantActor(map.GridCoords);
            var pick = SEntMan.SpawnEntity("Pickaxe", map.GridCoords);
            Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(actor, pick), Is.True);
            SEntMan.EventBus.RaiseLocalEvent(vein, new InteractUsingEvent(actor, pick, vein, map.GridCoords));
            Assert.That(stock.Remaining, Is.EqualTo(2));
            var drill = SEntMan.SpawnEntity("IndustrialDrill", map.GridCoords);
            Server.System<SharedTransformSystem>().AnchorEntity(drill);
            var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(drill);
            power.Powered = true;
            var processor = SEntMan.GetComponent<IndustrialProcessorComponent>(drill);
            var extractor = SEntMan.GetComponent<IndustrialExtractorComponent>(drill);
            var extraction = Server.System<IndustrialExtractionSystem>();
            extraction.Update(10);
            Assert.That(extractor.State, Is.EqualTo(IndustrialProcessorState.Paused));
            Assert.That(stock.Remaining, Is.EqualTo(2));
            SEntMan.EventBus.RaiseLocalEvent(drill, new IndustrialProcessorToggleMessage { Actor = actor });
            extraction.Update(10);
            Assert.That(extractor.State, Is.EqualTo(IndustrialProcessorState.Unresearched));
            Assert.That(stock.Remaining, Is.EqualTo(2));

            var disk = SEntMan.SpawnEntity("IndustrialProductionDisk", map.GridCoords);
            var data = SEntMan.GetComponent<IndustrialDataDiskComponent>(disk);
            data.Technologies.Add(new("IndustrialAutomation"));
            Server.System<IndustrialProgressionSystem>().InstallDisk(drill, data);
            SEntMan.DeleteEntity(disk);

            power.Powered = false;
            extraction.Update(10);
            Assert.That(extractor.State, Is.EqualTo(IndustrialProcessorState.Unpowered));
            Assert.That(power.Load, Is.EqualTo(processor.BasePowerLoad), "Keep requesting enough power to drill when the network falls short.");
            Assert.That(stock.Remaining, Is.EqualTo(2));

            power.Powered = true;
            processor.MaxOutputSlots = 0;
            extraction.Update(10);
            Assert.That(extractor.State, Is.EqualTo(IndustrialProcessorState.Blocked));
            Assert.That(stock.Remaining, Is.EqualTo(2), "A blocked extractor must not consume the reserve.");
            processor.MaxOutputSlots = 4;
            extraction.Update(2);
            Assert.That(extractor.State, Is.EqualTo(IndustrialProcessorState.Working));
            Assert.That(extractor.Elapsed, Is.EqualTo(2));
            Assert.That(stock.Remaining, Is.EqualTo(2));
            SEntMan.EventBus.RaiseLocalEvent(drill, new IndustrialProcessorToggleMessage { Actor = actor });
            extraction.Update(10);
            Assert.That(extractor.State, Is.EqualTo(IndustrialProcessorState.Paused));
            Assert.That(extractor.Elapsed, Is.EqualTo(2));
            SEntMan.EventBus.RaiseLocalEvent(drill, new IndustrialProcessorToggleMessage { Actor = actor });
            extraction.Update(3);
            Assert.That(extractor.Elapsed, Is.Zero);
            extraction.Update(1000);
            var output = Server.System<SharedContainerSystem>().GetContainer(drill, processor.OutputContainerId);
            Assert.That(stock.Remaining, Is.Zero);
            Assert.That(Server.System<IndustrialResourceSystem>().Count(output.ContainedEntities, "CopperOre"), Is.EqualTo(2));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task CleanroomTopologyRejectsOpenDoorsAndLimitsTheScan()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            EntityUid door = default;
            for (var x = 0; x < 5; x++)
            for (var y = 0; y < 5; y++)
            {
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                if (x != 0 && y != 0 && x != 4 && y != 4)
                    continue;
                var wall = SEntMan.SpawnEntity("WallSolid", maps.GridTileToLocal(map.Grid, map.Grid.Comp, new Vector2i(x, y)));
                if (x == 2 && y == 0) door = wall;
            }
            var rooms = Server.System<RoomSanitationSystem>();
            Assert.That(rooms.TryGetRoom(map.Grid, map.Grid.Comp, new Vector2i(2, 2), 64, out var cells), Is.True);
            Assert.That(cells.Count, Is.EqualTo(9));
            Assert.That(rooms.TryGetRoom(map.Grid, map.Grid.Comp, new Vector2i(2, 2), 5, out _), Is.False);
            SEntMan.DeleteEntity(door);
            Assert.That(rooms.TryGetRoom(map.Grid, map.Grid.Comp, new Vector2i(2, 2), 64, out _), Is.False,
                "Opening the room into the outside cannot retain a sterile certificate.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
