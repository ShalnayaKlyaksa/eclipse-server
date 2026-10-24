using Content.Client.Stylesheets.SheetletConfigs;
using Content.Client.Stylesheets.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.Stylesheets.Sheetlets;

[CommonSheetlet]
public sealed class TabContainerSheetlet<T> : Sheetlet<T> where T: PalettedStylesheet, ITabContainerConfig
{
    public override StyleRule[] GetRules(T sheet, object config)
    {
        var tabContainerPanel = EclipsePanel("#070300F4", "#A85E1290", 8, 8);
        var tabContainerBoxActive = EclipsePanel("#1A0900E8", "#A85E1270", 8, 2);
        var tabContainerBoxInactive = EclipsePanel("#00000000", "#00000000", 8, 2);

        return
        [
            E<TabContainer>()
                .Prop(TabContainer.StylePropertyPanelStyleBox, tabContainerPanel)
                .Prop(TabContainer.StylePropertyTabStyleBox, tabContainerBoxActive)
                .Prop(TabContainer.StylePropertyTabStyleBoxInactive, tabContainerBoxInactive),
        ];
    }

    private static StyleBoxFlat EclipsePanel(string background, string border, float horizontalPadding, float verticalPadding)
    {
        var borderColor = Color.FromHex(border);
        var style = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(background),
            BorderColor = borderColor,
            BorderThickness = borderColor.A > 0 ? new Thickness(1) : new Thickness(0),
        };

        style.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontalPadding);
        style.SetContentMarginOverride(StyleBox.Margin.Vertical, verticalPadding);
        return style;
    }
}
