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
        var lineEditStylebox = ForgeStyle.Panel(ForgeStyle.Input, ForgeStyle.Border, 7, 4);
        var lineEditNotEditable = ForgeStyle.Panel(ForgeStyle.Background, ForgeStyle.Border, 7, 4);

        return
        [
            E<LineEdit>()
                .Prop(LineEdit.StylePropertyStyleBox, lineEditStylebox),
            E<LineEdit>()
                .Class(LineEdit.StyleClassLineEditNotEditable)
                .Prop(LineEdit.StylePropertyStyleBox, lineEditNotEditable)
                .Prop("font-color", ForgeStyle.Muted),
            E<LineEdit>()
                .Pseudo(LineEdit.StylePseudoClassPlaceholder)
                .Prop("font-color", ForgeStyle.Muted),
            E<TextEdit>()
                .Pseudo(TextEdit.StylePseudoClassPlaceholder)
                .Prop("font-color", ForgeStyle.Muted),
        ];
    }

}
