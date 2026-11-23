using System.Diagnostics.CodeAnalysis;
using Content.Client.Guidebook.Richtext;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Guidebook.Controls;

/// <summary>Optional instructions remain inside the article and do not create another navigation mode.</summary>
public sealed class GuideFoldout : BoxContainer, IDocumentTag
{
    public BoxContainer BodyContents { get; } = new()
    {
        Orientation = LayoutOrientation.Vertical, HorizontalExpand = true,
        Margin = new Thickness(12, 10, 12, 0),
    };

    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        if (!args.TryGetValue("Title", out var title)) return false;
        var body = new CollapsibleBody { HorizontalExpand = true };
        body.AddChild(BodyContents);
        var heading = new CollapsibleHeading("[+] " + title) { ChevronVisible = false, StyleClasses = { "GuidebookFoldout" } };
        body.OnVisibilityChanged += _ => heading.Title = (body.Visible ? "[-] " : "[+] ") + title;
        AddChild(new Collapsible(heading, body) { HorizontalExpand = true });
        HorizontalExpand = true;
        control = this;
        return true;
    }
}
