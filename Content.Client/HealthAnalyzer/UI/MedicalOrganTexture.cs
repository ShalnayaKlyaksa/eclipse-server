using System.Numerics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.HealthAnalyzer.UI;

/// <summary>Transparent margins must not intercept clicks meant for an organ underneath.</summary>
public sealed class MedicalOrganTexture : TextureRect
{
    private readonly string _painted;
    private readonly int _width;
    private readonly int _height;

    public MedicalOrganTexture(IResourceCache cache, ResPath path)
    {
        Texture = cache.GetResource<TextureResource>(path).Texture;
        _width = Texture.Width;
        _height = Texture.Height;
        using var reader = cache.ContentFileReadText(path.WithExtension("hitmask"));
        _painted = reader.ReadToEnd();
        if (_painted.Length != _width * _height)
            throw new InvalidOperationException($"Invalid organ hit mask: {path}");
    }

    protected override bool HasPoint(Vector2 point)
    {
        if (Size.X <= 0 || Size.Y <= 0) return false;
        var x = (int) MathF.Floor(point.X / Size.X * _width);
        var y = (int) MathF.Floor(point.Y / Size.Y * _height);
        return x >= 0 && x < _width && y >= 0 && y < _height && _painted[y * _width + x] == '1';
    }
}
