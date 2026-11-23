using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Industrial;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Atmos;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Construction.Components;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Client.Graphics;
using Content.Client.Clickable;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class LiquidPipeImplementationTest : GameTest
{
    [TestCase("BasicLiquidPipe", "BasicLiquidPipe", 1f)]
    [TestCase("IndustrialLiquidPipe", "IndustrialLiquidPipe", 3f)]
    [TestCase("PerfectLiquidPipe", "PerfectLiquidPipe", 6f)]
    [TestCase("PerfectLiquidPipe", "BasicLiquidPipe", 1f)]
    public async Task FluidTransferHonoursWeakestTierAndConservesContents(string firstId, string secondId, float rate)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            Server.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            var a = SEntMan.SpawnEntity(firstId, map.GridCoords);
            var b = SEntMan.SpawnEntity(secondId, new EntityCoordinates(map.Grid, 1.5f, .5f));
            var network = Server.System<LiquidPipeNetworkSystem>();
            var solutions = Server.System<SharedSolutionContainerSystem>();
            network.Update(0);
            Assert.That(solutions.TryGetSolution(a, LiquidPipeComponent.SolutionName, out var source, out var sourceContents));
            Assert.That(solutions.TryGetSolution(b, LiquidPipeComponent.SolutionName, out _, out var targetContents));
            Assert.That(solutions.TryAddReagent(source.Value, "Water", FixedPoint2.New(20)));
            for (var i = 0; i < 4; i++)
                network.Update(.25f);
            Assert.That(targetContents.Volume.Float(), Is.EqualTo(rate).Within(.001f));
            Assert.That(sourceContents.Volume + targetContents.Volume, Is.EqualTo(FixedPoint2.New(20)));
            Assert.That(SEntMan.GetComponent<LiquidPipeComponent>(a).ConnectedDirections, Is.EqualTo(PipeDirection.East));
            Assert.That(SEntMan.GetComponent<LiquidPipeComponent>(b).ConnectedDirections, Is.EqualTo(PipeDirection.West));
            var appearance = Server.System<SharedAppearanceSystem>();
            Assert.That(appearance.TryGetData<Color>(b, SolutionContainerVisuals.Color, out var color));
            Assert.That(color, Is.EqualTo(targetContents.GetColor(Server.ResolveDependency<IPrototypeManager>())));
            var before = targetContents.Volume;
            network.Update(100);
            Assert.That((targetContents.Volume - before).Float(), Is.LessThanOrEqualTo(rate + .001f),
                "A long frame must not create an unlimited catch-up burst.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task CuttingAndRejoiningFluidLinePreservesContentsAndStopsTransferAcrossTheCut()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var transform = Server.System<SharedTransformSystem>();
            var network = Server.System<LiquidPipeNetworkSystem>();
            var solutions = Server.System<SharedSolutionContainerSystem>();
            var pipes = Enumerable.Range(0, 3).Select(i =>
            {
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(i, 0), map.Tile.Tile);
                return SEntMan.SpawnEntity("PerfectLiquidPipe", new EntityCoordinates(map.Grid, i + .5f, .5f));
            }).ToArray();
            network.Update(0);
            Assert.That(solutions.TryGetSolution(pipes[0], LiquidPipeComponent.SolutionName, out var first, out _));
            Assert.That(solutions.TryAddReagent(first.Value, "Water", FixedPoint2.New(20)));
            for (var i = 0; i < 12; i++)
                network.Update(.25f);
            Assert.That(solutions.TryGetSolution(pipes[2], LiquidPipeComponent.SolutionName, out _, out var right));
            Assert.That(right.Volume, Is.GreaterThan(FixedPoint2.Zero));
            transform.Unanchor(pipes[1], SEntMan.GetComponent<TransformComponent>(pipes[1]));
            network.Update(0);
            var rightBefore = right.Volume;
            for (var i = 0; i < 8; i++)
                network.Update(.25f);
            Assert.That(right.Volume, Is.EqualTo(rightBefore));
            Assert.That(SEntMan.GetComponent<LiquidPipeComponent>(pipes[2]).ConnectedDirections, Is.EqualTo(PipeDirection.None));
            FixedPoint2 Total() => pipes.Aggregate(FixedPoint2.Zero, (sum, pipe) =>
            {
                solutions.TryGetSolution(pipe, LiquidPipeComponent.SolutionName, out _, out var contents);
                return sum + contents!.Volume;
            });
            Assert.That(Total(), Is.EqualTo(FixedPoint2.New(20)));
            transform.AnchorEntity(pipes[1]);
            network.Update(0);
            for (var i = 0; i < 8; i++)
                network.Update(.25f);
            Assert.That(right.Volume, Is.Not.EqualTo(rightBefore));
            Assert.That(Total(), Is.EqualTo(FixedPoint2.New(20)));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task NativePipeMasksLoadAndContentSquareTracksColourAndEmptyState()
    {
        await Client.WaitAssertion(() =>
        {
            var cache = Client.ResolveDependency<IResourceCache>();
            foreach (var (folder, prefix) in new[] { ("item_lp_pipe", "lp"), ("liquid_pipe", "pipe") })
            {
                var rsi = cache.GetResource<RSIResource>(new ResPath($"/Textures/_Eclipse/Structures/Piping/{folder}.rsi")).RSI;
                Assert.That(cache.ContentFileExists(new ResPath($"/Textures/_Eclipse/Structures/Piping/{folder}.rsic")), Is.True);
                Assert.That(cache.ContentFileExists(new ResPath($"/Textures/_Eclipse/Structures/Piping/{folder}.rsi/{prefix}0.png")), Is.False,
                    "Connection images must be generated in memory, not supplied as baked PNGs.");
                var texture = ((AtlasTexture) rsi[prefix + "0"].Frame0).SourceTexture;
                for (var i = 0; i < 16; i++)
                {
                    Assert.That(rsi.TryGetState(prefix + i, out _), Is.True, folder + ": " + i);
                    Assert.That(((AtlasTexture) rsi[prefix + i].Frame0).SourceTexture, Is.SameAs(texture),
                        "All masks must share a single GPU atlas.");
                }
                Assert.That(cache.GetResource<RSIResource>(rsi.Path).RSI, Is.SameAs(rsi),
                    "Spawning another pipe must reuse the cached atlas.");
                var clicks = Client.ResolveDependency<IClickMapManager>();
                Assert.That(clicks.IsOccluding(rsi, prefix + "0", Robust.Shared.Graphics.RSI.RsiDirection.South, 0, new Vector2i(16,16)), Is.True);
                Assert.That(clicks.IsOccluding(rsi, prefix + "0", Robust.Shared.Graphics.RSI.RsiDirection.South, 0, new Vector2i(1,1)), Is.False);
                if (folder == "liquid_pipe")
                    Assert.That(((AtlasTexture) rsi["content"].Frame0).SourceTexture, Is.SameAs(texture),
                        "The colour square must use the same atlas as the pipe body.");
            }
            var pipe = CEntMan.SpawnEntity("BasicLiquidPipe", MapCoordinates.Nullspace);
            var sprite = CEntMan.GetComponent<SpriteComponent>(pipe);
            var sprites = Client.System<SpriteSystem>();
            Assert.That(sprites.LayerMapTryGet((pipe, sprite), "content", out var marker, false));
            var appearance = Client.System<AppearanceSystem>();
            var liquidColor = new Color(.75f, .15f, .3f, .4f);
            appearance.SetData(pipe, SolutionContainerVisuals.FillFraction, .5f);
            appearance.SetData(pipe, SolutionContainerVisuals.Color, liquidColor);
            appearance.FrameUpdate(0);
            Assert.That(sprites.TryGetLayer((pipe, sprite), marker, out var layer, false));
            Assert.That(layer.Color, Is.EqualTo(liquidColor.WithAlpha(1f)));
            appearance.SetData(pipe, SolutionContainerVisuals.FillFraction, 0f);
            appearance.FrameUpdate(0);
            Assert.That(layer.Color, Is.EqualTo(Color.FromHex("#2B3139")), "An empty pipe must not retain its previous fluid colour.");
            CEntMan.DeleteEntity(pipe);
        });
    }

    [Test]
    public async Task MixturesRetainReagentsAndDismantlingSpillsTheRealContents()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            Server.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            var a = SEntMan.SpawnEntity("PerfectLiquidPipe", map.GridCoords);
            var b = SEntMan.SpawnEntity("PerfectLiquidPipe", new EntityCoordinates(map.Grid, 1.5f, .5f));
            var solutions = Server.System<SharedSolutionContainerSystem>();
            var network = Server.System<LiquidPipeNetworkSystem>();
            network.Update(0);
            solutions.TryGetSolution(a, LiquidPipeComponent.SolutionName, out var first, out var source);
            solutions.TryGetSolution(b, LiquidPipeComponent.SolutionName, out var second, out var target);
            solutions.TryAddReagent(first.Value, "Water", FixedPoint2.New(20));
            solutions.TryAddReagent(second.Value, "Ethanol", FixedPoint2.New(10));
            network.Update(.25f);
            Assert.That(target.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(1.5f)));
            Assert.That(target.GetTotalPrototypeQuantity("Ethanol"), Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(source.Volume + target.Volume, Is.EqualTo(FixedPoint2.New(30)));
            var appearance = Server.System<SharedAppearanceSystem>();
            Assert.That(appearance.TryGetData<Color>(b, SolutionContainerVisuals.Color, out var color));
            Assert.That(color, Is.EqualTo(target.GetColor(Server.ResolveDependency<IPrototypeManager>())));
            var spilled = target.Volume;
            Server.System<SharedTransformSystem>().Unanchor(b);
            var user = SEntMan.SpawnEntity(null, map.GridCoords);
            SEntMan.EventBus.RaiseLocalEvent(b, new UserUnanchoredEvent(user, user));
            Assert.That(target.Volume, Is.EqualTo(FixedPoint2.Zero));
            var totalPuddles = FixedPoint2.Zero;
            var query = SEntMan.AllEntityQueryEnumerator<PuddleComponent, TransformComponent>();
            while (query.MoveNext(out var puddle, out var component, out var transform))
            {
                if (transform.GridUid == map.Grid.Owner && solutions.TryGetSolution(puddle, component.SolutionName, out _, out var contents))
                    totalPuddles += contents.Volume;
            }
            Assert.That(totalPuddles, Is.EqualTo(spilled), "Removing a filled pipe must spill its contents rather than silently deleting them.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
