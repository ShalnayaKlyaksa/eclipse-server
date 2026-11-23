using Content.Client.Stylesheets.SheetletConfigs;
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
        var chatBg = ForgeStyle.Panel(ForgeStyle.Background, ForgeStyle.Border);
        var chatLine = ForgeStyle.Panel(ForgeStyle.Input, ForgeStyle.Border, 8, 4);
        var chatChannelButton = ForgeStyle.Button(horizontalPadding: 9, verticalPadding: 4);
        var chatFilterButton = ForgeStyle.Button(horizontalPadding: 4, verticalPadding: 4);
        var hover = ForgeStyle.Button("_hover", 9, 4);
        var pressed = ForgeStyle.Button("_pressed", 9, 4);

        return
        [
            E<PanelContainer>()
                .Class(ChatInputBox.StyleClassChatPanel)
                .Panel(chatBg),
            E<ChatInputBox>().Class(ChatInputBox.StyleClassChatPanel)
                .Panel(ForgeStyle.Panel(Color.Transparent, Color.Transparent)),
            E<OutputPanel>().Class("ForgeChatOutput")
                .Prop(OutputPanel.StylePropertyStyleBox, ForgeStyle.Panel(Color.Transparent, Color.Transparent)),
            E<LineEdit>()
                .Class(ChatInputBox.StyleClassChatLineEdit)
                .Prop(LineEdit.StylePropertyStyleBox, chatLine),
            E<LineEdit>()
                .Class(ChatInputBox.StyleClassChatLineEdit)
                .Pseudo(LineEdit.StylePseudoClassPlaceholder)
                .Prop("font-color", ForgeStyle.Muted),
            E<ChannelSelectorButton>().Class(ContainerButton.StyleClassButton, ChannelSelectorItemButton.StyleClassChatSelectorOptionButton).Box(chatChannelButton).Modulate(Color.White),
            E<ChannelSelectorButton>().Class(ContainerButton.StyleClassButton, ChannelSelectorItemButton.StyleClassChatSelectorOptionButton).PseudoNormal().Box(chatChannelButton).Modulate(Color.White),
            E<ChannelSelectorButton>().Class(ContainerButton.StyleClassButton, ChannelSelectorItemButton.StyleClassChatSelectorOptionButton).PseudoHovered().Box(hover).Modulate(Color.White),
            E<ChannelSelectorButton>().Class(ContainerButton.StyleClassButton, ChannelSelectorItemButton.StyleClassChatSelectorOptionButton).PseudoPressed().Box(pressed).Modulate(Color.White),
            E<Button>().Class(ContainerButton.StyleClassButton, ChatInputBox.StyleClassChatFilterOptionButton).Box(chatChannelButton),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton, ChatInputBox.StyleClassChatFilterOptionButton).Box(chatFilterButton),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton, ChatInputBox.StyleClassChatFilterOptionButton).Modulate(Color.White),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton, ChatInputBox.StyleClassChatFilterOptionButton).PseudoHovered().Box(hover),
            E<ContainerButton>().Class(ContainerButton.StyleClassButton, ChatInputBox.StyleClassChatFilterOptionButton).PseudoPressed().Box(pressed),
            E<ChannelFilterButton>().Class(ContainerButton.StyleClassButton, ChatInputBox.StyleClassChatFilterOptionButton)
                .PseudoHovered().Box(ForgeStyle.Button("_hover", 4, 4)),
            E<ChannelFilterButton>().Class(ContainerButton.StyleClassButton, ChatInputBox.StyleClassChatFilterOptionButton)
                .PseudoPressed().Box(ForgeStyle.Button("_pressed", 4, 4)),
            E<Button>().Class(ContainerButton.StyleClassButton, "ForgeChatPreset").PseudoNormal().Box(chatChannelButton).Modulate(Color.White),
            E<Button>().Class(ContainerButton.StyleClassButton, "ForgeChatPreset").PseudoHovered().Box(hover),
            E<Button>().Class(ContainerButton.StyleClassButton, "ForgeChatPreset").PseudoPressed().Box(pressed),
            E<Button>().Class(ContainerButton.StyleClassButton, "ForgeChatPreset").ParentOf(E<Label>()).FontColor(ForgeStyle.Text),
        ];
    }

}
