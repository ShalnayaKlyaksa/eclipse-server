using Content.Client.Stylesheets.SheetletConfigs;
using Content.Client.Stylesheets.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.Stylesheets.Sheetlets;

[CommonSheetlet]
public sealed class LineEditSheetlet<T> : Sheetlet<T> where T : PalettedStylesheet, ILineEditConfig
{
    public override StyleRule[] GetRules(T sheet, object config)
    {
        var lineEditStylebox = SearchStyle("#20202B", "#7A5A46", "#A45B12");
        var lineEditNotEditable = SearchStyle("#17161D", "#4A3B33", "#00000000");

        return
        [
            E<LineEdit>()
                .Prop(LineEdit.StylePropertyStyleBox, lineEditStylebox),
            E<LineEdit>()
                .Class(LineEdit.StyleClassLineEditNotEditable)
                .Prop(LineEdit.StylePropertyStyleBox, lineEditNotEditable)
                .Prop("font-color", Color.FromHex("#9F948D")),
            E<LineEdit>()
                .Pseudo(LineEdit.StylePseudoClassPlaceholder)
                .Prop("font-color", Color.FromHex("#9F948D")),
            E<TextEdit>()
                .Pseudo(TextEdit.StylePseudoClassPlaceholder)
                .Prop("font-color", Color.FromHex("#9F948D")),
        ];
    }

    private static StyleBoxFlat SearchStyle(string background, string border, string accent)
    {
        var style = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(background),
            BorderColor = Color.FromHex(border),
            BorderThickness = new Thickness(1),
        };

        var accentColor = Color.FromHex(accent);
        if (accentColor.A > 0)
        {
            style.BorderThickness = new Thickness(1, 1, 1, 2);
            style.BorderColor = accentColor;
        }

        style.SetContentMarginOverride(StyleBox.Margin.Horizontal, 7);
        style.SetContentMarginOverride(StyleBox.Margin.Vertical, 4);
        return style;
    }
}
