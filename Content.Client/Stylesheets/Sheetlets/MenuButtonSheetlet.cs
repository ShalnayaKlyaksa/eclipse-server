using Content.Client.Stylesheets.Fonts;
using Content.Client.Stylesheets.SheetletConfigs;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.Stylesheets.Sheetlets;

[CommonSheetlet]
public sealed class MenuButtonSheetlet<T> : Sheetlet<T> where T : PalettedStylesheet, IButtonConfig, IIconConfig
{
    public override StyleRule[] GetRules(T sheet, object config)
    {
        var normal = ForgeStyle.HudButton(ResCache, horizontalPadding: 0, verticalPadding: 0);
        var hover = ForgeStyle.HudButton(ResCache, "_hover", 0, 0);
        var pressed = ForgeStyle.HudButton(ResCache, "_pressed", 0, 0);
        return
        [
            E<MenuButton>().Box(normal).Modulate(Color.White),
            E<MenuButton>().Class(StyleClass.ButtonSquare).Box(normal),
            E<MenuButton>().Class(StyleClass.ButtonOpenLeft).Box(normal),
            E<MenuButton>().Class(StyleClass.ButtonOpenRight).Box(normal),
            E<MenuButton>().Class(StyleClass.ButtonOpenBoth).Box(normal),
            E<MenuButton>().PseudoNormal().Box(normal).Modulate(Color.White),
            E<MenuButton>().PseudoHovered().Box(hover).Modulate(Color.White),
            E<MenuButton>().PseudoPressed().Box(pressed).Modulate(Color.White),
            E<MenuButton>().PseudoDisabled().Box(normal).Modulate(Color.Gray),
            E<Label>().Class(MenuButton.StyleClassLabelTopButton)
                .Prop(Label.StylePropertyFont, sheet.BaseFont.GetFont(14, FontKind.Bold)),
        ];
    }

}
