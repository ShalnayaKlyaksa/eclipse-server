using System.Numerics;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client.Stylesheets;

/// <summary>Shared Forge surfaces, based on the Ashen HUD.</summary>
public static class ForgeStyle
{
    public static readonly Color Background = Color.FromHex("#101010");
    public static readonly Color Surface = Color.FromHex("#1A1A1A");
    public static readonly Color Input = Color.FromHex("#0D0D0D");
    public static readonly Color Hover = Color.FromHex("#252525");
    public static readonly Color Border = Color.FromHex("#333333");
    public static readonly Color Accent = Color.FromHex("#FF861F");
    public static readonly Color Selection = Color.FromHex("#292016");
    public static readonly Color Text = Color.FromHex("#E0E0E0");
    public static readonly Color Muted = Color.FromHex("#A6A6A6");

    public static StyleBoxFlat Panel(Color background, Color border, float horizontalPadding = 0, float verticalPadding = 0)
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = new Thickness(border.A > 0 ? 1 : 0),
        };
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontalPadding);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, verticalPadding);
        return box;
    }

    public static StyleBoxFlat Button(string state = "", float horizontalPadding = 14, float verticalPadding = 2)
    {
        // Slot artwork distorts when stretched into wide buttons. Draw window controls at their actual size.
        var background = state switch { "_hover" => Hover, "_pressed" => Selection, _ => Surface };
        var border = state switch { "_hover" => Color.FromHex("#484848"), "_pressed" => Accent.WithAlpha(0.65f), _ => Border };
        return Panel(background, border, horizontalPadding, verticalPadding);
    }

    public static StyleBoxTexture HudButton(IResourceCache resources, string state = "", float horizontalPadding = 0, float verticalPadding = 0)
    {
        var box = new StyleBoxTexture
        {
            Texture = resources.GetTexture($"/Textures/Interface/Forge/SlotBackground{state}.png"),
            TextureScale = new Vector2(2),
        };
        box.SetPatchMargin(StyleBox.Margin.All, 2);
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontalPadding);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, verticalPadding);
        return box;
    }
}
