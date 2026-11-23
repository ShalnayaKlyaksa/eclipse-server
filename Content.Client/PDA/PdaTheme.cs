using Content.Client.Stylesheets;
using System.Linq;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.PDA;

public static class PdaTheme
{
    public static readonly Color TextColor = ForgeStyle.Text;

    public static Stylesheet Create(Stylesheet? stationStyle)
    {
        var resources = IoCManager.Resolve<IResourceCache>();
        var font = resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 10);
        var caption = resources.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 10);
        var title = resources.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 13);
        var small = resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 9);
        StyleRule[] rules =
        [
            E<Label>().Font(font).Prop(Label.StylePropertyFontColor, TextColor),
            E<RichTextLabel>().Font(font),
            E<Label>().Class("pda-brand").Font(caption).Prop(Label.StylePropertyFontColor, Color.FromHex("#A6A6A6")),
            E<Label>().Class("pda-title").Font(title),
            E<RichTextLabel>().Class("pda-title").Font(title),
            E<Label>().Class("pda-balance").Font(resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 24)).Prop(Label.StylePropertyFontColor, Color.FromHex("#FF861F")),
            E<Label>().Class("pda-positive").Prop(Label.StylePropertyFontColor, Color.FromHex("#92C7B0")),
            E<Label>().Class("pda-caption").Font(caption),
            E<Label>().Class("LabelHeading").Font(caption),
            E<Label>().Class("LabelBig").Font(title),
            E<Label>().Class("LabelSubText").Font(small),
            E<Label>().Class("terminal-heading").Font(caption),
            E<RichTextLabel>().Class("terminal-heading").Font(caption),
            E<Label>().Class("terminal-muted").Font(small).Prop(Label.StylePropertyFontColor, Color.FromHex("#A6A6A6")),
            // The station theme defines a :normal rule too. Override it with the same padding as hover.
            E<ContainerButton>().Class("button").PseudoNormal().Prop(ContainerButton.StylePropertyStyleBox, Box("#202020", "#505050", 3)).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("button").PseudoHovered().Prop(ContainerButton.StylePropertyStyleBox, Box("#303030", "#707070", 3)).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("button").PseudoPressed().Prop(ContainerButton.StylePropertyStyleBox, Box("#33251B", "#FF861F", 3)).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("button").PseudoDisabled().Prop(ContainerButton.StylePropertyStyleBox, Box("#161616", "#505050", 3)).Prop(Control.StylePropertyModulateSelf, Color.FromHex("#A6A6A6")),
            E<ContainerButton>().Class("button", "pda-copy").PseudoNormal().Prop(ContainerButton.StylePropertyStyleBox, Box("#00000000", "#00000000", 3)),
            E<ContainerButton>().Class("button", "pda-primary").PseudoNormal().Prop(ContainerButton.StylePropertyStyleBox, Box("#33251B", "#FF861F", 4)),
            E<ContainerButton>().Class("button", "pda-primary").PseudoHovered().Prop(ContainerButton.StylePropertyStyleBox, Box("#303030", "#FF861F", 4)),
            E<ContainerButton>().Class("button", "pda-primary").PseudoPressed().Prop(ContainerButton.StylePropertyStyleBox, Box("#33251B", "#FF861F", 4)),
            E<ContainerButton>().Class("button", "pda-primary").PseudoDisabled().Prop(ContainerButton.StylePropertyStyleBox, Box("#161616", "#505050", 4)),
            E<PdaSettingsButton>().PseudoNormal().Prop(PdaSettingsButton.StylePropertyBgColor, Color.FromHex("#202020")).Prop(PdaSettingsButton.StylePropertyFgColor, TextColor),
            E<PdaSettingsButton>().PseudoHovered().Prop(PdaSettingsButton.StylePropertyBgColor, Color.FromHex("#303030")).Prop(PdaSettingsButton.StylePropertyFgColor, TextColor),
            E<PdaSettingsButton>().PseudoPressed().Prop(PdaSettingsButton.StylePropertyBgColor, Color.FromHex("#33251B")).Prop(PdaSettingsButton.StylePropertyFgColor, TextColor),
            E<LineEdit>().Font(font).Prop("font-color", TextColor).Prop(LineEdit.StylePropertyStyleBox, Box("#141414", "#505050", 3)),
            E<TextEdit>().Font(font).Prop("font-color", TextColor),
            E<PanelContainer>().Class("pda-header").Prop(PanelContainer.StylePropertyPanel, Box("#202020", "#505050")),
            E<PanelContainer>().Class("pda-divider").Prop(PanelContainer.StylePropertyPanel, new StyleBoxFlat(Color.FromHex("#505050"))),
            E<PanelContainer>().Class("pda-editor").Prop(PanelContainer.StylePropertyPanel, Box("#141414", "#505050", radius: 3)),
            E<PanelContainer>().Class("BackgroundDark").Prop(PanelContainer.StylePropertyPanel, Box("#141414", "#505050", radius: 8)),
            E<PanelContainer>().Class("pda-mail").Prop(PanelContainer.StylePropertyPanel, Box("#202020", "#505050", 10)),
            E<PanelContainer>().Class("pda-taskbar").Prop(PanelContainer.StylePropertyPanel, Box("#202020", "#505050")),
            E<PanelContainer>().Class("windowPanel").Prop(PanelContainer.StylePropertyPanel, Box("#161616", "#505050", radius: 8)),
            E<PanelContainer>().Class("windowHeader").Prop(PanelContainer.StylePropertyPanel, Box("#202020", "#505050", 4)),
            E<Label>().Class("windowTitle").Font(caption).Prop(Label.StylePropertyFontColor, TextColor),
            E<Label>().Class("pda-rail").Font(resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 7)).Prop(Label.StylePropertyFontColor, Color.FromHex("#A6A6A6")),
            E<VScrollBar>().Prop(ScrollBar.StylePropertyGrabber, Box("#505050", "#505050", 3)),
            E<VScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, Box("#FF861F", "#FF861F", 3)),
        ];
        return new Stylesheet((stationStyle?.Rules ?? Array.Empty<StyleRule>()).Concat(rules).ToArray());
    }

    public static StyleBox Box(string background, string border, int padding = 0, float radius = 5) =>
        new RoundedSurface(IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>("/Textures/_Eclipse/Interface/PDA/rounded-surface.png").Texture,
            Color.FromHex(background), Color.FromHex(border), padding, radius);

    // Reuse the engine's antialiased nine-patch: screen controls and the enclosure share real curved edges.
    private sealed class RoundedSurface : StyleBox
    {
        private readonly StyleBoxTexture _edge;
        private readonly StyleBoxTexture _fill;
        public RoundedSurface(Texture texture, Color fill, Color edge, int padding, float radius)
        {
            _edge = Patch(texture, edge, radius);
            _fill = Patch(texture, fill, Math.Max(1, radius - 1));
            ContentMarginLeftOverride = ContentMarginRightOverride = padding;
            ContentMarginTopOverride = ContentMarginBottomOverride = padding;
        }
        private static StyleBoxTexture Patch(Texture texture, Color color, float radius)
        {
            var patch = new StyleBoxTexture { Texture = texture, Modulate = color, TextureScale = new System.Numerics.Vector2(radius / 5) };
            patch.SetPatchMargin(Margin.All, 5);
            return patch;
        }
        protected override float GetDefaultContentMargin(Margin margin) => 0;
        protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
        {
            _edge.Draw(handle, box, uiScale);
            if (box.Width > 2 * uiScale && box.Height > 2 * uiScale)
                _fill.Draw(handle, new UIBox2(box.TopLeft + new System.Numerics.Vector2(uiScale), box.BottomRight - new System.Numerics.Vector2(uiScale)), uiScale);
        }
    }
}


