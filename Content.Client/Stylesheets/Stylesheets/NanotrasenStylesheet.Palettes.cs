using Content.Client.Stylesheets.Palette;

namespace Content.Client.Stylesheets.Stylesheets;

public partial class NanotrasenStylesheet
{
    public override ColorPalette PrimaryPalette => Palettes.EclipsePrimary;
    public override ColorPalette SecondaryPalette => Palettes.EclipseSecondary;
    public override ColorPalette PositivePalette => Palettes.Green;
    public override ColorPalette NegativePalette => Palettes.Red;
    public override ColorPalette HighlightPalette => Palettes.EclipseGold;
}
