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
                .FontColor(Color.FromHex("#F4A817")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierLogoTitle)
                .Font(sheet.BaseFont.GetFont(25, FontKind.Bold))
                .FontColor(Color.FromHex("#F4A817")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierLogoSub)
                .Font(sheet.BaseFont.GetFont(14, FontKind.Bold))
                .FontColor(Color.FromHex("#C6A25A")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierNavTitle)
                .Font(sheet.BaseFont.GetFont(14, FontKind.Bold))
                .FontColor(Color.FromHex("#F2F2F2")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierNavSubtitle)
                .Font(sheet.BaseFont.GetFont(11))
                .FontColor(Color.FromHex("#AFAFAF")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierFooterTitle)
                .Font(sheet.BaseFont.GetFont(13, FontKind.Bold))
                .FontColor(Color.FromHex("#E8E8E8")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierHeaderGold)
                .Font(sheet.BaseFont.GetFont(15, FontKind.Bold))
                .FontColor(Color.FromHex("#F0A513")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierGoldSmall)
                .Font(sheet.BaseFont.GetFont(12, FontKind.Bold))
                .FontColor(Color.FromHex("#E6A11A")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierText)
                .Font(sheet.BaseFont.GetFont(13, FontKind.Bold))
                .FontColor(Color.FromHex("#EDEDED")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierSubtle)
                .Font(sheet.BaseFont.GetFont(11))
                .FontColor(Color.FromHex("#9A9A9A")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierTinySubtle)
                .Font(sheet.BaseFont.GetFont(10))
                .FontColor(Color.FromHex("#B8B8B8")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierSecondaryTitle)
                .Font(sheet.BaseFont.GetFont(12, FontKind.Bold))
                .FontColor(Color.FromHex("#BDBDBD")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierScore)
                .Font(sheet.BaseFont.GetFont(15, FontKind.Bold))
                .FontColor(Color.FromHex("#FFFFFF")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierRankIcon)
                .Font(sheet.BaseFont.GetFont(17, FontKind.Bold))
                .FontColor(Color.FromHex("#D5C9AE")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierServerIcon)
                .Font(sheet.BaseFont.GetFont(18, FontKind.Bold))
                .FontColor(Color.FromHex("#F0A513")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierStatusIcon)
                .Font(sheet.BaseFont.GetFont(16, FontKind.Bold))
                .FontColor(Color.FromHex("#00C9A6")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierSecondaryIcon)
                .Font(sheet.BaseFont.GetFont(16, FontKind.Bold))
                .FontColor(Color.FromHex("#AFAFAF")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierTab)
                .Font(sheet.BaseFont.GetFont(12, FontKind.Bold))
                .FontColor(Color.FromHex("#BDBDBD")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierTabActive)
                .Font(sheet.BaseFont.GetFont(12, FontKind.Bold))
                .FontColor(Color.FromHex("#F0A513")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierWarning)
                .Font(sheet.BaseFont.GetFont(12, FontKind.Bold))
                .FontColor(Color.FromHex("#FF1818")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierCompactHeaderGold)
                .Font(sheet.BaseFont.GetFont(14, FontKind.Bold))
                .FontColor(Color.FromHex("#F0A513")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierCompactText)
                .Font(sheet.BaseFont.GetFont(12, FontKind.Bold))
                .FontColor(Color.FromHex("#EDEDED")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierCompactSubtle)
                .Font(sheet.BaseFont.GetFont(11))
                .FontColor(Color.FromHex("#AFAFAF")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierRoadmapTitle)
                .Font(sheet.BaseFont.GetFont(31, FontKind.Bold))
                .FontColor(Color.FromHex("#F0A513")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierRoadmapSubtitle)
                .Font(sheet.BaseFont.GetFont(14, FontKind.Bold))
                .FontColor(Color.FromHex("#BEBEBE")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierRoadmapNumber)
                .Font(sheet.BaseFont.GetFont(25, FontKind.Bold))
                .FontColor(Color.FromHex("#E6A11A")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierRoadmapItemTitle)
                .Font(sheet.BaseFont.GetFont(15, FontKind.Bold))
                .FontColor(Color.FromHex("#F0A513")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierRoadmapDot)
                .Font(sheet.BaseFont.GetFont(18, FontKind.Bold))
                .FontColor(Color.FromHex("#E6A11A")),
            E<Label>().Identifier(MainMenuControl.StyleIdentifierRoadmapDotActive)
                .Font(sheet.BaseFont.GetFont(19, FontKind.Bold))
                .FontColor(Color.FromHex("#F0A513")),
        };

        AddButtonRules(rules, MainMenuControl.StyleIdentifierPrimary,
            ButtonBox("#070301F4", "#A85E126C", 0, 0, 0, 0),
            ButtonBox("#090501F8", "#F0A51399", 1, 0, 0, 0),
            ButtonBox("#140900F8", "#F0A513C4", 1, 0, 0, 0));

        AddButtonRules(rules, MainMenuControl.StyleIdentifierNav,
            ButtonBox("#070300F0", "#A85E1200", 0, 0, 0, 0),
            ButtonBox("#100700F6", "#A85E126A", 1, 0, 0, 0),
            ButtonBox("#090501F8", "#F0A51388", 1, 0, 0, 0));

        AddButtonRules(rules, MainMenuControl.StyleIdentifierFooter,
            ButtonBox("#070300F0", "#A85E1200", 0, 0, 0, 0),
            ButtonBox("#100700F6", "#A85E126A", 1, 0, 0, 0),
            ButtonBox("#090501F8", "#F0A51388", 1, 0, 0, 0));

        AddButtonRules(rules, MainMenuControl.StyleIdentifierSecondary,
            ButtonBox("#070300F2", "#A85E1228", 1, 0, 0, 0),
            ButtonBox("#100700F6", "#A85E1288", 1, 0, 0, 0),
            ButtonBox("#100700F8", "#F0A51399", 1, 0, 0, 0));

        AddButtonRules(rules, MainMenuControl.StyleIdentifierTabButton,
            ButtonBox("#070300E8", "#A85E1200", 0, 0, 0, 0),
            ButtonBox("#100700F2", "#A85E1255", 1, 0, 0, 0),
            ButtonBox("#0C0600F2", "#F0A51377", 1, 0, 0, 0));

        AddButtonRules(rules, MainMenuControl.StyleIdentifierTabButtonActive,
            ButtonBox("#0B0500F6", "#A85E1288", 1, 0, 0, 0),
            ButtonBox("#110800F8", "#F0A513AA", 1, 0, 0, 0),
            ButtonBox("#160A00F8", "#F0A513CC", 1, 0, 0, 0));

        AddButtonRules(rules, MainMenuControl.StyleIdentifierCompactButton,
            ButtonBox("#070300F2", "#A85E1228", 1, 0, 0, 0),
            ButtonBox("#100700F6", "#A85E1288", 1, 0, 0, 0),
            ButtonBox("#100700F8", "#F0A51399", 1, 0, 0, 0));

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
        var titleGold = Color.FromHex("#F0A513");
        var subtitleLight = Color.FromHex("#D8D8D8");

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
