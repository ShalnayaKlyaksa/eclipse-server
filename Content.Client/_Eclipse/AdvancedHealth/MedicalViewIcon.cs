using System.Numerics;
using Content.Client.HealthAnalyzer.UI;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>Pixel-aligned silhouettes stay legible at the chip toolbar's actual size.</summary>
public sealed class MedicalViewIcon(MedicalBodyViewMode mode) : Control
{
    private static readonly string[] Body =
    [
        "................",
        "......####......",
        "......#++#......",
        "......####......",
        ".......##.......",
        "..############..",
        "..####++++####..",
        "......#++#......",
        "......#++#......",
        "......####......",
        ".....######.....",
        ".....##..##.....",
        ".....##..##.....",
        ".....##..##.....",
        "....###..###....",
        "................",
    ];

    private readonly Texture? _organ = mode == MedicalBodyViewMode.Organs
        ? IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>(
            "/Textures/_Eclipse/Interface/AdvancedHealth/Organs48/heart.png").Texture
        : null;

    private static readonly string[] Protection =
    [
        "................",
        "...##########...",
        "..##++++++++##..",
        "..#++++++++++#..",
        "..#++++++++++#..",
        "..#++++##++++#..",
        "..#++++##++++#..",
        "..#++######++#..",
        "..#++######++#..",
        "..#++++##++++#..",
        "...#+++##+++#...",
        "...##++++++##...",
        "....##++++##....",
        ".....##++##.....",
        "......####......",
        "................",
    ];

    protected override void Draw(DrawingHandleScreen handle)
    {
        var selected = Parent is Button { Pressed: true };
        if (_organ != null)
        {
            var side = MathF.Floor(Math.Min(PixelSize.X, PixelSize.Y));
            var corner = (PixelSize - new Vector2(side)) / 2;
            handle.DrawTextureRect(_organ, UIBox2.FromDimensions(corner, new Vector2(side)),
                Color.White.WithAlpha(selected ? 1 : 0.85f));
            return;
        }
        var pixels = mode == MedicalBodyViewMode.General ? Body : Protection;
        var size = MathF.Floor(Math.Min(PixelSize.X, PixelSize.Y) * 0.9f);
        var cell = size / 16;
        var origin = (PixelSize - new Vector2(size)) / 2;
        var ink = selected ? HealthMenuStyle.AmberBright : HealthMenuStyle.Amber;
        var fill = ink.WithAlpha(selected ? 0.5f : 0.25f);
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
        {
            if (pixels[y][x] == '.') continue;
            var left = MathF.Round(origin.X + x * cell);
            var top = MathF.Round(origin.Y + y * cell);
            var right = MathF.Round(origin.X + (x + 1) * cell);
            var bottom = MathF.Round(origin.Y + (y + 1) * cell);
            handle.DrawRect(new UIBox2(left, top, right, bottom), pixels[y][x] == '#' ? ink : fill);
        }
    }
}
