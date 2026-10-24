using Content.Client.Stylesheets.SheetletConfigs;
using Content.Client.Stylesheets.Stylesheets;
using Content.Client.UserInterface.Systems.Chat.Controls;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.Stylesheets.Sheetlets.Hud;

[CommonSheetlet]
public sealed class ChatSheetlet<T> : Sheetlet<T> where T: PalettedStylesheet, IButtonConfig
{
    public override StyleRule[] GetRules(T sheet, object config)
    {
        IButtonConfig btnCfg = sheet;

        var chatBg = EclipsePanel("#080100F2", "#8A2F12AA", 0, 0);
        var chatLine = EclipsePanel("#070100F4", "#A84B00CC", 8, 4);
        var chatChannelButton = EclipsePanel("#090200F4", "#A84B0066", 9, 4);
        var chatFilterButton = EclipsePanel("#090200F4", "#A84B0088", 4, 4);

        return
        [
            E<PanelContainer>()
                .Class(ChatInputBox.StyleClassChatPanel)
                .Panel(chatBg),
            E<LineEdit>()
                .Class(ChatInputBox.StyleClassChatLineEdit)
                .Prop(LineEdit.StylePropertyStyleBox, chatLine),
            E<LineEdit>()
                .Class(ChatInputBox.StyleClassChatLineEdit)
                .Pseudo(LineEdit.StylePseudoClassPlaceholder)
                .Prop("font-color", Color.FromHex("#AFA49E")),
            E<Button>().Class(ChatInputBox.StyleClassChatFilterOptionButton).Box(chatChannelButton),
            E<ContainerButton>().Class(ChatInputBox.StyleClassChatFilterOptionButton).Box(chatFilterButton),
        ];
    }

    private static StyleBoxFlat EclipsePanel(string background, string border, float horizontalPadding, float verticalPadding)
    {
        var style = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(background),
            BorderColor = Color.FromHex(border),
            BorderThickness = Color.FromHex(border).A > 0 ? new Thickness(1) : new Thickness(0),
        };

        style.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontalPadding);
        style.SetContentMarginOverride(StyleBox.Margin.Vertical, verticalPadding);
        return style;
    }
}
