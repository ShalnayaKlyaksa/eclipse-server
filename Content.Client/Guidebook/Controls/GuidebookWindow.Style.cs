using Content.Client.Stylesheets;
using System.Linq;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.Guidebook.Controls;

public sealed partial class GuidebookWindow
{
    internal static readonly Color Accent = ForgeStyle.Accent;
    internal static readonly Color Ink = ForgeStyle.Text;
    internal static readonly Color Muted = ForgeStyle.Muted;
    private static readonly Color Background = ForgeStyle.Background;
    private static readonly Color Border = ForgeStyle.Border;

    private void ApplyStyles()
    {
        var resources = IoCManager.Resolve<IResourceCache>();
        var font = resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 12);
        var heading = resources.GetFont("/Fonts/NotoSansDisplay/NotoSansDisplay-Bold.ttf", 20);
        var small = resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 10);
        StyleRule[] rules =
        [
            E<Label>().Font(font).FontColor(Ink),
            E<Label>().Class(Button.StyleClassButton).AlignMode(Label.AlignMode.Left),
            E<RichTextLabel>().Font(font),
            E<LineEdit>().Font(font),
            E<Label>().Class("LabelHeadingBigger").Font(heading).FontColor(Accent),
            E<Label>().Class("LabelHeading").Font(resources.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 15)).FontColor(Accent),
            E<Label>().Class("LabelKeyText").FontColor(Accent),
            E<Label>().Class("GuidebookBrand").Font(heading).FontColor(Accent),
            E<Label>().Class("GuidebookMuted").Font(small).FontColor(Muted),
            E<PanelContainer>().Class("GuidebookShell").Panel(Panel(Background, Border, 0)),
            E<PanelContainer>().Class("GuidebookHeader").Panel(Panel("#202020", Border, 14)),
            E<PanelContainer>().Class("GuidebookSidebar").Panel(Panel("#141414", Border, 12)),
            E<PanelContainer>().Class("GuidebookToolbar").Panel(Panel("#202020", Border, 8)),
            E<PanelContainer>().Class("GuidebookFooter").Panel(Panel(Background, Border, 7)),
            E<ContainerButton>().Box(Panel("#202020", Border, 7)).Modulate(Color.White),
            E<ContainerButton>().PseudoHovered().Box(Panel("#303030", Accent, 7)).Modulate(Color.White),
            E<ContainerButton>().PseudoPressed().Box(Panel("#33251B", Accent, 7)).Modulate(Color.White),
            E<ContainerButton>().Class("button").PseudoNormal().Box(Panel("#202020", Border, 7)).Modulate(Color.White),
            E<ContainerButton>().Class("button").PseudoHovered().Box(Panel("#303030", Accent, 7)).Modulate(Color.White),
            E<ContainerButton>().Class("button").PseudoPressed().Box(Panel("#33251B", Accent, 7)).Modulate(Color.White),
            E<ContainerButton>().Class("GuidebookNav").Box(Panel("#141414", Color.Transparent, 7)),
            E<ContainerButton>().Class("GuidebookSelected").Box(Panel("#33251B", Accent, 7)),
            E<ContainerButton>().Class("GuidebookLink").Box(Panel(Color.Transparent, Color.Transparent, 2)),
            E<ContainerButton>().Class("GuidebookLink").Class("button").Box(Panel(Color.Transparent, Color.Transparent, 2)),
            E<ContainerButton>().Class("GuidebookFoldout").Box(Panel("#202020", Border, 8)),
            E<ContainerButton>().Class("GuidebookResult").Box(Panel("#202020", Border, 12)),
            E<ContainerButton>().Class("GuidebookResult").PseudoHovered().Box(Panel("#303030", Accent, 12)),
            E<LineEdit>().Prop(LineEdit.StylePropertyStyleBox, Panel("#141414", Color.FromHex("#505050"), 8)),
            E<ScrollBar>().Prop(ScrollBar.StylePropertyGrabber, new StyleBoxFlat(Color.FromHex("#505050"))
            { ContentMarginLeftOverride = 6, ContentMarginTopOverride = 12 }),
        ];
        Stylesheet = new Stylesheet((UserInterfaceManager.Stylesheet?.Rules ?? []).Concat(rules).ToArray());
    }

    internal static StyleBoxFlat Panel(string background, Color border, int padding) => Panel(Color.FromHex(background), border, padding);
    internal static StyleBoxFlat Panel(Color background, Color border, int padding) => new(background)
    {
        BorderColor = border, BorderThickness = new Thickness(1),
        ContentMarginLeftOverride = padding, ContentMarginRightOverride = padding,
        ContentMarginTopOverride = padding, ContentMarginBottomOverride = padding,
    };
    internal static RichTextLabel Paragraph(string text, Color? color = null)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromUnformatted(text), color ?? Ink);
        return label;
    }
    internal static Button MakeButton(string text, Action action, string style = "")
    {
        var button = new Button { Text = text, ClipText = false, TextAlign = Label.AlignMode.Left, MinHeight = 30 };
        if (style.Length > 0) button.AddStyleClass(style);
        button.OnPressed += _ => action();
        return button;
    }
    private static ContainerButton NavButton(string text, Action action, bool selected = false)
    {
        var button = new ContainerButton
        {
            HorizontalExpand = true, MinHeight = 32, ToolTip = text,
            StyleClasses = { selected ? "GuidebookSelected" : "GuidebookNav" },
        };
        var normal = Panel(selected ? "#33251B" : "#141414", Color.Transparent, 7);
        button.StyleBoxOverride = normal;
        button.ModulateSelfOverride = Color.White;
        button.OnMouseEntered += _ => button.StyleBoxOverride = Panel("#303030", Accent, 7);
        button.OnMouseExited += _ => button.StyleBoxOverride = normal;
        button.AddChild(Paragraph(text, selected ? Accent : Ink));
        button.OnPressed += _ => action();
        return button;
    }
}
