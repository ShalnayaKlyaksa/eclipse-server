#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._Eclipse.Industrial;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Atmos;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class IndustrialFluidInterfaceTest : GameTest
{
    [Test]
    public async Task RedrawnCrusherFreezesPoweredAndUnpoweredPausedFramesAndRestartsItsWorkingAnimation()
    {
        await Client.WaitAssertion(() =>
        {
            var appearance = Client.System<AppearanceSystem>();
            var sprites = Client.System<SpriteSystem>();
            foreach (var prototype in new[] { "BasicIndustrialCrusher", "IndustrialCrusher", "PerfectIndustrialCrusher",
                         "IndustrialCutter", "PerfectIndustrialCutter", "IndustrialGrinder", "PerfectIndustrialGrinder",
                         "IndustrialWireMill", "PerfectIndustrialWireMill" })
            {
                var uid = CEntMan.SpawnEntity(prototype, MapCoordinates.Nullspace);
                var sprite = CEntMan.GetComponent<SpriteComponent>(uid);
                Assert.That(sprites.LayerMapTryGet((uid, sprite), IndustrialProcessorVisualLayers.Base, out var index, false));
                Assert.That(sprites.TryGetLayer((uid, sprite), index, out var layer, false));
                void CheckPaused(bool powered)
                {
                    appearance.SetData(uid, IndustrialProcessorVisuals.Powered, powered);
                    appearance.FrameUpdate(0);
                    Assert.That(sprites.LayerGetRsiState((uid, sprite), index).ToString(), Is.EqualTo("paused"), prototype);
                    Assert.That(layer.AutoAnimated, Is.False, "The two paused frames must not blink between on and off.");
                    Assert.That(layer.AnimationFrame, Is.EqualTo(powered ? 0 : 1), prototype);
                }
                foreach (var state in new[] { IndustrialProcessorState.Idle, IndustrialProcessorState.Paused,
                             IndustrialProcessorState.Blocked, IndustrialProcessorState.Unresearched, IndustrialProcessorState.Unheated })
                {
                    appearance.SetData(uid, IndustrialProcessorVisuals.State, state);
                    CheckPaused(true);
                    CheckPaused(false);
                    CheckPaused(true);
                }
                appearance.SetData(uid, IndustrialProcessorVisuals.State, IndustrialProcessorState.Working);
                appearance.FrameUpdate(0);
                Assert.That(sprites.LayerGetRsiState((uid, sprite), index).ToString(), Is.EqualTo("power_on"));
                Assert.That(layer.AutoAnimated, Is.True);
                CheckPaused(false); // Power can disappear before the process-state update arrives.
                appearance.SetData(uid, IndustrialProcessorVisuals.Powered, true);
                appearance.FrameUpdate(0);
                Assert.That(sprites.LayerGetRsiState((uid, sprite), index).ToString(), Is.EqualTo("power_on"));
                Assert.That(layer.AutoAnimated, Is.True);
                CEntMan.DeleteEntity(uid);
            }
        });
    }

    [Test]
    public async Task RedrawnSmelterUsesPowerOnOnlyWhileWorkingAcrossAllMachineTiers()
    {
        await Client.WaitAssertion(() =>
        {
            var appearance = Client.System<AppearanceSystem>();
            var sprites = Client.System<SpriteSystem>();
            var machines = new[] { "BasicIndustrialSmelter", "IndustrialSmelter", "PerfectIndustrialSmelter",
                "IndustrialChemicalReactor", "PerfectIndustrialChemicalReactor", "IndustrialDryer", "PerfectIndustrialDryer",
                "IndustrialBlastFurnace", "PerfectIndustrialBlastFurnace", "IndustrialVacuumFurnace", "PerfectIndustrialVacuumFurnace" };
            foreach (var prototype in machines)
            {
                var uid = CEntMan.SpawnEntity(prototype, MapCoordinates.Nullspace);
                var sprite = CEntMan.GetComponent<SpriteComponent>(uid);
                Assert.That(sprites.LayerMapTryGet((uid, sprite), IndustrialProcessorVisualLayers.Base, out var layer, false));
                Assert.That(sprites.LayerGetRsiState((uid, sprite), layer).ToString(), Is.EqualTo("paused"), prototype);
                foreach (var state in Enum.GetValues<IndustrialProcessorState>())
                {
                    appearance.SetData(uid, IndustrialProcessorVisuals.State, state);
                    appearance.FrameUpdate(0);
                    Assert.That(sprites.LayerGetRsiState((uid, sprite), layer).ToString(),
                        Is.EqualTo(state == IndustrialProcessorState.Working ? "power_on" : "paused"), $"{prototype}: {state}");
                }
                CEntMan.DeleteEntity(uid);
            }
        });
    }

    [Test]
    public async Task AllProductionMachineSpriteFamiliesLoadBeforeTheirMenusOpen()
    {
        await Client.WaitAssertion(() =>
        {
            var resources = Client.ResolveDependency<IResourceCache>();
            foreach (var family in new[] { "crusher", "centrifuge", "smelter", "assembler", "buffer", "heat_buffer", "workbench" })
            {
                var rsi = resources.GetResource<RSIResource>(new ResPath($"/Textures/_Eclipse/Structures/Machines/industrial_{family}.rsi")).RSI;
                Assert.That(rsi.TryGetState("icon", out _), Is.True, family);
            }
        });
    }

    [Test]
    public async Task FluidAndBathRecipesFitTheMinimumWindowAndRemainSearchable()
    {
        await Client.WaitAssertion(() =>
        {
            var empty = new IndustrialProcessorSlotState(null, 0, "");
            var slots = Enumerable.Repeat(empty, 8).ToArray();
            var medium = new IndustrialFluidSlotState("IndustrialCrystalMedium", "Среда выращивания высокочистых монокристаллов", 25, Color.Cyan);
            var slurry = new IndustrialFluidSlotState("IndustrialQuartzSlurry", "Кварцевая суспензия", 20, Color.Gray);
            var product = new IndustrialFluidSlotState("IndustrialSilicaLeachate", "Раствор выщелачивания кремнезёма", 25, Color.Yellow);
            var state = new IndustrialProcessorBoundUserInterfaceState("Промышленная установка выращивания монокристаллов", "Промышленный",
                "industrial-processor-idle", 0, null, true, false, false, 8, 8, slots, slots, empty,
                FacePortState.LiquidInput, FacePortState.LiquidOutput, FacePortState.ItemOutput, FacePortState.ItemInput)
            {
                UsesFluids = true, FluidInputCapacity = 250, FluidOutputCapacity = 250,
                FluidInput = [medium, slurry], FluidOutput = [product],
                Recipes = [new IndustrialRecipeUiState("FluidRecipe", product.Name, 8, true, [], [])
                { FluidInputs = [slurry], FluidOutputs = [product] }], SelectedRecipeId = "FluidRecipe",
            };
            using var window = new IndustrialProcessorWindow();
            window.OpenCentered();
            window.UpdateState(state);
            Assert.That(Find<Control>(window, "ProcessBoard").Visible, Is.False, "Pure fluid recipes should not display empty item buffers.");
            foreach (var size in new[] { new Vector2(800, 520), new Vector2(880, 660) })
            {
                window.SetSize = size;
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                foreach (var child in VisibleChildren(window))
                    Assert.That(child.GlobalRect.Right, Is.LessThanOrEqualTo(size.X + 1), child.Name);
                Assert.That(Find<BoxContainer>(window, "MachineConnections").GlobalRect.Bottom, Is.LessThanOrEqualTo(size.Y));
            }
            Find<LineEdit>(window, "RecipeSearch").SetText("СУСПЕНЗИЯ", true);
            Assert.That(Find<ContainerButton>(window, "Recipe-FluidRecipe").Visible, Is.True, "Fluid ingredients must be included in search.");
            var solid = new IndustrialProcessorSlotState("IndustrialSiliconBoule", 1, "Монокристалл кремния");
            state.Recipes = [new IndustrialRecipeUiState("BathRecipe", solid.DisplayName, 18, true, [solid], [solid]) { Bath = [medium] }];
            state.SelectedRecipeId = "BathRecipe";
            window.UpdateState(state);
            Assert.That(Find<Control>(window, "ProcessBoard").Visible, Is.True);
            Assert.That(VisibleChildren(window).OfType<Label>().Any(l => l.Text == "25 / 25 u"), Is.True,
                "The retained bath must have its own availability counter.");
            state.IsBuffer = true;
            state.MaxInputSlots = state.MaxOutputSlots = 0;
            state.InputSlots = state.OutputSlots = [];
            state.Recipes = [];
            state.FluidInputCapacity = 1000;
            state.SelectedRecipeId = null;
            window.UpdateState(state);
            Assert.That(Find<Control>(window, "ProcessBoard").Visible, Is.False);
        });
    }

    [Test]
    public async Task ConnectorsUsePortDirectionAndOnlyAllocateAtMachineEndpoints()
    {
        await Client.WaitAssertion(() =>
        {
            var uid = CEntMan.SpawnEntity("LpItemPipe", MapCoordinates.Nullspace);
            var sprite = CEntMan.GetComponent<SpriteComponent>(uid);
            var sprites = Client.System<SpriteSystem>();
            var connectors = Client.System<IndustrialPipeConnectorSystem>();
            const string rsi = "_Eclipse/Structures/Piping/item_lp_pipe.rsi";
            connectors.Update((uid, sprite), PipeDirection.None, PipeDirection.None, rsi);
            Assert.That(sprites.LayerMapTryGet((uid, sprite), "industrial-socket-North", out _, false), Is.False);
            foreach (var (direction, expected, offset) in new[]
            {
                (PipeDirection.North, Angle.Zero, new Vector2(0, .5f)),
                (PipeDirection.South, Angle.FromDegrees(180), new Vector2(0, -.5f)),
                (PipeDirection.West, Angle.FromDegrees(90), new Vector2(-.5f, 0)),
                (PipeDirection.East, Angle.FromDegrees(-90), new Vector2(.5f, 0)),
            })
            {
                foreach (var input in new[] { true, false })
                {
                    connectors.Update((uid, sprite), input ? direction : PipeDirection.None, input ? PipeDirection.None : direction, rsi);
                    Assert.That(sprites.LayerMapTryGet((uid, sprite), "industrial-arrow-" + direction, out var key, false));
                    Assert.That(sprites.TryGetLayer((uid, sprite), key, out var layer, false));
                    Assert.That(layer.Visible);
                    Assert.That(layer.Offset, Is.EqualTo(offset * .8125f));
                    var target = expected.Theta + (input ? 0 : Math.PI);
                    Assert.That(Math.Cos(layer.Rotation.Theta - target), Is.EqualTo(1).Within(.0001),
                        "Input must point towards the machine, output away from it: " + direction);
                }
            }
            connectors.Update((uid, sprite), PipeDirection.None, PipeDirection.None, rsi);
            foreach (var face in new[] { "North", "South", "East", "West" })
            {
                sprites.LayerMapTryGet((uid, sprite), "industrial-arrow-" + face, out var key, false);
                sprites.TryGetLayer((uid, sprite), key, out var layer, false);
                Assert.That(layer!.Visible, Is.False, "Disconnected sockets must disappear.");
            }
            CEntMan.DeleteEntity(uid);
        });
    }

    private static T Find<T>(Control parent, string name) where T : Control
        => parent.Name == name && parent is T found ? found : parent.Children.Select(c => FindOrNull<T>(c, name)).First(c => c != null)!;
    private static T? FindOrNull<T>(Control parent, string name) where T : Control
        => parent.Name == name && parent is T found ? found : parent.Children.Select(c => FindOrNull<T>(c, name)).FirstOrDefault(c => c != null);
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
