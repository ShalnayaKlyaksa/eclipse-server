using Content.Client.Stylesheets;
using System.Linq;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._Eclipse.Industrial.Orders;

public sealed partial class FabricationOrderWindow
{
    private const string PageHex = "#161616";
    private const string PanelHex = "#202020";
    private const string InputHex = "#141414";
    private const string BorderHex = "#505050";
    private static readonly Color Ink = ForgeStyle.Text;

    private static StyleBoxFlat FieldBox(string fill, string border = BorderHex) => new()
    {
        BackgroundColor = Color.FromHex(fill), BorderColor = Color.FromHex(border), BorderThickness = new Thickness(1),
        ContentMarginLeftOverride = 10, ContentMarginRightOverride = 10, ContentMarginTopOverride = 7, ContentMarginBottomOverride = 7,
    };

    private static StyleBoxFlat TableRow(string fill, bool selected = false) => new()
    {
        BackgroundColor = Color.FromHex(fill), BorderColor = Color.FromHex(selected ? "#FF861F" : BorderHex),
        BorderThickness = new Thickness(selected ? 3 : 0, 0, 0, 1),
        ContentMarginLeftOverride = 10, ContentMarginRightOverride = 10, ContentMarginTopOverride = 7, ContentMarginBottomOverride = 7,
    };

    private void ApplyTheme()
    {
        var resources = IoCManager.Resolve<IResourceCache>();
        var normal = resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 12);
        var small = resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 10);
        var item = resources.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 12);
        var bold = resources.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 14);
        var key = resources.GetFont("/Fonts/RobotoMono/RobotoMono-Regular.ttf", 10);
        StyleRule[] rules =
        [
            E<Label>().Font(normal).Prop(Label.StylePropertyFontColor, Ink), E<RichTextLabel>().Font(normal).Prop("font-color", Ink),
            E<Label>().Class("button").Prop(Label.StylePropertyFontColor, Ink),
            E<Label>().Class(OptionButton.StyleClassOptionButton).Prop(Label.StylePropertyFontColor, Ink),
            E<RichTextLabel>().Class("orders-item-title").Font(item), E<Label>().Class("orders-item-title").Font(item),
            E<RichTextLabel>().Class("orders-small").Font(small), E<Label>().Class("orders-small").Font(small),
            E<Label>().Class("orders-key").Font(key), E<Label>().Class("orders-heading").Font(bold),
            E<Label>().Class("FancyWindowTitle").Font(normal).Prop(Label.StylePropertyFontColor, Ink),
            E<PanelContainer>().Class("BackgroundPanel").Prop(PanelContainer.StylePropertyPanel, FieldBox(PageHex)),
            E<PanelContainer>().Class("WindowHeadingBackground").Prop(PanelContainer.StylePropertyPanel, FieldBox("#202020")),
            E<PanelContainer>().Class("LowDivider").Prop(PanelContainer.StylePropertyPanel, new StyleBoxFlat(Color.FromHex(BorderHex))),
            E<TextureButton>().Class("windowCloseButton").PseudoNormal().Prop(Control.StylePropertyModulateSelf, Ink),
            E<TextureButton>().Class("windowCloseButton").PseudoHovered().Prop(Control.StylePropertyModulateSelf, Color.FromHex("#E0ABA3")),
            E<TextureButton>().Class("windowCloseButton").PseudoPressed().Prop(Control.StylePropertyModulateSelf, Color.FromHex("#C98E86")),
            E<TextureRect>().Class(CheckBox.StyleClassCheckBox).Prop(TextureRect.StylePropertyTexture,
                resources.GetTexture("/Textures/Interface/Nano/Monotone/monotone_checkbox_unchecked.svg.96dpi.png")).Prop(Control.StylePropertyModulateSelf, Ink),
            E<TextureRect>().Class(CheckBox.StyleClassCheckBox).Class(CheckBox.StyleClassCheckBoxChecked).Prop(TextureRect.StylePropertyTexture,
                resources.GetTexture("/Textures/Interface/Nano/Monotone/monotone_checkbox_checked.svg.96dpi.png")).Prop(Control.StylePropertyModulateSelf, Ink),
            E<TextureRect>().Class(OptionButton.StyleClassOptionTriangle).Prop(Control.StylePropertyModulateSelf, Ink),
            E<ContainerButton>().Class("orders-control").Prop(ContainerButton.StylePropertyStyleBox, FieldBox("#202020")).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("orders-control").PseudoHovered().Prop(ContainerButton.StylePropertyStyleBox, FieldBox("#303030", "#505050")).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("orders-control").PseudoPressed().Prop(ContainerButton.StylePropertyStyleBox, TableRow("#33251B", true)).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("orders-product").Prop(ContainerButton.StylePropertyStyleBox, TableRow(PanelHex)).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("orders-product").PseudoHovered().Prop(ContainerButton.StylePropertyStyleBox, TableRow("#303030")).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("orders-product").PseudoPressed().Prop(ContainerButton.StylePropertyStyleBox, TableRow("#33251B", true)).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("orders-primary").Prop(ContainerButton.StylePropertyStyleBox, FieldBox("#FF861F", "#FF861F")).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("orders-primary").PseudoHovered().Prop(ContainerButton.StylePropertyStyleBox, FieldBox("#FFA35A", "#FFA35A")).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("orders-primary").PseudoDisabled().Prop(ContainerButton.StylePropertyStyleBox, FieldBox("#202020", "#202020")).Prop(Control.StylePropertyModulateSelf, Color.White),
            E<LineEdit>().Font(normal).Prop("font-color", Ink).Prop(LineEdit.StylePropertyStyleBox, FieldBox(InputHex)),
            E<LineEdit>().Pseudo(LineEdit.StylePseudoClassPlaceholder).Prop("font-color", Color.FromHex("#A6A6A6")),
            E<LineEdit>().Class(LineEdit.StyleClassLineEditNotEditable).Prop(LineEdit.StylePropertyStyleBox, FieldBox("#161616")).Prop("font-color", Color.FromHex("#A6A6A6")),
            E<TextEdit>().Font(normal).Prop("font-color", Ink).Prop(TextEdit.StylePropertyCursorColor, Ink).Prop(TextEdit.StylePropertySelectionColor, Color.FromHex("#FF861F")),
            E<TextEdit>().Pseudo(TextEdit.StylePseudoClassPlaceholder).Prop("font-color", Color.FromHex("#A6A6A6")),
            E<VScrollBar>().Prop(ScrollBar.StylePropertyGrabber, new StyleBoxFlat(Color.FromHex("#505050"))
                { ContentMarginLeftOverride = 6, ContentMarginRightOverride = 2, ContentMarginTopOverride = 12, ContentMarginBottomOverride = 12 }),
            E<PanelContainer>().Class(OptionButton.StyleClassOptionsBackground).Prop(PanelContainer.StylePropertyPanel, FieldBox(PanelHex)),
        ];
        var stationStyle = IoCManager.Resolve<IUserInterfaceManager>().Stylesheet;
        ((Control) this).Stylesheet = new Stylesheet((stationStyle?.Rules ?? []).Concat(rules).ToArray());
        StyleOption(_category); StyleOption(_department); StyleOption(_status);
    }
}
