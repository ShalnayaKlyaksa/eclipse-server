using Content.Client.Stylesheets;
using Content.Client.Stylesheets.Fonts;
using Content.Client.Stylesheets.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client.MainMenu.UI;

[CommonSheetlet]
public sealed class MainMenuSheetlet : Sheetlet<NanotrasenStylesheet>
{
    public override StyleRule[] GetRules(NanotrasenStylesheet sheet, object config)
    {
        var rules = new List<StyleRule>
        {
            // make those buttons bigger
            E<Button>()
                .Identifier(MainMenuControl.StyleIdentifierMainMenu)
                .ParentOf(E<Label>())
                .Font(sheet.BaseFont.GetFont(16, FontKind.Bold)),
            E<BoxContainer>()
                .Identifier(MainMenuControl.StyleIdentifierMainMenuVBox)
                .Prop(BoxContainer.StylePropertySeparation, 2),

            E<Label>().Identifier(MainMenuControl.StyleIdentifierLogoText)
                .Font(sheet.BaseFont.GetFont(22, FontKind.Bold))
                .FontColor(Color.FromHex("#F2A65C")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierLogoTitle)
                .Font(sheet.BaseFont.GetFont(25, FontKind.Bold))
                .FontColor(Color.FromHex("#F2A65C")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierLogoSub)
                .Font(sheet.BaseFont.GetFont(14, FontKind.Bold))
                .FontColor(Color.FromHex("#B0B0B0")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierNavTitle)
                .Font(sheet.BaseFont.GetFont(14, FontKind.Bold))
                .FontColor(Color.FromHex("#F0F0F0")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierNavSubtitle)
                .Font(sheet.BaseFont.GetFont(11))
                .FontColor(Color.FromHex("#A8A8A8")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierFooterTitle)
                .Font(sheet.BaseFont.GetFont(13, FontKind.Bold))
                .FontColor(Color.FromHex("#E5E5E5")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierHeaderGold)
                .Font(sheet.BaseFont.GetFont(15, FontKind.Bold))
                .FontColor(Color.FromHex("#F0F0F0")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierGoldSmall)
                .Font(sheet.BaseFont.GetFont(12, FontKind.Bold))
                .FontColor(Color.FromHex("#F2A65C")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierText)
                .Font(sheet.BaseFont.GetFont(13, FontKind.Bold))
                .FontColor(Color.FromHex("#E5E5E5")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierSubtle)
                .Font(sheet.BaseFont.GetFont(11))
                .FontColor(Color.FromHex("#A8A8A8")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierTinySubtle)
                .Font(sheet.BaseFont.GetFont(10))
                .FontColor(Color.FromHex("#BFBFBF")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierSecondaryTitle)
                .Font(sheet.BaseFont.GetFont(12, FontKind.Bold))
                .FontColor(Color.FromHex("#BFBFBF")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierScore)
                .Font(sheet.BaseFont.GetFont(15, FontKind.Bold))
                .FontColor(Color.FromHex("#FFFFFF")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierRankIcon)
                .Font(sheet.BaseFont.GetFont(17, FontKind.Bold))
                .FontColor(Color.FromHex("#D5C9AE")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierServerIcon)
                .Font(sheet.BaseFont.GetFont(18, FontKind.Bold))
                .FontColor(Color.FromHex("#F2A65C")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierStatusIcon)
                .Font(sheet.BaseFont.GetFont(16, FontKind.Bold))
                .FontColor(Color.FromHex("#00C9A6")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierSecondaryIcon)
                .Font(sheet.BaseFont.GetFont(16, FontKind.Bold))
                .FontColor(Color.FromHex("#A8A8A8")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierTab)
                .Font(sheet.BaseFont.GetFont(12, FontKind.Bold))
                .FontColor(Color.FromHex("#BFBFBF")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierTabActive)
                .Font(sheet.BaseFont.GetFont(12, FontKind.Bold))
                .FontColor(Color.FromHex("#F2A65C")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierWarning)
                .Font(sheet.BaseFont.GetFont(12, FontKind.Bold))
                .FontColor(Color.FromHex("#FF9A78")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierCompactHeaderGold)
                .Font(sheet.BaseFont.GetFont(14, FontKind.Bold))
                .FontColor(Color.FromHex("#F2A65C")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierCompactText)
                .Font(sheet.BaseFont.GetFont(12, FontKind.Bold))
                .FontColor(Color.FromHex("#E5E5E5")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierCompactSubtle)
                .Font(sheet.BaseFont.GetFont(11))
                .FontColor(Color.FromHex("#A8A8A8")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierRoadmapTitle)
                .Font(sheet.BaseFont.GetFont(31, FontKind.Bold))
                .FontColor(Color.FromHex("#F0F0F0")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierRoadmapSubtitle)
                .Font(sheet.BaseFont.GetFont(14, FontKind.Bold))
                .FontColor(Color.FromHex("#BEBEBE")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierRoadmapNumber)
                .Font(sheet.BaseFont.GetFont(25, FontKind.Bold))
                .FontColor(Color.FromHex("#F2A65C")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierRoadmapItemTitle)
                .Font(sheet.BaseFont.GetFont(15, FontKind.Bold))
                .FontColor(Color.FromHex("#F2A65C")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierRoadmapDot)
                .Font(sheet.BaseFont.GetFont(18, FontKind.Bold))
                .FontColor(Color.FromHex("#F2A65C")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierRoadmapDotActive)
                .Font(sheet.BaseFont.GetFont(19, FontKind.Bold))
                .FontColor(Color.FromHex("#F2A65C")),
        };

        var surface = ButtonBox("#242424", "#383838", 1, 1, 1, 1);
        var hover = ButtonBox("#393939", "#505050", 1, 1, 1, 1);
        var selected = ButtonBox("#303030", "#F2A65C", 2, 0, 0, 0);
        var selectedHover = ButtonBox("#414141", "#F2A65C", 2, 0, 0, 0);
        var pressed = ButtonBox("#414141", "#B17A48", 1, 1, 1, 1);
        var navigation = ButtonBox("#1C1C1C", "#383838", 0, 0, 0, 0);
        var tab = ButtonBox("#1C1C1C", "#383838", 0, 0, 0, 0);
        var selectedTab = ButtonBox("#303030", "#F2A65C", 0, 0, 0, 2);

        AddButtonRules(rules, MainMenuControl.StyleIdentifierPrimary, selected, selectedHover, pressed);
        AddButtonRules(rules, MainMenuControl.StyleIdentifierNav, navigation, hover, pressed);
        AddButtonRules(rules, MainMenuControl.StyleIdentifierLobbyNav, ButtonBox("#0C0C0C", "#262626", 0, 0, 0, 0), hover, pressed);
        AddButtonRules(rules, MainMenuControl.StyleIdentifierFooter, navigation, hover, pressed);
        AddButtonRules(rules, MainMenuControl.StyleIdentifierSecondary, surface, hover, pressed);
        AddButtonRules(rules, MainMenuControl.StyleIdentifierTabButton, tab, hover, pressed);
        AddButtonRules(rules, MainMenuControl.StyleIdentifierTabButtonActive, selectedTab, hover, pressed);
        AddButtonRules(rules, MainMenuControl.StyleIdentifierCompactButton, surface, hover, pressed);

        AddButtonLabelRules(rules, MainMenuControl.StyleIdentifierPrimary, sheet.BaseFont.GetFont(13, FontKind.Bold));
        AddButtonLabelRules(rules, MainMenuControl.StyleIdentifierNav, sheet.BaseFont.GetFont(13, FontKind.Bold));
        AddButtonLabelRules(rules, MainMenuControl.StyleIdentifierFooter, sheet.BaseFont.GetFont(12, FontKind.Bold));
        AddButtonLabelRules(rules, MainMenuControl.StyleIdentifierSecondary, sheet.BaseFont.GetFont(12, FontKind.Bold));
        AddButtonLabelRules(rules, MainMenuControl.StyleIdentifierTabButton, sheet.BaseFont.GetFont(11, FontKind.Bold));
        AddButtonLabelRules(rules, MainMenuControl.StyleIdentifierTabButtonActive, sheet.BaseFont.GetFont(11, FontKind.Bold));
        AddButtonLabelRules(rules, MainMenuControl.StyleIdentifierCompactButton, sheet.BaseFont.GetFont(11, FontKind.Bold));
        AddButtonTextHighlightRules(rules);

        return rules.ToArray();
    }

