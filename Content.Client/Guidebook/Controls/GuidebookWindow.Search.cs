using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Guidebook;
using Content.Shared.Kitchen;
using Robust.Shared.Prototypes;

namespace Content.Client.Guidebook.Controls;

public sealed partial class GuidebookWindow
{
    private static readonly Regex GuideTagPattern = new(@"<(Guide\w+)\s+([^>]+)>", RegexOptions.Singleline);
    private static readonly Regex AttributePattern = new("(\\w+)=\"([^\"]*)\"");

    private string GetDocument(GuideEntry entry)
    {
        if (_documents.TryGetValue(entry.Text, out var document)) return document;
        try
        {
            using var reader = _resourceManager.ContentFileReadText(entry.Text);
            document = reader.ReadToEnd();
        }
        catch (IOException e)
        {
            _sawmill.Warning($"Could not read guide entry {entry.Id}: {e.Message}");
            document = "";
        }
        _documents[entry.Text] = document;
        return document;
    }

    private string SearchText(GuideEntry entry)
    {
        var markup = GetDocument(entry);
        var result = new StringBuilder(GuidebookCatalog.PlainText(markup));
        // Index generated cards without constructing/spawning every guidebook control just to search.
        foreach (Match tag in GuideTagPattern.Matches(markup))
        {
            var attributes = AttributePattern.Matches(tag.Groups[2].Value)
                .Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            switch (tag.Groups[1].Value)
            {
                case "GuideEntityEmbed" when attributes.TryGetValue("Entity", out var id):
                    if (_prototypes.TryIndex<EntityPrototype>(id, out var entity))
                        result.Append(' ').Append(entity.Name).Append(' ').Append(entity.Description);
                    break;
                case "GuideReagentEmbed" when attributes.TryGetValue("Reagent", out var id):
                    if (_prototypes.TryIndex<ReagentPrototype>(id, out var reagent))
                        result.Append(' ').Append(reagent.LocalizedName);
                    break;
                case "GuideReagentGroupEmbed" when attributes.TryGetValue("Group", out var group):
                    foreach (var member in _prototypes.EnumeratePrototypes<ReagentPrototype>().Where(p => p.Group == group))
                        result.Append(' ').Append(member.LocalizedName);
                    break;
                case "GuideMicrowaveGroupEmbed" when attributes.TryGetValue("Group", out var group):
                    foreach (var recipe in _prototypes.EnumeratePrototypes<FoodRecipePrototype>().Where(p => p.Group == group))
                        if (_prototypes.TryIndex<EntityPrototype>(recipe.Result, out var product))
                            result.Append(' ').Append(product.Name);
                    break;
            }
        }
        return result.ToString();
    }
}
