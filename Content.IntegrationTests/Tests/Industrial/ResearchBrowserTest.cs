using System.Linq;
using System.Numerics;
using Content.Client._Erida.Research.UI;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared._Erida.Research;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class ResearchBrowserTest : GameTest
{
    [Test]
    public async Task ResearchLanesAreStableNonOverlappingAndFollowLocalPrerequisites()
    {
        await Client.WaitAssertion(() =>
        {
            var technologies = CProtoMan.EnumeratePrototypes<TechnologyPrototype>().Where(t => !t.Hidden).ToDictionary(t => t.ID);
            var positions = ResearchMapLayout.Arrange(technologies.Values);
            var reordered = ResearchMapLayout.Arrange(technologies.Values.Reverse());
            Assert.That(positions.Values.Distinct().Count(), Is.EqualTo(technologies.Count));
            foreach (var (id, position) in positions)
            {
                Assert.That(reordered[id], Is.EqualTo(position), id + " must not jump when availability updates arrive.");
                foreach (var prerequisite in technologies[id].TechnologyPrerequisites)
                    if (technologies.TryGetValue(prerequisite.Id, out var parent) && ResearchMapLayout.Group(parent) == ResearchMapLayout.Group(technologies[id]))
                        Assert.That(position.X, Is.GreaterThan(positions[parent.ID].X), id);
            }
            var path = ResearchMapLayout.PrerequisitePath("IndustrialChipPackaging", technologies);
            Assert.That(path, Does.Contain("IndustrialElectronics").And.Contain("IndustrialOreEnrichment").And.Contain("IndustrialPhotolithography"));
            Assert.That(path, Does.Not.Contain("IndustrialHeavyExtraction").And.Not.Contain("IndustrialPerfectAutomation"));
            Assert.That(FancyResearchConsoleMenu.MatchesResearchSearch("ЧИСТЫЙ кремнезем", ["Чистый кремнезём"]), Is.True);
        });
    }

    [Test]
    public async Task BrowserFindsMachineAndFluidUnlocksAcrossSectionsAndShowsTheirActualResearchPath()
    {
        await Client.WaitAssertion(() =>
        {
            var states = CProtoMan.EnumeratePrototypes<TechnologyPrototype>().Where(t => !t.Hidden)
                .ToDictionary(t => t.ID, _ => ResearchAvailability.Unavailable);
            states["IndustrialElectronics"] = ResearchAvailability.Researched;
            var window = new FancyResearchConsoleMenu();
            window.UpdateTerminalState(new ResearchTerminalState { Username = "review", CanResearch = true });
            window.UpdatePanels(states);
            window.SelectResearchSection(0);
            var search = Find<LineEdit>(window, "Search");
            search.SetText(CProtoMan.Index<EntityPrototype>("IndustrialUpgradeModuleLaserEngraver").Name, true);
            var graph = Find<ResearchesContainerPanel>(window, "DragContainer");
            Assert.That(graph.Children.OfType<FancyResearchConsoleItem>().Any(n => n.Prototype.ID == "IndustrialPhotolithography"), Is.True);
            Assert.That(window.CurrentTech?.Id, Is.EqualTo("IndustrialPhotolithography"), "Search must select a match rather than one of its prerequisites.");
            var reagent = CProtoMan.Index<ReagentPrototype>("IndustrialDeveloper");
            search.SetText(reagent.LocalizedName, true);
            Assert.That(graph.Children.OfType<FancyResearchConsoleItem>().Any(n => n.Prototype.ID == "IndustrialSurfaceChemistry"), Is.True,
                "A liquid-only recipe must be discoverable.");
            window.SetResearchGoal("IndustrialChipPackaging");
            var expected = ResearchMapLayout.PrerequisitePath("IndustrialChipPackaging",
                CProtoMan.EnumeratePrototypes<TechnologyPrototype>().Where(t => !t.Hidden).ToDictionary(t => t.ID));
            Assert.That(graph.Children.OfType<FancyResearchConsoleItem>().Select(n => n.Prototype.ID), Is.EquivalentTo(expected));
            Assert.That(window.CurrentTech?.Id, Is.EqualTo("IndustrialChipPackaging"));
            window.Close();
        });
    }

    [Test]
    public async Task NewResearchBranchesUnlockRealProcessesAndLeaveEarlyDepartmentSupplyShort()
    {
        await Server.WaitAssertion(() =>
        {
            var technologies = SProtoMan.EnumeratePrototypes<TechnologyPrototype>().ToDictionary(t => t.ID);
            var branches = technologies.Values.Where(t => t.Name.ToString().StartsWith("research-branch-"));
            var gates = SProtoMan.EnumeratePrototypes<IndustrialRecipePrototype>().Select(r => r.RequiredTechnology?.Id)
                .Concat(SProtoMan.EnumeratePrototypes<IndustrialFabricationRecipePrototype>().Select(r => r.RequiredTechnology?.Id)).ToHashSet();
            Assert.That(branches.Count(), Is.EqualTo(14));
            foreach (var branch in branches) Assert.That(gates, Does.Contain(branch.ID), branch.ID + " needs a working unlock.");
            foreach (var recipe in new[] { "IndustrialEarlyFlashlights", "IndustrialEarlySmallCells", "IndustrialProduceSalvageDrill", "IndustrialProduceSecurityHelmet" })
            {
                var technology = SProtoMan.Index<IndustrialRecipePrototype>(recipe).RequiredTechnology!.Value.Id;
                var path = ResearchMapLayout.PrerequisitePath(technology, technologies);
                Assert.That(path.Count, Is.LessThanOrEqualTo(2), recipe + " must not wait for the semiconductor factory.");
            }
        });
    }

    private static T Find<T>(Control parent, string name) where T : Control
    {
        foreach (var child in parent.Children)
        {
            if (child.Name == name) return (T) child;
            var nested = Find<T>(child, name);
            if (nested != null) return nested;
        }
        return null!;
    }
}
