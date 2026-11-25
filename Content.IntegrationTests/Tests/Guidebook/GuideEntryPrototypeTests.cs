using System.Linq;
using Content.Client.Guidebook;
using Content.Client.Guidebook.Controls;
using Content.Client.Guidebook.Richtext;
using Content.Client.UserInterface.ControlExtensions;
using Content.IntegrationTests.Fixtures;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Content.IntegrationTests.Utility;
using Content.Shared.Guidebook;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests.Guidebook;

[TestFixture]
[TestOf(typeof(GuidebookSystem))]
[TestOf(typeof(GuideEntryPrototype))]
[TestOf(typeof(DocumentParsingManager))]
public sealed class GuideEntryPrototypeTests : GameTest
{
    private static string[] _guideEntries = GameDataScrounger.PrototypesOfKind<GuideEntryPrototype>();

    [Test]
    public async Task SpeciesOverviewShowsOneCardAndArticlePerPlayableSpecies()
    {
        await Client.WaitAssertion(() =>
        {
            var prototypes = Client.ResolveDependency<IPrototypeManager>();
            var parser = Client.ResolveDependency<DocumentParsingManager>();
            var playable = prototypes.EnumeratePrototypes<SpeciesPrototype>()
                .Where(species => species.RoundStart && species.VariantOf == null).ToArray();
            var overview = prototypes.Index<GuideEntryPrototype>(new ProtoId<GuideEntryPrototype>("Species"));
            Assert.That(overview.OpenArticleFromCatalog, Is.True);

            using var document = new Document();
            Assert.That(parser.TryAddMarkup(document, new ProtoId<GuideEntryPrototype>(overview.ID)), Is.True);
            var cards = document.GetControlOfType<GuideSpeciesCard>(true).Select(card => card.ToolTip);
            Assert.That(cards, Is.EquivalentTo(playable.Select(species => Loc.GetString(species.Name))));

            foreach (var species in playable)
            {
                var article = new ProtoId<GuideEntryPrototype>(species.ID);
                Assert.That(overview.Children, Does.Contain(article));
                using var articleDocument = new Document();
                Assert.That(parser.TryAddMarkup(articleDocument, article), Is.True, species.ID);
            }
        });
    }

    [Test]
    [TestCaseSource(nameof(_guideEntries))]
    [Description("Ensures a given guidebook entry is valid, checking the document/etc.")]
    public async Task Validate(string protoKey)
    {
        var pair = Pair;
        var client = pair.Client;
        await client.WaitIdleAsync();
        var protoMan = client.ResolveDependency<IPrototypeManager>();
        var resMan = client.ResolveDependency<IResourceManager>();
        var parser = client.ResolveDependency<DocumentParsingManager>();
        var proto = protoMan.Index<GuideEntryPrototype>(protoKey);

        await client.WaitAssertion(() =>
        {
            using var reader = resMan.ContentFileReadText(proto.Text);
            var text = reader.ReadToEnd();

            Assert.That(parser.TryAddMarkup(new Document(), text), $"Failed to parse the guide entry's document.");
        });
    }
}
