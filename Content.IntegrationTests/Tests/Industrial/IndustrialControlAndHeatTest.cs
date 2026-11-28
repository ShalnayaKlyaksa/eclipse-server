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
using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Temperature.Systems;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Binary.Components;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Content.Shared.Stacks;
using Content.Server.Stack;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Shared.Temperature.Components;
using Content.Shared.Verbs;
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
    public async Task HeatBufferSpriteKeepsItsBodyAndShowsBothGasConnectors()
    {
        await Client.WaitAssertion(() =>
        {
            var buffer = CEntMan.SpawnEntity("IndustrialHeatBuffer", MapCoordinates.Nullspace);
            var sprite = CEntMan.GetComponent<SpriteComponent>(buffer);
            var sprites = Client.System<SpriteSystem>();
            Assert.That(sprites.LayerGetRsiState((buffer, sprite), 0).ToString(), Is.EqualTo("icon"));
            Assert.That(sprite.AllLayers.Count(), Is.EqualTo(3));
            Assert.That(sprites.LayerGetRsiState((buffer, sprite), 1).ToString(), Is.EqualTo("pipeConnector"));
            Assert.That(sprites.LayerGetRsiState((buffer, sprite), 2).ToString(), Is.EqualTo("pipeConnector"));
            Assert.That(sprite.NoRotation, Is.True);
            Assert.That(sprite[1].Rotation, Is.EqualTo(Angle.FromDegrees(90)));
            Assert.That(sprite[2].Rotation, Is.EqualTo(Angle.FromDegrees(-90)));
            CEntMan.DeleteEntity(buffer);
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
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(-1, 0), map.Tile.Tile);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(0, 1), map.Tile.Tile);
            var buffer = SEntMan.SpawnEntity("IndustrialHeatBuffer", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            var machine = SEntMan.SpawnEntity("BasicIndustrialThermalCentrifuge", new EntityCoordinates(map.Grid, 0.5f, 1.5f));
            if (!SEntMan.GetComponent<TransformComponent>(buffer).Anchored)
                Server.System<SharedTransformSystem>().AnchorEntity(buffer);
            SEntMan.GetComponent<IndustrialProcessorComponent>(machine).SetFacePort(Direction.South, FacePortState.HeatInput);
            SEntMan.SpawnEntity("GasPipeFourway", new EntityCoordinates(map.Grid, -0.5f, 0.5f));
            var networks = Server.System<NodeGroupSystem>();
            networks.ForceUpdate();
            var nodes = Server.System<NodeContainerSystem>();
            Assert.That(nodes.TryGetNodes(buffer, "inlet", "outlet", out PipeNode inlet, out PipeNode outlet), Is.True);
            inlet.Air.SetMoles(Gas.Plasma, 1);
            inlet.Air.Temperature = 293.15f;
            var temperature = Server.System<TemperatureSystem>();
            temperature.ForceChangeTemperature(buffer, 293.15f);
            temperature.ForceChangeTemperature(machine, 293.15f);
            var update = new AtmosDeviceUpdateEvent(1f, null, null);
            SEntMan.EventBus.RaiseLocalEvent(buffer, ref update);
            Assert.That(inlet.Air.GetMoles(Gas.Plasma), Is.EqualTo(1), "An open outlet must not take gas.");
            Assert.That(SEntMan.GetComponent<IndustrialHeatBufferComponent>(buffer).PlasmaFlowing, Is.False);

            SEntMan.SpawnEntity("GasPipeFourway", new EntityCoordinates(map.Grid, 1.5f, 0.5f));
            networks.ForceUpdate();
            SEntMan.EventBus.RaiseLocalEvent(buffer, ref update);
            Assert.That(outlet.Air.GetMoles(Gas.Plasma), Is.GreaterThan(0));
            Assert.That(inlet.Air.GetMoles(Gas.Plasma) + outlet.Air.GetMoles(Gas.Plasma), Is.EqualTo(1).Within(0.0001f));
            Assert.That(SEntMan.GetComponent<TemperatureComponent>(buffer).CurrentTemperature, Is.EqualTo(293.15f).Within(0.01f));
            Assert.That(SEntMan.GetComponent<TemperatureComponent>(machine).CurrentTemperature, Is.EqualTo(293.15f).Within(0.01f));
            SEntMan.EventBus.RaiseLocalEvent(buffer, ref update);
            Assert.That(SEntMan.GetComponent<IndustrialHeatBufferComponent>(buffer).PlasmaFlowing, Is.False,
                "Plasma left in the outlet is not a continuing inlet flow.");

            inlet.Air.Clear();
            outlet.Air.Clear();
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
    public async Task PlasmaReturnsThroughThePumpInAClosedCircuit()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            EntityCoordinates Tile(int x, int y)
            {
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                return new EntityCoordinates(map.Grid, x + 0.5f, y + 0.5f);
            }

            var transform = Server.System<SharedTransformSystem>();
            var buffer = SEntMan.SpawnEntity("IndustrialHeatBuffer", map.GridCoords);
            var layers = SEntMan.GetComponent<AtmosPipeLayersComponent>(buffer);
            Server.System<AtmosPipeLayersSystem>().SetPipeLayer((buffer, layers), AtmosPipeLayer.Secondary);
            Assert.That(transform.AnchorEntity(buffer), Is.True);
            for (var y = 0; y <= 2; y++)
            {
                SEntMan.SpawnEntity("GasPipeFourwayAlt1", Tile(-1, y));
                SEntMan.SpawnEntity("GasPipeFourwayAlt1", Tile(1, y));
            }

            var pumpPosition = Tile(0, 2);
            var pump = SEntMan.SpawnEntity("GasVolumePumpAlt1", pumpPosition);
            transform.SetLocalRotation(pump, Direction.West.ToAngle() - Direction.North.ToAngle());
            SEntMan.GetComponent<ApcPowerReceiverComponent>(pump).Powered = true;
            var volumePump = SEntMan.GetComponent<GasVolumePumpComponent>(pump);
            volumePump.Enabled = true;
            Server.System<NodeGroupSystem>().ForceUpdate();

            var nodes = Server.System<NodeContainerSystem>();
            Assert.That(nodes.TryGetNodes(buffer, "inlet", "outlet", out PipeNode west, out PipeNode east), Is.True);
            Assert.That(nodes.TryGetNodes(pump, "inlet", "outlet", out PipeNode pumpInlet, out PipeNode pumpOutlet), Is.True);
            Assert.That(west.CurrentPipeDirection, Is.EqualTo(PipeDirection.West));
            Assert.That(east.CurrentPipeDirection, Is.EqualTo(PipeDirection.East));
            Assert.That(west.Air, Is.SameAs(pumpInlet.Air));
            Assert.That(east.Air, Is.SameAs(pumpOutlet.Air).And.Not.SameAs(west.Air));
            east.Air.SetMoles(Gas.Plasma, 150);
            east.Air.Temperature = 850f;

            var initialRotation = SEntMan.GetComponent<TransformComponent>(buffer).LocalRotation;
            var reverse = Server.System<SharedVerbSystem>().GetLocalVerbs(buffer, buffer, typeof(Verb), force: true)
                .Single(verb => verb.Text == Loc.GetString("industrial-heat-buffer-reverse"));
            reverse.Act!();
            Assert.That(SEntMan.GetComponent<TransformComponent>(buffer).LocalRotation, Is.EqualTo(initialRotation));
            Assert.That(west.Air, Is.SameAs(pumpInlet.Air));
            Assert.That(east.Air.GetMoles(Gas.Plasma), Is.EqualTo(150));

            var update = new AtmosDeviceUpdateEvent(1f, null, null);
            SEntMan.EventBus.RaiseLocalEvent(buffer, ref update);
            Assert.That(SEntMan.GetComponent<IndustrialHeatBufferComponent>(buffer).PlasmaFlowing, Is.True);
            Assert.That(SEntMan.GetComponent<TemperatureComponent>(buffer).CurrentTemperature, Is.GreaterThan(293.15f));
            var returned = east.Air.TotalMoles;
            SEntMan.EventBus.RaiseLocalEvent(pump, ref update);
            Assert.That(east.Air.TotalMoles, Is.GreaterThan(returned));
            Assert.That(east.Air.TotalMoles + west.Air.TotalMoles, Is.EqualTo(150).Within(0.01f));

            SEntMan.DeleteEntity(pump);
            SEntMan.SpawnEntity("GasPipeFourwayAlt1", pumpPosition);
            Server.System<NodeGroupSystem>().ForceUpdate();
            Assert.That(west.Air, Is.SameAs(east.Air));
            var plasma = east.Air.GetMoles(Gas.Plasma);
            var body = SEntMan.GetComponent<TemperatureComponent>(buffer);
            var before = body.CurrentTemperature;
            SEntMan.EventBus.RaiseLocalEvent(buffer, ref update);
            Assert.That(body.CurrentTemperature, Is.GreaterThan(before));
            Assert.That(east.Air.GetMoles(Gas.Plasma), Is.EqualTo(plasma).Within(0.01f));
            Assert.That(SEntMan.GetComponent<IndustrialHeatBufferComponent>(buffer).PlasmaFlowing, Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task HeatPipesWarmAProcessorAroundACornerAndStopWhenDisconnected()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            EntityCoordinates Tile(int x, int y)
            {
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                return new EntityCoordinates(map.Grid, x + 0.5f, y + 0.5f);
            }

            var transform = Server.System<SharedTransformSystem>();
            var buffer = SEntMan.SpawnEntity("IndustrialHeatBuffer", map.GridCoords);
            Assert.That(transform.AnchorEntity(buffer), Is.True);
            var bendPosition = Tile(1, 0);
            var endPosition = Tile(1, 1);
            var machine = SEntMan.SpawnEntity("BasicIndustrialThermalCentrifuge", Tile(2, 1));
            var bend = SEntMan.SpawnEntity("IndustrialHeatPipe", bendPosition);
            var end = SEntMan.SpawnEntity("IndustrialHeatPipe", endPosition);
            Assert.That(transform.AnchorEntity(bend), Is.True);
            Assert.That(transform.AnchorEntity(end), Is.True);
            var networks = Server.System<NodeGroupSystem>();
            networks.ForceUpdate();
            Assert.That(SEntMan.GetComponent<IndustrialProcessorComponent>(machine).GetFacePort(Direction.West), Is.EqualTo(FacePortState.HeatInput));
            var nodes = Server.System<NodeContainerSystem>();
            Assert.That(nodes.TryGetNode<AdjacentNode>(buffer, IndustrialHeatBufferComponent.HeatNodeName, out var bufferNode), Is.True);
            Assert.That(nodes.TryGetNode<AdjacentNode>(end, IndustrialHeatBufferComponent.HeatNodeName, out var endNode), Is.True);
            Assert.That(bufferNode.NodeGroup, Is.Not.Null.And.SameAs(endNode.NodeGroup));

            var temperature = Server.System<TemperatureSystem>();
            var target = SEntMan.GetComponent<TemperatureComponent>(machine);
            temperature.ForceChangeTemperature(buffer, 900);
            var update = new AtmosDeviceUpdateEvent(1f, null, null);
            for (var i = 0; i < 30; i++)
                SEntMan.EventBus.RaiseLocalEvent(buffer, ref update);
            Assert.That(target.CurrentTemperature, Is.GreaterThan(400));

            transform.Unanchor(bend);
            networks.ForceUpdate();
            temperature.ForceChangeTemperature(machine, 293.15f);
            SEntMan.EventBus.RaiseLocalEvent(buffer, ref update);
            Assert.That(target.CurrentTemperature, Is.EqualTo(293.15f).Within(0.01f));
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
    public async Task MachineWindowHandlesFullBuffersAndUpdatesRecipes()
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
            state.Recipes = [new IndustrialRecipeUiState("CrushSteelOre", longRecipe, 5, false, [slots[0]], [slots[1]])];
            state.MaxInputSlots = state.MaxOutputSlots = 4;
            state.InputSlots = state.OutputSlots = slots[..4];
            window.UpdateState(state);
            window.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(800, 520)));
            Assert.That(search.Text, Is.EqualTo("IRON ORE"), "Catalogue updates must preserve the search.");
            Assert.That(VisibleChildren(window).Count(c => c.Name == "Recipe-CrushSteelOre"), Is.EqualTo(1),
                "Changing recipe availability must replace its row without leaving duplicates.");
            Assert.That(VisibleChildren(window).Count(c => c.Name?.StartsWith("InputSlot") == true), Is.EqualTo(4));

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
    public async Task ResourceStorageWindowShowsOnlyInventoryAndConnections()
    {
        await Client.WaitAssertion(() =>
        {
            var empty = new IndustrialProcessorSlotState(null, 0, string.Empty);
            var slots = Enumerable.Repeat(empty, 32).ToArray();
            slots[0] = new IndustrialProcessorSlotState("SheetSteel", 50, "Сталь");
            var state = new IndustrialProcessorBoundUserInterfaceState("Промышленное хранилище ресурсов", "Промышленный",
                "industrial-processor-idle", 0, null, true, false, false, 32, 32, slots, [], empty,
                FacePortState.ItemInput, FacePortState.ItemOutput, FacePortState.Disabled, FacePortState.Disabled)
            {
                IsBuffer = true,
            };
            using var window = new IndustrialProcessorWindow();
            window.OpenCentered();
            window.UpdateState(state);
            var size = new Vector2(800, 520);
            window.SetSize = size;
            window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));

            var controls = VisibleChildren(window).ToArray();
            Assert.That(controls.Count(control => control.Name?.StartsWith("InputSlot") == true), Is.EqualTo(32));
            Assert.That(controls.Any(control => control.Name is "RecipeSearch" or "ProcessTitle" or "OutputBuffer" or "PauseButton"), Is.False);
            Assert.That(Find<BoxContainer>(window, "MachineConnections").GlobalRect.Bottom, Is.LessThanOrEqualTo(size.Y));
            Assert.That(Find<ContainerButton>(window, "InputSlot31").GlobalRect.Right, Is.LessThanOrEqualTo(size.X));
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