    private static void AddButtonTextHighlightRules(List<StyleRule> rules)
    {
        AddButtonTextHighlightRules<Button>(rules);
        AddButtonTextHighlightRules<ContainerButton>(rules);
        AddButtonTextHighlightRules<OptionButton>(rules);
    }

    private static void AddButtonTextHighlightRules<T>(List<StyleRule> rules)
        where T : Control
    {
        var titleGold = Color.FromHex("#F2A65C");
        var subtitleLight = Color.FromHex("#D9D9D9");

        rules.AddRange([
            E<T>().Identifier(MainMenuControl.StyleIdentifierNav).PseudoHovered()
                .ParentOf(E<Label>().Identifier(MainMenuControl.StyleIdentifierNavTitle))
                .FontColor(titleGold),
            E<T>().Identifier(MainMenuControl.StyleIdentifierNav).PseudoHovered()
                .ParentOf(E<Label>().Identifier(MainMenuControl.StyleIdentifierNavSubtitle))
                .FontColor(subtitleLight),
            E<T>().Identifier(MainMenuControl.StyleIdentifierFooter).PseudoHovered()
                .ParentOf(E<Label>().Identifier(MainMenuControl.StyleIdentifierFooterTitle))
                .FontColor(titleGold),
            E<T>().Identifier(MainMenuControl.StyleIdentifierFooter).PseudoHovered()
                .ParentOf(E<Label>().Identifier(MainMenuControl.StyleIdentifierNavSubtitle))
                .FontColor(subtitleLight),
            E<T>().Identifier(MainMenuControl.StyleIdentifierPrimary)
                .ParentOf(E<Label>().Identifier(MainMenuControl.StyleIdentifierNavTitle))
                .FontColor(titleGold),
            E<T>().Identifier(MainMenuControl.StyleIdentifierPrimary)
                .ParentOf(E<Label>().Identifier(MainMenuControl.StyleIdentifierNavSubtitle))
                .FontColor(subtitleLight),
        ]);
    }

