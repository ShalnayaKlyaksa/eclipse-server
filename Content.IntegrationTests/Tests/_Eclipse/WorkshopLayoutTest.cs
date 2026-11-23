using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Content.Client._Eclipse.Beekeeping;
using Content.Client._Eclipse.Cooking;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Eclipse;
using Content.Shared._Eclipse.Beekeeping;
using Content.Shared._Eclipse.Cooking;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Eclipse;

public sealed class WorkshopLayoutTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task WorkstationWindowsStayBoundedWithLongNamesAndFullInventories()
    {
        await Client.WaitAssertion(() =>
        {
            var longName = new string('Ж', 300);
            using var kitchen = new CulinaryWindow();
            kitchen.Apply(new CulinaryState([longName], longName, 280, CulinaryMethod.Simmer, .5f, true, false, true, "culinary-status-working")
            {
                InputSlots = Enumerable.Range(0, 12).Select(i => new WorkshopItemState(i, "FoodPotato", longName)).ToArray(),
                PropertyValues = Enum.GetValues<CulinaryProperty>().Select(p => new CulinaryPropertyState(p, 5)).ToArray(),
                History = Enum.GetNames<CulinaryMethod>(), InputCount = 12, SecondsRemaining = 30,
            });
            Verify(kitchen, new Vector2(620, 580));
            Verify(kitchen, new Vector2(560, 510));
            foreach (var methods in new[]
            {
                new[] { CulinaryMethod.Boil, CulinaryMethod.Simmer, CulinaryMethod.Reduce },
                new[] { CulinaryMethod.Bake }, new[] { CulinaryMethod.Fry }, new[] { CulinaryMethod.Steam },
                new[] { CulinaryMethod.Soak, CulinaryMethod.Marinate },
            })
            {
                kitchen.Apply(new CulinaryState([], "", 20, methods[0], 0, false, true, true, "culinary-status-ready") { AllowedMethods = methods });
                Verify(kitchen, new Vector2(560, 510));
            }
            using var apiary = new ApiaryWindow();
            var species = CProtoMan.EnumeratePrototypes<BeeSpeciesPrototype>().Select(p => new ApiarySpeciesState(
                p.ID, longName, p.Tier, p.CycleSeconds, p.Comb.Id, longName, p.RareProduct?.Id, longName,
                p.RareChance, p.RareGeneration, p.RareLineageSeconds / 60, p.NeedsMinerals)).ToArray();
            apiary.Apply(new ApiaryState("apiary-status-working", longName, longName, longName, longName, .5f, 200, 200, 50, 12, 8, true)
            {
                ActiveQueen = true, Species = species,
                OutputSlots = Enumerable.Range(0, 40).Select(i => new WorkshopItemState(i, "EclipseBeeDrone", longName)).ToArray(),
                ParentSlots = [new(0, "EclipseBeePrincess", longName) { Role = "Princess" }, new(1, "EclipseBeeDrone", longName) { Role = "Drone" }],
                Crosses = CProtoMan.EnumeratePrototypes<BeeMutationPrototype>().Select(p => new ApiaryMutationState(
                    p.Result.Id, p.First.Id, p.Second.Id, p.Chance, p.Generation, p.LineageSeconds / 60, p.NeedsMinerals)).ToArray(),
            });
            Verify(apiary, new Vector2(860, 650));
            Verify(apiary, new Vector2(800, 630));
            apiary.Tabs.CurrentTab = 1;
            Verify(apiary, new Vector2(800, 580));
            var search = Find<LineEdit>(apiary, "ApiarySearch");
            search.SetText("нет такой породы", true);
            Assert.That(Find<BoxContainer>(apiary, "ApiarySpeciesList").ChildCount, Is.Zero);
            search.SetText("", true);
            Assert.That(Find<BoxContainer>(apiary, "ApiarySpeciesList").ChildCount, Is.EqualTo(species.Length));
        });
    }

    private static void Verify(FancyWindow window, Vector2 size)
    {
        window.SetSize = size;
        window.Measure(size);
        Assert.That(window.DesiredSize.X, Is.LessThanOrEqualTo(size.X + 1), "Text must not turn the window into a fullscreen strip.");
        window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
        foreach (var control in Visible(window))
        {
            Assert.That(control.GlobalRect.Right, Is.LessThanOrEqualTo(size.X + 1), $"{control.GetType().Name} {control.Name} overflows horizontally.");
            if (control.Name is "ApiaryToggle" or "ApiaryInsertHeld")
                Assert.That(control.GlobalRect.Bottom, Is.LessThanOrEqualTo(size.Y + 1), "Colony actions must stay visible without scrolling.");
            if (control is ScrollContainer scroll) Assert.That(scroll.HScrollEnabled, Is.False);
            if (control is TextureRect { Texture: AtlasTexture atlas })
                Assert.That(atlas.SourceTexture, Is.Not.InstanceOf<AtlasTexture>(), "The renderer accepts only one atlas layer.");
        }
    }

    private static IEnumerable<Control> Visible(Control parent)
    {
        foreach (var child in parent.Children)
        {
            if (!child.Visible) continue;
            yield return child;
            foreach (var descendant in Visible(child)) yield return descendant;
        }
    }

    private static T Find<T>(Control root, string name) where T : Control
        => Visible(root).OfType<T>().First(control => control.Name == name);
}
