using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Client.Humanoid;
using Content.IntegrationTests.Pair;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.Input;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.UnitTesting;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.IntegrationTests.Tests.Markings;

[AttributeUsage(AttributeTargets.Method)]
public sealed class MarkingTestAttribute : TestAttribute, IWrapTestMethod
{
    private sealed class MarkingTestCommand(TestCommand inner) : DelegatingTestCommand(inner)
    {
        public override TestResult Execute(TestExecutionContext context)
        {
            var fixture = innerCommand.Test.Fixture as MarkingsViewModelTests;
            fixture!.Client.WaitAssertion(() =>
                {
                    context.CurrentResult = innerCommand.Execute(context);
                })
                .Wait();
            return context.CurrentResult;
        }
    }

    public TestCommand Wrap(TestCommand command)
    {
        return new MarkingTestCommand(command);
    }
}

[TestFixture]
[TestOf(typeof(MarkingsViewModel))]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class MarkingsViewModelTests
{
    public ProtoId<SpeciesPrototype> TestSpecies = "Moth";
    public ProtoId<OrganCategoryPrototype> Head = "Head";
    public ProtoId<OrganCategoryPrototype> Torso = "Torso";
    public ProtoId<MarkingPrototype> MothAntennasCharred = "MothAntennasCharred";
    public ProtoId<MarkingPrototype> MothChestCharred = "MothChestCharred";
    public ProtoId<MarkingPrototype> MothChestDeathhead = "MothChestDeathhead";
    public ProtoId<MarkingPrototype> MothChestFan = "MothChestFan";
    public ProtoId<MarkingPrototype> LizardHornsCurled = "LizardHornsCurled";
    public ProtoId<MarkingPrototype> MothAntennasDefault = "MothAntennasDefault";

    public TestPair Pair = default!;
    public RobustIntegrationTest.ClientIntegrationInstance Client => Pair.Client;
    public MarkingsViewModel Model = default!;
    public MarkingManager Manager = default!;

    [SetUp]
    public async Task SetUp()
    {
        Pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true },
            testContext: new NUnitTestContextWrap(TestContext.CurrentContext, TestContext.Out));
        await Client.WaitPost(() =>
        {
            Model = new MarkingsViewModel();
            Manager = Client.ResolveDependency<MarkingManager>();

            Model.Species = TestSpecies;
            Model.OrganData = Manager.GetMarkingData(TestSpecies);
            Model.OrganProfileData = Manager.GetProfileData(TestSpecies, Sex.Male, Color.White, Color.White);
            Model.ValidateMarkings();
        });
    }

    [TearDown]
    public async Task TearDown()
    {
        await Pair.CleanReturnAsync();
    }

    [MarkingTest]
    public void MarkingSelection()
    {
        Assert.That(Model.TrySelectMarking(Head, HumanoidVisualLayers.HeadTop, MothAntennasCharred), Is.True, "You should be able to select a marking in a limit-1 category if another marking is selected");
        Assert.That(Model.SelectedMarkings(Head, HumanoidVisualLayers.HeadTop)!, Has.Count.EqualTo(1), "The markings model should respect the limits when selecting markings");
        Assert.That(Model.SelectedMarkings(Head, HumanoidVisualLayers.HeadTop)![0].MarkingId, Is.EqualTo(MothAntennasCharred), "The markings model should have replaced the default marking with charred antennae");

        Assert.That(Model.TrySelectMarking(Torso, HumanoidVisualLayers.Chest, MothChestCharred), Is.True);
        Assert.That(Model.SelectedMarkings(Torso, HumanoidVisualLayers.Chest)!, Has.Count.EqualTo(1));
        Assert.That(Model.SelectedMarkings(Torso, HumanoidVisualLayers.Chest)![0].MarkingId, Is.EqualTo(MothChestCharred));

        Assert.That(Model.TrySelectMarking(Torso, HumanoidVisualLayers.Chest, MothChestDeathhead), Is.True);
        Assert.That(Model.SelectedMarkings(Torso, HumanoidVisualLayers.Chest)!, Has.Count.EqualTo(2));
        Assert.That(Model.SelectedMarkings(Torso, HumanoidVisualLayers.Chest)![1].MarkingId, Is.EqualTo(MothChestDeathhead));

        Assert.That(Model.TrySelectMarking(Torso, HumanoidVisualLayers.Chest, MothChestFan), Is.False);
        Assert.That(Model.TrySelectMarking(Head, HumanoidVisualLayers.HeadTop, LizardHornsCurled), Is.False);

        Model.EnforceLimits = false;
        Assert.That(Model.TrySelectMarking(Torso, HumanoidVisualLayers.Chest, MothChestFan), Is.True);
        Assert.That(Model.SelectedMarkings(Torso, HumanoidVisualLayers.Chest)!, Has.Count.EqualTo(3));
        Assert.That(Model.SelectedMarkings(Torso, HumanoidVisualLayers.Chest)![2].MarkingId, Is.EqualTo(MothChestFan));
    }

    [MarkingTest]
    public void MarkingDeselection()
    {
        Assert.That(Model.TryDeselectMarking(Head, HumanoidVisualLayers.HeadTop, MothAntennasDefault), Is.False);
        Assert.That(Model.SelectedMarkings(Head, HumanoidVisualLayers.HeadTop)!, Has.Count.EqualTo(1));
        Assert.That(Model.SelectedMarkings(Head, HumanoidVisualLayers.HeadTop)![0].MarkingId, Is.EqualTo(MothAntennasDefault));

        Model.EnforceLimits = false;

        Assert.That(Model.TryDeselectMarking(Head, HumanoidVisualLayers.HeadTop, MothAntennasDefault), Is.True);
        Assert.That(Model.SelectedMarkings(Head, HumanoidVisualLayers.HeadTop)!, Has.Count.EqualTo(0));
    }

    [MarkingTest]
    public void MarkingColors()
    {
        Model.TrySetMarkingColor(Head, HumanoidVisualLayers.HeadTop, MothAntennasDefault, 0, Color.AliceBlue);
        Assert.That(Model.SelectedMarkings(Head, HumanoidVisualLayers.HeadTop)![0].MarkingColors[0], Is.EqualTo(Color.AliceBlue));
    }

    [MarkingTest]
    public void MarkingColorRestoration()
    {
        Model.EnforceLimits = false;
        Model.TrySetMarkingColor(Head, HumanoidVisualLayers.HeadTop, MothAntennasDefault, 0, Color.AliceBlue);
        Assert.That(Model.SelectedMarkings(Head, HumanoidVisualLayers.HeadTop)![0].MarkingColors[0], Is.EqualTo(Color.AliceBlue));

        Assert.That(Model.TryDeselectMarking(Head, HumanoidVisualLayers.HeadTop, MothAntennasDefault), Is.True);
        Assert.That(Model.TrySelectMarking(Head, HumanoidVisualLayers.HeadTop, MothAntennasDefault), Is.True);

        Assert.That(Model.SelectedMarkings(Head, HumanoidVisualLayers.HeadTop)![0].MarkingColors[0], Is.EqualTo(Color.AliceBlue));
    }

    [MarkingTest]
    public void MarkingGradientRestoration()
    {
        const HumanoidVisualLayers layer = HumanoidVisualLayers.HeadTop;
        var serialization = Client.ResolveDependency<ISerializationManager>();
        var original = Model.SelectedMarkings(Head, layer)![0];
        var oldData = serialization.WriteValue(original);
        Assert.That(serialization.Read<Marking>(oldData).Gradient, Is.Null);

        var gradient = new MarkingGradient
        {
            TipColor = Color.Gold,
            Steps = 4,
            Shape = MarkingGradientShape.Linear,
            Direction = MarkingGradientDirection.Right,
            AngleDegrees = 45,
            Start = 25,
            Length = 50,
            Stops = [new MarkingGradientStop { Position = 50, Color = Color.Green }],
        };
        Model.TrySetMarkingGradient(Head, layer, original.MarkingId, gradient);
        Assert.That(Model.TrySelectMarking(Head, layer, MothAntennasCharred), Is.True);
        Assert.That(Model.TrySelectMarking(Head, layer, original.MarkingId), Is.True);
        var restored = Model.SelectedMarkings(Head, layer)![0];
        Assert.That(restored.Gradient, Is.EqualTo(gradient));
        Assert.That(serialization.Read<Marking>(serialization.WriteValue(restored)), Is.EqualTo(restored));

        Model.TrySetMarkingGradient(Head, layer, original.MarkingId, null);
        Assert.That(Model.SelectedMarkings(Head, layer)![0], Is.EqualTo(original));
    }

    [MarkingTest]
    public void BakedMarkingUsesShapeAndDirection()
    {
        var size = new Vector2i(8, 8);
        var pixels = new Rgba32[64];
        for (var y = 0; y < size.Y; y++)
        for (var x = 0; x < size.X; x++)
        {
            if (x >= 3 || y <= 4)
                pixels[y * size.X + x] = new Rgba32(255, 255, 255);
        }

        var frame = new MarkingGradientBaker.Frame(Vector2i.Zero, pixels);
        using var image = new SixLabors.ImageSharp.Image<Rgba32>(size.X, size.Y);
        var gradient = new MarkingGradient { TipColor = Color.Blue, Steps = 4 };
        MarkingGradientBaker.BakeFrame(frame, size, Color.Red, gradient, image);
        Assert.That(image[1, 4].B, Is.GreaterThan(image[5, 4].B), "The shorter lock should reach the tip color sooner.");
        Assert.That(image[1, 6].A, Is.Zero, "The bake should preserve transparent pixels.");

        gradient.Shape = MarkingGradientShape.Linear;
        gradient.Direction = MarkingGradientDirection.Right;
        MarkingGradientBaker.BakeFrame(frame, size, Color.Red, gradient, image);
        Assert.That(image[7, 1].B, Is.GreaterThan(image[0, 1].B));

        gradient.Shape = MarkingGradientShape.Radial;
        MarkingGradientBaker.BakeFrame(frame, size, Color.Red, gradient, image);
        Assert.That(image[7, 1].B, Is.GreaterThan(image[4, 4].B));

        var cache = Client.ResolveDependency<IResourceCache>();
        var state = cache.GetResource<RSIResource>("/Textures/Corvax/Mobs/Customization/human_hair.rsi").RSI["amazon"];
        var baker = new MarkingGradientBaker(Client.ResolveDependency<IResourceManager>());
        var bake = baker.GetBake(state, Color.Red, gradient);
        Assert.That(bake, Is.Not.Null);
        Assert.That(baker.GetBake(state, Color.Red, gradient)?.Texture, Is.SameAs(bake?.Texture));
    }

    [MarkingTest]
    public void GradientUsesRangeAngleAndIntermediateColors()
    {
        var size = new Vector2i(9, 9);
        var pixels = Enumerable.Repeat(new Rgba32(255, 255, 255), 81).ToArray();
        var frame = new MarkingGradientBaker.Frame(Vector2i.Zero, pixels);
        using var image = new SixLabors.ImageSharp.Image<Rgba32>(size.X, size.Y);
        var gradient = new MarkingGradient
        {
            Shape = MarkingGradientShape.Linear,
            Steps = 2,
            AngleDegrees = 0,
            Start = 25,
            Length = 50,
            TipColor = Color.Blue,
            Stops = [new MarkingGradientStop { Position = 50, Color = Color.Lime }],
        };

        MarkingGradientBaker.BakeFrame(frame, size, Color.Red, gradient, image);
        Assert.That(image[2, 0], Is.EqualTo(new Rgba32(255, 0, 0)));
        Assert.That(image[4, 0], Is.EqualTo(new Rgba32(0, 255, 0)));
        Assert.That(image[6, 0], Is.EqualTo(new Rgba32(0, 0, 255)));

        MarkingGradientBaker.BakeFrame(frame, size, Color.Red, gradient with { AngleDegrees = 45 }, image);
        Assert.That(image[0, 0], Is.EqualTo(new Rgba32(255, 0, 0)));
        Assert.That(image[8, 0], Is.EqualTo(new Rgba32(0, 255, 0)));
        Assert.That(image[8, 8], Is.EqualTo(new Rgba32(0, 0, 255)));
    }

    [MarkingTest]
    public void SavingGradientPersistsThePreviewBakeForTheNextBaker()
    {
        var cache = Client.ResolveDependency<IResourceCache>();
        var state = cache.GetResource<RSIResource>("/Textures/Corvax/Mobs/Customization/human_hair.rsi").RSI["amazon"];
        var resources = Client.ResolveDependency<IResourceManager>();
        var directory = MarkingGradientBaker.CacheDirectory;
        var disk = resources.UserData;
        disk.CreateDir(directory);
        var before = disk.DirectoryEntries(directory).ToHashSet();
        var baker = new MarkingGradientBaker(resources);
        var gradient = new MarkingGradient { AngleDegrees = 337, Length = 63, TipColor = Color.Gold };
        var root = new Color(0.8f, 0.36f, 0.28f);

        var preview = baker.GetBake(state, root, gradient);
        Assert.That(preview, Is.Not.Null);
        Assert.That(disk.DirectoryEntries(directory), Is.EquivalentTo(before));
        Assert.That(baker.GetBake(state, root, gradient, persist: true)?.Texture, Is.SameAs(preview?.Texture));

        var path = directory / disk.DirectoryEntries(directory).Except(before).Single();
        try
        {
            // A known byte distinguishes loading the existing bake from overwriting it with a new one.
            using (var file = disk.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                file.Seek(-1, SeekOrigin.End);
                file.WriteByte(123);
            }

            var reopened = new MarkingGradientBaker(resources);
            Assert.That(reopened.GetBake(state, Color.FromHex(root.ToHex()), gradient, persist: true), Is.Not.Null);
            Assert.That(disk.DirectoryEntries(directory).Except(before), Has.Exactly(1).Items);
            using var saved = disk.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            saved.Seek(-1, SeekOrigin.End);
            Assert.That(saved.ReadByte(), Is.EqualTo(123));
        }
        finally
        {
            disk.Delete(path);
        }
    }

    [TestCase(700)]
    [TestCase(1200)]
    public async Task ColorEditorStaysInsideMarkingPicker(int width)
    {
        MarkingPicker picker = default!;
        PanelContainer host = default!;
        Button colorButton = default!;
        Button addColor = default!;
        Button removeColor = default!;
        var size = new Vector2(width, 600);
        const HumanoidVisualLayers layer = HumanoidVisualLayers.HeadTop;

        await Client.WaitAssertion(() =>
        {
            Model.TrySetMarkingGradient(Head, layer, MothAntennasDefault, new MarkingGradient());
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            host = new PanelContainer
            {
                SetSize = size,
                HorizontalAlignment = Control.HAlignment.Left,
                VerticalAlignment = Control.VAlignment.Top,
            };
            picker = new MarkingPicker();
            host.AddChild(picker);
            ui.WindowRoot.AddChild(host);
            picker.SetModel(Model);
        });

        try
        {
            await Pair.RunTicksSync(1);
            await Client.WaitAssertion(() =>
            {
                colorButton = Descendants(picker).OfType<Button>().First(button => button.VisibleInTree &&
                    button.Text == Loc.GetString("marking-colors-button"));
            });
            await ClickButton(colorButton);
            (Vector2 Position, Vector2 Size, Vector2 Selector) before = default;
            await Client.WaitAssertion(() =>
            {
                var selector = Descendants(picker).OfType<ColorSelectorSliders>().Single();
                var editor = selector.Parent!.Parent!;
                before = (editor.GlobalPosition, editor.Size, selector.Size);
                Assert.That(editor.Width, Is.GreaterThan(0f));
                Assert.That(editor.GlobalPosition.X + editor.Width, Is.LessThanOrEqualTo(picker.GlobalPosition.X + picker.Width));

                addColor = ColorStopButton("marking-gradient-add-color");
                removeColor = ColorStopButton("marking-gradient-remove-color");
                selector.Color = Color.FromHex("#CCFFFF");
            });
            await Pair.RunTicksSync(1);
            await Client.WaitAssertion(() =>
            {
                var selector = Descendants(picker).OfType<ColorSelectorSliders>().Single();
                var editor = selector.Parent!.Parent!;
                Assert.That((editor.GlobalPosition, editor.Size, selector.Size), Is.EqualTo(before));
            });

            await ClickButton(addColor);
            await Client.WaitAssertion(() =>
            {
                var gradient = Model.GetMarking(Head, layer, MothAntennasDefault)?.Gradient;
                Assert.That(gradient?.Stops?.Single().Position, Is.EqualTo(50));
                Assert.That(removeColor.Disabled, Is.False);
            });
            await ClickButton(removeColor);
            await Client.WaitAssertion(() =>
            {
                Assert.That(Model.GetMarking(Head, layer, MothAntennasDefault)?.Gradient?.Stops, Is.Null);
            });
        }
        finally
        {
            await Client.WaitPost(() => host.Parent?.RemoveChild(host));
        }

        Button ColorStopButton(string key) => Descendants(picker).OfType<Button>()
            .Single(button => button.VisibleInTree && button.ToolTip == Loc.GetString(key));
    }

    [Test]
    public async Task MarkingFiltersFollowTheSelectedZone()
    {
        MarkingPicker picker = default!;
        PanelContainer host = default!;
        ScrollContainer available = default!;
        await Client.WaitAssertion(() =>
        {
            ProtoId<SpeciesPrototype> species = "Human";
            Model.Species = species;
            Model.OrganData = Manager.GetMarkingData(species);
            Model.OrganProfileData = Manager.GetProfileData(species, Sex.Male, Color.White, Color.White);
            Model.Markings = [];
            Model.ValidateMarkings();

            picker = new MarkingPicker();
            host = new PanelContainer { SetSize = new Vector2(1200, 600) };
            host.AddChild(picker);
            Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.AddChild(host);
            picker.SetModel(Model);
        });

        try
        {
            await Pair.RunTicksSync(1);
            await Click(button => Descendants(button).OfType<Label>().Any(label => label.Text == "Волосы"));
            await Client.WaitAssertion(() =>
            {
                Assert.That(Filters(), Is.EqualTo(new[] { FilterLabel("all"), FilterLabel("hairstyles"), FilterLabel("facial-hair") }));
                Assert.That(HasMarking("HumanHairAmazon"), Is.True);
                available = Descendants(picker).OfType<ScrollContainer>().Single(scroll => HasMarking("HumanHairAmazon", scroll));
                available.VScroll = 120;
                Assert.That(available.VScroll, Is.GreaterThan(0));
            });

            await Click(button => button.Text?.TrimStart(' ', '▶') == FilterLabel("facial-hair"));
            await Client.WaitAssertion(() =>
            {
                Assert.That(HasMarking("HumanHairAmazon"), Is.False);
                Assert.That(HasMarking("HumanFacialHairFullbeard"), Is.True);
                Assert.That(available.VScroll, Is.Zero);
            });

            await Click(button => Descendants(button).OfType<Label>().Any(label => label.Text == "Туловище"));
            await Client.WaitAssertion(() =>
            {
                Assert.That(Filters(), Does.Contain(FilterLabel("underwear")));
                Assert.That(Filters(), Does.Not.Contain(FilterLabel("facial-hair")));
            });

            await Click(button => Descendants(button).OfType<Label>().Any(label => label.Text == "Волосы"));
            await Client.WaitAssertion(() => Assert.That(HasMarking("HumanHairAmazon"), Is.False));
        }
        finally
        {
            await Client.WaitPost(() => host.Parent?.RemoveChild(host));
        }

        async Task Click(Func<Button, bool> matches)
        {
            Button button = default!;
            await Client.WaitAssertion(() => button = Descendants(picker).OfType<Button>().First(matches));
            await ClickButton(button);
        }

        string[] Filters() => Descendants(picker).OfType<Button>().Select(button => button.Text)
            .Where(text => text != null && (text.StartsWith("▶  ") || text.StartsWith("   ")))
            .Select(text => text.TrimStart(' ', '▶')).ToArray();

        bool HasMarking(string id, Control parent = null)
        {
            var name = Client.ResolveDependency<ILocalizationManager>().TryGetString($"marking-{id}", out var localized) ? localized : id;
            return Descendants(parent ?? picker).OfType<Label>().Any(label => label.Text == name);
        }

        static string FilterLabel(string key) => Loc.GetString($"markings-filter-{key}");
    }

    private async Task ClickButton(Button button)
    {
        var screen = new ScreenCoordinates(button.GlobalPixelPosition + button.PixelSize / 2, button.Window?.Id ?? default);
        foreach (var state in new[] { BoundKeyState.Down, BoundKeyState.Up })
        {
            await Client.DoGuiEvent(button, new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state,
                screen, default, button.Size / 2, button.PixelSize / 2));
        }
        await Pair.RunTicksSync(1);
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
