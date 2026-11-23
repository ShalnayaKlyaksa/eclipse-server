using System.Linq;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;
namespace Content.Client._Erida.Research.UI;
public static class ResearchTerminalTheme
{
    public static Stylesheet Create(Stylesheet? stationStyle)
    {
        var resources = IoCManager.Resolve<IResourceCache>();
        var font = resources.GetFont("/Fonts/RobotoMono/RobotoMono-Regular.ttf", 11);
        var heading = resources.GetFont("/Fonts/RobotoMono/RobotoMono-Bold.ttf", 12);
        var green = Color.FromHex("#EEEEEE");
        var summary = resources.GetFont("/Fonts/RobotoMono/RobotoMono-Regular.ttf", 8);
        var small = resources.GetFont("/Fonts/RobotoMono/RobotoMono-Regular.ttf", 9);
        StyleRule[] rules =
        [
            E<Label>().Font(font).Prop(Label.StylePropertyFontColor, green),
            E<RichTextLabel>().Font(font),
            E<Label>().Class("terminal-heading").Font(heading).Prop(Label.StylePropertyFontColor, Color.FromHex("#FF861F")),
            E<RichTextLabel>().Class("terminal-heading").Font(heading),
            E<Label>().Class("terminal-muted").Font(small).Prop(Label.StylePropertyFontColor, Color.FromHex("#A6A6A6")),
            E<RichTextLabel>().Class("terminal-node").Font(small),
            E<Label>().Class("terminal-node").Font(small),
            E<Label>().Class("terminal-summary").Font(summary),
            E<VScrollBar>().Prop(ScrollBar.StylePropertyGrabber, TerminalBox("#505050", "#505050")),
            E<HScrollBar>().Prop(ScrollBar.StylePropertyGrabber, TerminalBox("#505050", "#505050")),
            E<VScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, TerminalBox("#707070", "#707070")),
            E<HScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, TerminalBox("#707070", "#707070")),
            E<VScrollBar>().PseudoPressed().Prop(ScrollBar.StylePropertyGrabber, TerminalBox("#FF861F", "#FF861F")),
            E<HScrollBar>().PseudoPressed().Prop(ScrollBar.StylePropertyGrabber, TerminalBox("#FF861F", "#FF861F")),
            E<LineEdit>().Class("terminal-password").Prop("font-color", Color.Transparent),
            E<LineEdit>().Font(font).Prop("font-color", green)
                .Prop(LineEdit.StylePropertyStyleBox, TerminalBox("#141414", "#505050")),
            E<Label>().Class("LabelKeyText").Font(font).Prop(Label.StylePropertyFontColor, Color.FromHex("#FF861F")),
            E<Label>().Class("FancyWindowTitle").Font(font).Prop(Label.StylePropertyFontColor, green),
            E<PanelContainer>().Class("BackgroundPanel").Prop(PanelContainer.StylePropertyPanel, TerminalBox("#161616", "#505050")),
            E<PanelContainer>().Class("WindowHeadingBackground").Prop(PanelContainer.StylePropertyPanel, TerminalBox("#202020", "#505050")),
            E<ContainerButton>().Class("terminal-control").Prop(ContainerButton.StylePropertyStyleBox, TerminalBox("#202020", "#505050"))
                .Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("terminal-control").PseudoHovered().Prop(ContainerButton.StylePropertyStyleBox, TerminalBox("#303030", "#707070"))
                .Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("terminal-control").PseudoPressed().Prop(ContainerButton.StylePropertyStyleBox, TerminalBox("#33251B", "#FF861F"))
                .Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("terminal-control").PseudoDisabled().Prop(ContainerButton.StylePropertyStyleBox, TerminalBox("#141414", "#505050")),
            E<PanelContainer>().Class(OptionButton.StyleClassOptionsBackground).Prop(PanelContainer.StylePropertyPanel, TerminalBox("#141414", "#505050")),
        ];
        return new Stylesheet((stationStyle?.Rules ?? Array.Empty<StyleRule>()).Concat(rules).ToArray());
    }
    public static void StyleButtons(Control parent)
    {
        if (parent is ContainerButton and not DrawButton) parent.AddStyleClass("terminal-control");
        foreach (var child in parent.Children) StyleButtons(child);
    }
    private static StyleBoxFlat TerminalBox(string background, string border) => new()
    {
        BackgroundColor = Color.FromHex(background), BorderColor = Color.FromHex(border), BorderThickness = new Thickness(1),
        ContentMarginLeftOverride = 8, ContentMarginRightOverride = 8,
        ContentMarginTopOverride = 4, ContentMarginBottomOverride = 4,
    };
}
