using System.Linq;
using Content.Client.Guidebook;
using Content.Shared.Guidebook;
using NUnit.Framework;
using Robust.Shared.Prototypes;

namespace Content.Tests.Client;

[TestFixture]
public sealed class GuidebookCatalogTest
{
    private static GuideEntry Entry(string id, params string[] children) => new()
    {
        Id = id, Children = children.Select(c => new ProtoId<GuideEntryPrototype>(c)).ToList(),
    };

    [Test]
    public void CatalogKeepsHierarchyAndSearchesAcrossBranches()
    {
        var entries = new[] { Entry("root", "heat", "pipes"), Entry("heat"), Entry("pipes") };
        var titles = new[] { "Инженерия", "Нагрев", "Трубы" };
        var text = new[] { "Обзор", "Корпус нагрет горячей плазмой", "Сырьё проходит через трубы" };
        var catalog = new GuidebookCatalog(entries, e => titles[System.Array.IndexOf(entries, e)],
            e => text[System.Array.IndexOf(entries, e)]);
        Assert.Multiple(() =>
        {
            Assert.That(catalog.Roots, Is.EqualTo(new[] { "root" }));
            Assert.That(catalog.Children("root"), Is.EqualTo(new[] { "heat", "pipes" }));
            Assert.That(catalog.Ancestors("heat"), Is.EqualTo(new[] { "root" }));
            Assert.That(catalog.Search("ИНЖЕНЕРИЯ сырье"), Is.EqualTo(new[] { "pipes" }));
            Assert.That(catalog.Search("плазм"), Is.EqualTo(new[] { "heat" }));
            Assert.That(catalog.Search("несуществующая тема"), Is.Empty);
        });
    }

    [Test]
    public void RestrictedAndForcedRootsDoNotAddUnprovidedGuidesOrLoop()
    {
        var catalog = new GuidebookCatalog(new[] { Entry("a", "b", "missing"), Entry("b", "a"), Entry("c") },
            e => e.Id, _ => "", ["a", "c"], "a");
        Assert.Multiple(() =>
        {
            Assert.That(catalog.Roots, Is.EqualTo(new[] { "a" }));
            Assert.That(catalog.Children("a"), Is.EqualTo(new[] { "b", "c" }));
            Assert.That(catalog.Ancestors("b"), Is.EqualTo(new[] { "a" }));
            Assert.That(catalog.Ancestors("a"), Is.Empty);
            Assert.That(catalog.Entries.ContainsKey("missing"), Is.False);
        });
    }

    [Test]
    public void SearchStripsTagsButKeepsVisibleLinksAndCaptions()
    {
        var plain = GuidebookCatalog.PlainText("<Document><!-- hidden -->[bold]Тепло[/bold] " +
            "[textlink=\"Настройка портов\" link=\"Ports\"] <GuideEntityEmbed Entity=\"Id\" Caption=\"Дробилка\"/></Document>");
        Assert.That(plain, Is.EqualTo("Тепло Настройка портов Дробилка"));
    }

    [Test]
    public void TeachingExamplesDoNotConsumeBlockedInputAndRespectBoundaries()
    {
        var example = new GuidebookExampleState();
        Assert.That(example.Crush(false, false), Is.EqualTo(GuidebookExampleState.CrushResult.NoPower));
        Assert.That(example.Crush(true, true), Is.EqualTo(GuidebookExampleState.CrushResult.OutputFull));
        Assert.That(example.Input, Is.EqualTo(3));
        for (var i = 0; i < 3; i++) Assert.That(example.Crush(true, false), Is.EqualTo(GuidebookExampleState.CrushResult.Success));
        Assert.Multiple(() =>
        {
            Assert.That(example.Input, Is.Zero);
            Assert.That(example.Output, Is.EqualTo(6));
            Assert.That(example.Crush(true, false), Is.EqualTo(GuidebookExampleState.CrushResult.NoInput));
            Assert.That(GuidebookExampleState.CanTransfer(true, true), Is.True);
            Assert.That(GuidebookExampleState.CanTransfer(true, false), Is.False);
            Assert.That(GuidebookExampleState.CanStartHeatCycle(399), Is.False);
            Assert.That(GuidebookExampleState.CanStartHeatCycle(400), Is.True);
        });
        example.Reset();
        Assert.That(example.Input, Is.EqualTo(3));
        Assert.That(example.Output, Is.Zero);
    }
}
