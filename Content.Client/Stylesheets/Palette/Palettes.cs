namespace Content.Client.Stylesheets.Palette;

/// <summary>
///     Stores all style palettes in one accessible location
/// </summary>
/// <remarks>
///     Technically not limited to only colors, can store like, standard padding amounts, and font sizes, maybe?
/// </remarks>
public static class Palettes
{
    // muted tones
    public static readonly ColorPalette Navy = ColorPalette.FromHexBase("#4f5376", lightnessShift: 0.05f, chromaShift: 0.0045f);
    public static readonly ColorPalette Cyan = ColorPalette.FromHexBase("#42586a", lightnessShift: 0.05f, chromaShift: 0.0045f);
    public static readonly ColorPalette Slate = ColorPalette.FromHexBase("#545562");
    public static readonly ColorPalette Neutral = ColorPalette.FromHexBase("#555555");
    public static readonly ColorPalette EclipsePrimary = ColorPalette.FromHexBase(
        "#E6A11A",
        lightnessShift: 0.045f,
        chromaShift: 0.01f,
        element: Color.FromHex("#2A1200"),
        background: Color.FromHex("#070300"),
        text: Color.FromHex("#E6A11A"));
    public static readonly ColorPalette EclipseSecondary = ColorPalette.FromHexBase(
        "#A6A6A6",
        lightnessShift: 0.045f,
        chromaShift: 0.002f,
        element: Color.FromHex("#0A0400"),
        background: Color.FromHex("#070300"),
        text: Color.FromHex("#A6A6A6"));

    // status tones
    public static readonly ColorPalette Red = ColorPalette.FromHexBase("#b62124", chromaShift: 0.02f);
    public static readonly ColorPalette Amber = ColorPalette.FromHexBase("#c18e36");
    public static readonly ColorPalette Green = ColorPalette.FromHexBase("#3c854a");
    public static readonly StatusPalette Status = new([Red.Base, Amber.Base, Green.Base]);

    // highlight tones
    public static readonly ColorPalette Gold = ColorPalette.FromHexBase("#a88b5e");
    public static readonly ColorPalette EclipseGold = ColorPalette.FromHexBase(
        "#FFB52A",
        lightnessShift: 0.045f,
        chromaShift: 0.012f,
        element: Color.FromHex("#A85E12"),
        background: Color.FromHex("#1A0900"),
        text: Color.FromHex("#FFB52A"));
    public static readonly ColorPalette Maroon = ColorPalette.FromHexBase("#9b2236");

    // Intended to be used with `ModulateSelf` to darken / lighten something
    public static readonly ColorPalette AlphaModulate = ColorPalette.FromHexBase("#ffffff");

}