    private static void AddButtonLabelRules(List<StyleRule> rules, string identifier, Font font)
    {
        AddButtonLabelRules<Button>(rules, identifier, font);
        AddButtonLabelRules<ContainerButton>(rules, identifier, font);
        AddButtonLabelRules<OptionButton>(rules, identifier, font);
    }

    private static void AddButtonLabelRules<T>(List<StyleRule> rules, string identifier, Font font)
        where T : Control
    {
        rules.Add(E<T>().Identifier(identifier).ParentOf(E<Label>()).Font(font));
    }

    private static void AddButtonRules(
        List<StyleRule> rules,
        string identifier,
        StyleBox normal,
        StyleBox hovered,
        StyleBox pressed)
    {
        AddButtonRules<Button>(rules, identifier, normal, hovered, pressed);
        AddButtonRules<ContainerButton>(rules, identifier, normal, hovered, pressed);
        AddButtonRules<OptionButton>(rules, identifier, normal, hovered, pressed);
    }

    private static void AddButtonRules<T>(
        List<StyleRule> rules,
        string identifier,
        StyleBox normal,
        StyleBox hovered,
        StyleBox pressed)
        where T : Control
    {
        rules.AddRange([
            E<T>().Identifier(identifier).PseudoNormal().Box(normal),
            E<T>().Identifier(identifier).PseudoHovered().Box(hovered),
            E<T>().Identifier(identifier).PseudoPressed().Box(pressed),
            E<T>().Identifier(identifier).PseudoDisabled().Box(normal).Modulate(Color.FromHex("#777777")),
        ]);
    }

    private static EclipseStyleBoxRounded ButtonBox(
        string background,
        string border,
        float left,
        float top,
        float right,
        float bottom)
    {
        return new EclipseStyleBoxRounded
        {
            BackgroundColor = Color.FromHex(background),
            BorderColor = Color.FromHex(border),
            BorderThickness = new Thickness(left, top, right, bottom),
            Radius = 6f,
        };
    }
}
