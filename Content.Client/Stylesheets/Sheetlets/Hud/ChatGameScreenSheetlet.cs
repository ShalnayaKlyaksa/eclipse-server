using Content.Client.UserInterface.Screens;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.Stylesheets.Sheetlets.Hud;

[CommonSheetlet]
public sealed class ChatGameScreenSheetlet : Sheetlet<PalettedStylesheet>
{
    public override StyleRule[] GetRules(PalettedStylesheet sheet, object config)
    {
        return
        [
            E()
                .Class(SeparatedChatGameScreen.StyleClassChatContainer)
                .Panel(EclipsePanel("#070300F4", "#A85E1290")),
            E<OutputPanel>()
                .Class(SeparatedChatGameScreen.StyleClassChatOutput)
                .Panel(EclipsePanel("#070300F4", "#00000000")),
        ];
    }

    private static StyleBoxFlat EclipsePanel(string background, string border)
    {
        var borderColor = Color.FromHex(border);
        return new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(background),
            BorderColor = borderColor,
            BorderThickness = borderColor.A > 0 ? new Thickness(1) : new Thickness(0),
        };
    }
}
