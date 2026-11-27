using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._Erida.Research.UI;
using Content.Shared._Erida.Research;
using Content.Shared.Research.Prototypes;
using Content.Shared.Research.Components;
using IndustrialProcessorWindow = Content.Client._Eclipse.Industrial.IndustrialProcessorWindow;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Industrial;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Temperature.Systems;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Content.Shared.Stacks;
using Content.Server.Stack;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Shared.Temperature.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class IndustrialControlAndHeatTest : GameTest
{
    [Test]
    public async Task PowerAppearanceChangesWhileTheMachineStaysManuallyPaused()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("BasicIndustrialCrusher", map.GridCoords);
            var processor = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
            processor.Enabled = false;
            var receiver = SEntMan.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(uid);
            var appearance = Server.System<SharedAppearanceSystem>();
            foreach (var powered in new[] { true, false, true })
            {
                receiver.Powered = powered;
                var change = new Content.Shared.Power.PowerChangedEvent(powered, powered ? 50 : 0);
                SEntMan.EventBus.RaiseLocalEvent(uid, ref change);
                Assert.That(processor.LastState, Is.EqualTo(IndustrialProcessorState.Paused));
                Assert.That(appearance.TryGetData<bool>(uid, IndustrialProcessorVisuals.Powered, out var visualPower));
                Assert.That(visualPower, Is.EqualTo(powered), "The cached process state must not hide power changes.");
            }
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task ResearchUnlocksAnIndustrialRecipeOnTheExistingDatabase()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var machine = SEntMan.SpawnEntity("BasicIndustrialAssembler", map.GridCoords);
            var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(machine);
            comp.CanWorkWithoutPower = true;
            SEntMan.EventBus.RaiseLocalEvent(machine,
                new IndustrialProcessorRecipeMessage("IndustrialAdvancedHighPowerCells") { Actor = machine });
            Assert.That(comp.SelectedRecipe, Is.Null, "The server must reject an unresearched recipe selection.");
            Server.System<Content.Server.Research.Systems.ResearchSystem>().AddTechnology(machine, "IndustrialEnergyStorage");
            SEntMan.GetComponent<IndustrialMachineKnowledgeComponent>(machine).Technologies.Add(new("IndustrialEnergyStorage"));
            SEntMan.EventBus.RaiseLocalEvent(machine,
                new IndustrialProcessorRecipeMessage("IndustrialAdvancedHighPowerCells") { Actor = machine });
            Assert.That(comp.SelectedRecipe, Is.Null, "Research must not bypass the minimum machine tier.");
            Server.System<IndustrialProcessorSystem>().UpgradeMachine((machine, comp), MachineTier.Industrial,
                new EntProtoId("IndustrialAssembler"));
            SEntMan.EventBus.RaiseLocalEvent(machine,
                new IndustrialProcessorRecipeMessage("IndustrialAdvancedHighPowerCells") { Actor = machine });
            Assert.That(comp.SelectedRecipe, Is.Not.Null);
            var containers = Server.System<SharedContainerSystem>();
            var input = containers.GetContainer(machine, comp.InputContainerId);
            foreach (var (prototype, amount) in new[] { ("IndustrialAdvancedBoard1", 1), ("SheetSteel1", 2), ("SheetGlass1", 2), ("SheetCopper1", 2) })
            {
                var item = SEntMan.SpawnEntity(prototype, map.GridCoords);
                Server.System<StackSystem>().SetCount((item, SEntMan.GetComponent<StackComponent>(item)), amount);
                Assert.That(containers.Insert(item, input), Is.True);
            }
            Server.System<IndustrialProcessorSystem>().Update(20);
            var output = containers.GetContainer(machine, comp.OutputContainerId);
            Assert.That(output.Count, Is.EqualTo(1));
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(output.ContainedEntities[0]).EntityPrototype?.ID,
                Is.EqualTo("PowerCellHighPrinted"));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [TestCase("IndustrialWorkbench")]
    [TestCase("IndustrialWorkbenchTier2")]
    public async Task WorkbenchBatchConsumesAggregatedMaterialsAndHonoursQuantity(string prototype)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var bench = SEntMan.SpawnEntity(prototype, map.GridCoords);
            var actor = SEntMan.SpawnEntity(null, map.GridCoords);
            SEntMan.EnsureComponent<DoAfterComponent>(actor);
            SEntMan.EnsureComponent<HandsComponent>(actor);
            SEntMan.EnsureComponent<TagComponent>(actor);
            ProtoId<TagPrototype> instant = "InstantDoAfters";
            Server.System<TagSystem>().AddTag(actor, instant);
            var storage = SEntMan.GetComponent<StorageComponent>(bench);
            var containers = Server.System<SharedContainerSystem>();
            void Insert(string prototype, int amount)
            {
                var item = SEntMan.SpawnEntity(prototype, map.GridCoords);
                Server.System<StackSystem>().SetCount((item, SEntMan.GetComponent<StackComponent>(item)), amount);
                Assert.That(containers.Insert(item, storage.Container), Is.True);
            }
            Insert("IndustrialIronPlate1", 3);
            Insert("IndustrialIronPlate", 5);
            Insert("IndustrialIronRod1", 4);
            var resources = Server.System<IndustrialResourceSystem>();
            SEntMan.EventBus.RaiseLocalEvent(bench, new IndustrialWorkbenchCraftBuiMessage("FabricateIndustrialMechanicalCore", 0) { Actor = actor });
            Assert.That(resources.Count(storage.Container.ContainedEntities, "IndustrialIronPlate"), Is.EqualTo(8));
            SEntMan.EventBus.RaiseLocalEvent(bench, new IndustrialWorkbenchCraftBuiMessage("FabricateIndustrialMechanicalCore", 2) { Actor = actor });
            Assert.That(resources.Count(storage.Container.ContainedEntities, "IndustrialIronPlate"), Is.Zero);
            Assert.That(resources.Count(storage.Container.ContainedEntities, "IndustrialIronRod"), Is.Zero);
            var count = 0;
            var query = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out _, out var meta, out var transform))
            {
                if (transform.GridUid == map.Grid.Owner && meta.EntityPrototype?.ID == "IndustrialMechanicalCore")
                    count++;
            }
            Assert.That(count, Is.EqualTo(2));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [TestCase("IndustrialWorkbench", false)]
    [TestCase("IndustrialWorkbenchTier2", true)]
    public async Task PerfectModuleCraftCannotBypassWorkbenchTier(string prototype, bool allowed)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var bench = SEntMan.SpawnEntity(prototype, map.GridCoords);
            SEntMan.GetComponent<IndustrialMachineKnowledgeComponent>(bench).Technologies.Add(new("IndustrialPerfectAutomation"));
            var actor = SEntMan.SpawnEntity(null, map.GridCoords);
            SEntMan.EnsureComponent<DoAfterComponent>(actor);
            SEntMan.EnsureComponent<HandsComponent>(actor);
            SEntMan.EnsureComponent<TagComponent>(actor);
            Server.System<TagSystem>().AddTag(actor, new ProtoId<TagPrototype>("InstantDoAfters"));
            var storage = SEntMan.GetComponent<StorageComponent>(bench);
            var containers = Server.System<SharedContainerSystem>();
            foreach (var (ingredient, quantity) in new[]
                     { ("IndustrialUpgradeModuleCrusherIndustrial", 1), ("IndustrialPerfectBoard1", 2), ("IndustrialAlloyBlank1", 2) })
            {
                for (var i = 0; i < quantity; i++)
                    Assert.That(containers.Insert(SEntMan.SpawnEntity(ingredient, map.GridCoords), storage.Container), Is.True);
            }
            var plasteel = SEntMan.SpawnEntity("SheetPlasteel1", map.GridCoords);
            Server.System<StackSystem>().SetCount((plasteel, SEntMan.GetComponent<StackComponent>(plasteel)), 8);
            Assert.That(containers.Insert(plasteel, storage.Container), Is.True);
            SEntMan.EventBus.RaiseLocalEvent(bench,
                new IndustrialWorkbenchCraftBuiMessage("FabricateUpgradeModuleCrusherPerfect", 1) { Actor = actor });
            var resources = Server.System<IndustrialResourceSystem>();
            Assert.That(resources.Count(storage.Container.ContainedEntities, "IndustrialPerfectBoard"), Is.EqualTo(allowed ? 0 : 2));
            Assert.That(resources.Count(storage.Container.ContainedEntities, "IndustrialAlloyBlank"), Is.EqualTo(allowed ? 0 : 2));
            Assert.That(resources.Count(storage.Container.ContainedEntities, "SheetPlasteel"), Is.EqualTo(allowed ? 0 : 8));
            Assert.That(resources.Count(storage.Container.ContainedEntities, "IndustrialUpgradeModuleCrusherIndustrial"), Is.EqualTo(allowed ? 0 : 1));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task EveryImportedMachineSpriteLoadsItsProductionStates()
    {
        await Client.WaitAssertion(() =>
        {
            var cache = Client.ResolveDependency<IResourceCache>();
            foreach (var name in new[] { "crusher", "centrifuge", "smelter", "assembler", "buffer", "heat_buffer", "workbench" })
            {
                var resource = cache.GetResource<RSIResource>(new ResPath($"/Textures/_Eclipse/Structures/Machines/industrial_{name}.rsi"));
                Assert.That(resource.RSI.TryGetState("icon", out _), Is.True, name);
                Assert.That(resource.RSI.TryGetState("building", out _), Is.True, name);
                if (name is not ("heat_buffer" or "workbench"))
                    Assert.That(resource.RSI.TryGetState("paused", out _), Is.True, name);
            }
        });
    }

    [Test]
    public async Task ColdPlasmaCannotCreateHeatAndStoredHeatIsConserved()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            var buffer = SEntMan.SpawnEntity("IndustrialHeatBuffer", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            var machine = SEntMan.SpawnEntity("BasicIndustrialThermalCentrifuge", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            if (!SEntMan.GetComponent<TransformComponent>(buffer).Anchored)
                Server.System<SharedTransformSystem>().AnchorEntity(buffer);
            SEntMan.GetComponent<IndustrialProcessorComponent>(machine).SetFacePort(Direction.West, FacePortState.HeatInput);
            var nodes = Server.System<NodeContainerSystem>();
            Assert.That(nodes.TryGetNodes(buffer, "inlet", "outlet", out PipeNode inlet, out PipeNode outlet), Is.True);
            inlet.Air.SetMoles(Gas.Plasma, 1);
            inlet.Air.Temperature = 293.15f;
            var temperature = Server.System<TemperatureSystem>();
            temperature.ForceChangeTemperature(buffer, 293.15f);
            temperature.ForceChangeTemperature(machine, 293.15f);
            var update = new AtmosDeviceUpdateEvent(1f, null, null);
            SEntMan.EventBus.RaiseLocalEvent(buffer, ref update);
            Assert.That(SEntMan.GetComponent<TemperatureComponent>(buffer).CurrentTemperature, Is.EqualTo(293.15f).Within(0.01f));
            Assert.That(SEntMan.GetComponent<TemperatureComponent>(machine).CurrentTemperature, Is.EqualTo(293.15f).Within(0.01f));
            SEntMan.EventBus.RaiseLocalEvent(buffer, ref update);
            Assert.That(SEntMan.GetComponent<IndustrialHeatBufferComponent>(buffer).PlasmaFlowing, Is.False,
                "Plasma left in the outlet is not a continuing inlet flow.");

            temperature.ForceChangeTemperature(buffer, 900);
            var body = SEntMan.GetComponent<TemperatureComponent>(buffer);
            var target = SEntMan.GetComponent<TemperatureComponent>(machine);
            var bufferCapacity = temperature.GetHeatCapacity(buffer, body);
            var machineCapacity = temperature.GetHeatCapacity(machine, target);
            var before = (double) bufferCapacity * body.CurrentTemperature + (double) machineCapacity * target.CurrentTemperature;
            SEntMan.EventBus.RaiseLocalEvent(buffer, ref update);
            var after = (double) bufferCapacity * body.CurrentTemperature + (double) machineCapacity * target.CurrentTemperature;
            Assert.That(target.CurrentTemperature, Is.GreaterThan(293.15f));
            Assert.That(body.CurrentTemperature, Is.LessThan(900));
            Assert.That(after, Is.EqualTo(before).Within(Math.Max(1, Math.Abs(before) * 0.000001)));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task InvalidSlotRequestsAndPauseDoNotConsumeAnything()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("BasicIndustrialCrusher", map.GridCoords);
            var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
            comp.CanWorkWithoutPower = true;
            var containers = Server.System<SharedContainerSystem>();
            var input = containers.GetContainer(uid, comp.InputContainerId);
            var ore = SEntMan.SpawnEntity("SteelOre1", map.GridCoords);
            containers.Insert(ore, input);
            var negative = new IndustrialProcessorSlotMessage(true, -1) { Actor = uid };
            var tooLarge = new IndustrialProcessorSlotMessage(true, int.MaxValue) { Actor = uid };
            Assert.DoesNotThrow(() => SEntMan.EventBus.RaiseLocalEvent(uid, negative));
            Assert.DoesNotThrow(() => SEntMan.EventBus.RaiseLocalEvent(uid, tooLarge));
            SEntMan.EventBus.RaiseLocalEvent(uid, new IndustrialProcessorToggleMessage { Actor = uid });
            var processor = Server.System<IndustrialProcessorSystem>();
            processor.Update(30);
            Assert.That(input.Contains(ore), Is.True);
            Assert.That(comp.ProcessingAccumulator, Is.Zero);
            SEntMan.EventBus.RaiseLocalEvent(uid, new IndustrialProcessorToggleMessage { Actor = uid });
            processor.Update(10);
            Assert.That(input.Count, Is.Zero);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task UpgradingAnExistingMachinePreservesInventoryAndPorts()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("BasicIndustrialCrusher", map.GridCoords);
            var comp = SEntMan.GetComponent<IndustrialProcessorComponent>(uid);
            comp.AutoStart = false;
            comp.SetFacePort(Direction.West, FacePortState.ItemInput);
            comp.SetFacePort(Direction.East, FacePortState.ItemOutput);
            var containers = Server.System<SharedContainerSystem>();
            var input = containers.GetContainer(uid, comp.InputContainerId);
            var ore = SEntMan.SpawnEntity("SteelOre1", map.GridCoords);
            Assert.That(containers.Insert(ore, input), Is.True);
            var module = SEntMan.SpawnEntity("IndustrialUpgradeModuleCrusherIndustrial", map.GridCoords);
            Server.System<IndustrialMachineAssemblySystem>().TryUpgradeMachine((uid, comp), uid, module,
                SEntMan.GetComponent<IndustrialUpgradeModuleComponent>(module));
            Assert.That(comp.Tier, Is.EqualTo(MachineTier.Industrial));
            Assert.That(comp.MaxInputSlots, Is.EqualTo(8));
            Assert.That(input.Contains(ore), Is.True);
            Assert.That(comp.WestFacePort, Is.EqualTo(FacePortState.ItemInput));
            Assert.That(comp.EastFacePort, Is.EqualTo(FacePortState.ItemOutput));
            Assert.That(SEntMan.Deleted(module), Is.True);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task MachineWindowHandlesFullBuffersAndStorageFilters()
    {
        await Client.WaitAssertion(() =>
        {
            var slots = new IndustrialProcessorSlotState[16];
            for (var i = 0; i < slots.Length; i++)
                slots[i] = new IndustrialProcessorSlotState("SteelOre1", i + 1, "Iron ore");
            const string longRecipe = "Обогащение измельчённой руды и производство промышленных компонентов повышенной эффективности";
            var state = new IndustrialProcessorBoundUserInterfaceState("Совершенная промышленная термическая центрифуга", "Совершенный",
                "industrial-processor-working", 0.4f, longRecipe, true, false, false,
                16, 16, slots, slots, slots[0], FacePortState.ItemInput, FacePortState.ItemOutput,
                FacePortState.Disabled, FacePortState.Disabled)
            {
                SecondsRemaining = 3,
                SpeedMultiplier = 2.75f,
                SelectedRecipeId = "CrushSteelOre",
                Recipes =
                [
                    new IndustrialRecipeUiState("Locked", "A locked recipe", 5, false, [slots[0]], [slots[1]]),
                    new IndustrialRecipeUiState("CrushSteelOre", longRecipe, 5, true, [slots[0]], [slots[1]]),
                    new IndustrialRecipeUiState("Available", "Автосборка", 5, true, [], []),
                ],
            };
            using var window = new IndustrialProcessorWindow();
            window.OpenCentered();
            Assert.DoesNotThrow(() => window.UpdateState(state));
            Assert.That(Find<ProgressBar>(window, "CycleProgress").GetAsRatio(), Is.EqualTo(0.4f).Within(0.001f),
                "A fractional cycle state must fill the bar to the same fraction.");
            foreach (var size in new[] { new Vector2(800, 520), new Vector2(880, 660), new Vector2(1040, 800) })
            {
                window.SetSize = size;
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                var input = Find<PanelContainer>(window, "InputBuffer");
                var output = Find<PanelContainer>(window, "OutputBuffer");
                Assert.That(input.Width, Is.EqualTo(output.Width).Within(1), "Buffers must share the process board equally.");
                Assert.That(Find<ContainerButton>(window, "InputSlot0").ActualModulateSelf, Is.EqualTo(Color.White),
                    "The station theme must not tint slot surfaces black.");
                Assert.That(Find<BoxContainer>(window, "MachineConnections").GlobalRect.Bottom, Is.LessThanOrEqualTo(size.Y),
                    "Connection controls must remain accessible without scrolling the process.");
                foreach (var control in VisibleChildren(window))
                {
                    Assert.That(control.GlobalRect.Left, Is.GreaterThanOrEqualTo(-1), control.Name);
                    Assert.That(control.GlobalRect.Right, Is.LessThanOrEqualTo(size.X + 1),
                        $"{control.GetType().Name} {control.Name} overflows at {size}.");
                }
                Assert.That(Find<ScrollContainer>(window, "ProductionScroll").HScrollEnabled, Is.False);
                Assert.That(Find<ScrollContainer>(window, "RecipeScroll").HScrollEnabled, Is.False);
            }
            var search = Find<LineEdit>(window, "RecipeSearch");
            var recipeButton = Find<ContainerButton>(window, "Recipe-CrushSteelOre");
            var availableOnly = Find<CheckBox>(window, "AvailableRecipesOnly");
            var lockedRecipe = Find<ContainerButton>(window, "Recipe-Locked");
            Assert.That(availableOnly.Pressed, Is.False);
            Assert.That(lockedRecipe.Visible, Is.True);
            Assert.That(recipeButton.Parent!.Children.Where(c => c.Visible).Select(c => c.Name),
                Is.EqualTo(new[] { "Recipe-Available", "Recipe-CrushSteelOre", "Recipe-Locked" }));
            search.SetText("locked", true);
            Assert.That(lockedRecipe.Visible, Is.True);
            availableOnly.Pressed = true;
            search.SetText("Locked", true);
            Assert.That(lockedRecipe.Visible, Is.False);
            availableOnly.Pressed = false;
            search.SetText("iron ore", true);
            Assert.That(recipeButton.Parent.Children.Where(c => c.Visible).Select(c => c.Name),
                Is.EqualTo(new[] { "Recipe-CrushSteelOre", "Recipe-Locked" }));
            search.SetText("missing recipe", true);
            Assert.That(recipeButton.Visible, Is.False);
            search.SetText("ОБОГАЩЕНИЕ", true);
            Assert.That(recipeButton.Visible, Is.True, "Recipe filtering must ignore case for Russian names.");
            search.SetText("IRON ORE", true);
            Assert.That(recipeButton.Visible, Is.True, "Recipes must also be searchable by their ingredients.");
            var automatic = Find<Button>(window, "AutomaticRecipe");
            Assert.That(automatic.Label.TryGetStyleProperty<Robust.Client.Graphics.Font>("font", out _), Is.True,
                "The industrial stylesheet must preserve the station's text fonts.");
            state.IsBuffer = true;
            state.SelectedRecipeId = "CrushSteelOre";
            Assert.DoesNotThrow(() => window.UpdateState(state));
            window.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(800, 520)));
            Assert.That(Find<PanelContainer>(window, "OutputBuffer").Visible, Is.False);
            Assert.That(Find<Button>(window, "PauseButton").Visible, Is.False);
            var bufferScroll = Find<ScrollContainer>(window, "ProductionScroll");
            foreach (var control in VisibleChildren(bufferScroll))
                Assert.That(control.GlobalRect.Right, Is.LessThanOrEqualTo(bufferScroll.GlobalRect.Right + 1), control.Name);
            state.Recipes = [new IndustrialRecipeUiState("CrushSteelOre", longRecipe, 5, false, [slots[0]], [slots[1]])];
            state.MaxInputSlots = state.MaxOutputSlots = 4;
            state.InputSlots = state.OutputSlots = slots[..4];
            window.UpdateState(state);
            window.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(800, 520)));
            Assert.That(search.Text, Is.EqualTo("IRON ORE"), "Catalogue updates must preserve the search.");
            Assert.That(VisibleChildren(window).Count(c => c.Name == "Recipe-CrushSteelOre"), Is.EqualTo(1),
                "Changing recipe availability must replace its row without leaving duplicates.");
            Assert.That(VisibleChildren(window).Count(c => c.Name?.StartsWith("InputSlot") == true), Is.EqualTo(4));

            state.IsBuffer = false;
            state.IsExtractor = true;
            state.Enabled = false;
            state.Recipes = [];
            state.CurrentRecipeName = null;
            state.StateKey = "industrial-extraction-stopped";
            window.UpdateState(state);
            Assert.That(Find<Button>(window, "PauseButton").Text, Is.EqualTo(Loc.GetString("industrial-extraction-start")));
            Assert.That(VisibleChildren(window).Any(c => c.Name == "CurrentRecipe"), Is.False);

            state.Enabled = true;
            state.StateKey = "industrial-processor-working";
            state.CurrentRecipeName = Loc.GetString("industrial-extraction-running");
            window.UpdateState(state);
            Assert.That(Find<Button>(window, "PauseButton").Text, Is.EqualTo(Loc.GetString("industrial-extraction-stop")));
            Assert.That(Find<ProgressBar>(window, "CycleProgress").Visible, Is.True);
            Assert.That(Find<ProgressBar>(window, "CycleProgress").GetAsRatio(), Is.EqualTo(0.4f).Within(0.001f));
            window.Close();
        });
    }

    [Test]
    public async Task ResearchTerminalKeepsControlsAccessibleAndFitsFactoryAndAllResearchGraphs()
    {
        try
        {
            await Client.WaitAssertion(() =>
            {
                var technologies = CProtoMan.EnumeratePrototypes<TechnologyPrototype>()
                    .Where(x => !x.Hidden)
                    .ToDictionary(x => x.ID, _ => ResearchAvailability.Unavailable);
                technologies["IndustrialElectronics"] = ResearchAvailability.Researched;
                technologies["IndustrialAutomation"] = ResearchAvailability.Available;
                var console = CEntMan.SpawnEntity("ComputerResearchAndDevelopment", MapCoordinates.Nullspace);
                var window = new FancyResearchConsoleMenu();
                window.SetEntity(console);
                window.UpdateTerminalState(new ResearchTerminalState { Username = "review", CanResearch = true, ServerName = "НИО" });
                window.UpdatePanels(technologies);
                window.UpdateInformationPanel(12000);
                window.OpenCentered();
                foreach (var section in new[] { 1, 0 })
                foreach (var size in new[] { new Vector2(1000, 660), new Vector2(1280, 800) })
                {
                    window.SelectResearchSection(section);
                    window.SetSize = size;
                    window.Measure(size);
                    window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    window.Recenter();
                    foreach (var name in new[] { "WriteDisk", "OverviewButton", "ServerButton", "CrtEffects", "ZoomIn", "ZoomOut" })
                    {
                        var button = Find<Button>(window, name);
                        Assert.That(button.GlobalRect.Right, Is.LessThanOrEqualTo(size.X + 1), name);
                        Assert.That(button.GlobalRect.Bottom, Is.LessThanOrEqualTo(size.Y + 1), name);
                    }
                    var graph = Find<ResearchesContainerPanel>(window, "DragContainer");
                    Assert.That(graph.Width, Is.GreaterThan(350), "The details panel must leave space to navigate the map.");
                    Assert.That(Find<Button>(window, "ZoomIn").GlobalRect.Right,
                        Is.LessThanOrEqualTo(Find<BoxContainer>(window, "InfoContainer").GlobalRect.Left), "Zoom controls must not overlap the dossier.");
                    window.FitGraph();
                    // The headless test does not run UI render frames. Flush descendant layout
                    // invalidations that the normal UI frame queue processes before rendering.
                    InvalidateTree(window);
                    window.Measure(size);
                    window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    Assert.That(graph.GraphZoom, Is.GreaterThanOrEqualTo(.55f), "Overview must retain readable cards instead of collapsing to dots.");
                    var selected = graph.Children.OfType<FancyResearchConsoleItem>().First(n => n.Prototype.ID == window.CurrentTech);
                    window.Recenter();
                    Assert.That(selected.MinSize.X, Is.GreaterThanOrEqualTo(120));
                    Assert.That(Find<BoxContainer>(selected, "SummaryContents").Visible || Find<BoxContainer>(selected, "CardContents").Visible, Is.True);
                }
                window.SetGraphZoom(.85f);
                var researchGraph = Find<ResearchesContainerPanel>(window, "DragContainer");
                var boundaryNode = researchGraph.Children.OfType<FancyResearchConsoleItem>().First();
                var boundaryPosition = boundaryNode.PlaceInViewport(new Vector2(-50, 50), researchGraph.Size);
                Assert.That(boundaryPosition, Is.EqualTo(new Vector2(-50, 50)), "Panning across an edge must not move a card onto its neighbours.");
                Assert.That(boundaryNode.IsClipped, Is.False);
                Assert.That(Find<BoxContainer>(boundaryNode, "CardContents").Visible, Is.True);
                window.SetGraphZoom(.65f);
                boundaryNode.PlaceInViewport(new Vector2(50, 50), researchGraph.Size);
                Assert.That(Find<BoxContainer>(boundaryNode, "SummaryContents").Visible, Is.True, "One zoom-out step must retain readable research names.");
                Assert.That(Find<BoxContainer>(boundaryNode, "CardContents").Visible, Is.False, "Medium-scale nodes must not shrink item sprites into noise.");
                Assert.That(Find<Label>(boundaryNode, "SummaryLine1").Text, Is.Not.Empty);
                var search = Find<LineEdit>(window, "Search");
                search.SetText("нет такого исследования", true);
                Assert.That(Find<ResearchesContainerPanel>(window, "DragContainer").ChildCount, Is.Zero);
                search.SetText("", true);
                Assert.That(Find<ResearchesContainerPanel>(window, "DragContainer").ChildCount, Is.EqualTo(technologies.Count));
                Assert.That(window.CurrentTech, Is.Not.Null, "The terminal must select a visible technology after filtering.");
                window.Close();
                CEntMan.DeleteEntity(console);
            });
        }
        catch (Exception error) { TestContext.Error.WriteLine(error); Assert.Fail(error.ToString()); }
    }

    private static void InvalidateTree(Control control)
    {
        foreach (var child in control.Children) InvalidateTree(child);
        control.InvalidateMeasure();
        control.InvalidateArrange();
    }

    private static T Find<T>(Control parent, string name) where T : Control
    {
        foreach (var child in parent.Children)
        {
            if (child.Name == name)
                return (T) child;
            if (Find<T>(child, name) is { } found)
                return found;
        }
        return null!;
    }

    private static IEnumerable<Control> VisibleChildren(Control parent)
    {
        foreach (var child in parent.Children.Where(c => c.Visible))
        {
            yield return child;
            foreach (var descendant in VisibleChildren(child))
                yield return descendant;
        }
    }
}
