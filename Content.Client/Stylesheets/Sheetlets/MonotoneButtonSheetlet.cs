using Content.Client.Stylesheets.SheetletConfigs;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.Stylesheets.Sheetlets;

[CommonSheetlet]
public sealed class MonotoneButtonSheetlet<T> : Sheetlet<T> where T : IButtonConfig
{
    public override StyleRule[] GetRules(T sheet, object config)
    {
        return
        [
            E<MonotoneButton>().Box(ForgeStyle.Button()).Modulate(Color.White),
            E<MonotoneButton>().PseudoHovered().Box(ForgeStyle.Button("_hover")),
            E<MonotoneButton>().PseudoPressed().Box(ForgeStyle.Button("_pressed")),
        ];
    }
}
