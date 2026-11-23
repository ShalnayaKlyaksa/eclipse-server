using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
namespace Content.Client._Erida.Research.UI;

/// <summary>Readable schematic markers replace item thumbnails at overview scale.</summary>
public sealed class ResearchNodePanel : PanelContainer
{
    public bool Compact;
    public bool Selected;
    public Color MarkerColor;
    protected override void Draw(DrawingHandleScreen handle)
    {
        if (!Compact) { base.Draw(handle); return; }
        var center = PixelSize / 2;
        var radius = MathF.Min(5 * UIScale, MathF.Min(PixelWidth, PixelHeight) / 3);
        handle.DrawRect(new UIBox2(center - new Vector2(radius), center + new Vector2(radius)), MarkerColor);
        if (Selected)
        {
            var ring = radius + 3 * UIScale;
            handle.DrawRect(new UIBox2(center - new Vector2(ring), center + new Vector2(ring)), Color.FromHex("#CAE9BE"), false);
        }
    }
}
