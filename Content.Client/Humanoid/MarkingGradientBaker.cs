using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Shared.Humanoid.Markings;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client.Humanoid;

internal sealed partial class MarkingGradientBaker(IResourceManager resources)
{
    private const byte VisibleAlpha = 26;
    private const int CacheSize = 128;
    private readonly Dictionary<RSI.State, Source> _sources = new();
    private readonly Dictionary<BakeKey, CachedBake> _bakes = new();
    private readonly Queue<BakeKey> _bakeOrder = new();

    public Bake? GetBake(RSI.State state, Color root, MarkingGradient gradient, bool persist = false)
    {
        gradient = gradient.Normalized();
        // Match the color precision stored in the character profile.
        root = new Color(root.RByte, root.GByte, root.BByte, root.AByte);
        var key = new BakeKey(state, root, gradient);
        if (_bakes.TryGetValue(key, out var cached))
        {
            if (persist)
                Persist(cached);
            return cached.Bake;
        }

        if (!_sources.TryGetValue(state, out var source))
        {
            source = LoadSource(state);
            if (source == null)
                return null;

            if (_sources.Count >= CacheSize)
                _sources.Clear();
            _sources.Add(state, source);
        }

        var path = CachePath(source, root, gradient);
        using var fromDisk = TryReadBake(path, source.Size);
        using var generated = fromDisk == null ? new Image<Rgba32>(source.Size.X, source.Size.Y) : null;
        if (generated != null)
        {
            foreach (var frame in source.Frames)
                BakeFrame(frame, state.Size, root, gradient, generated);
        }

        var bake = new Bake(Texture.LoadFromImage(fromDisk ?? generated!, $"{state.StateId}-gradient"), source.AtlasTransform);
        cached = new CachedBake(bake, path, generated == null ? null : generated.GetPixelSpan().ToArray());
        if (persist)
            Persist(cached);
        if (_bakes.Count >= CacheSize)
            _bakes.Remove(_bakeOrder.Dequeue());
        _bakes.Add(key, cached);
        _bakeOrder.Enqueue(key);
        return bake;
    }

    private Source? LoadSource(RSI.State state)
    {
        var regions = state.Icons.SelectMany(frames => frames)
            .OfType<AtlasTexture>()
            .DistinctBy(texture => texture.SubRegion)
            .OrderBy(texture => texture.SubRegion.Top)
            .ThenBy(texture => texture.SubRegion.Left)
            .ToArray();
        if (regions.Length == 0)
            return null;

        var path = state.RSI.Path;
        var separateFrames = resources.TryContentFileRead(path / $"{state.StateId}.png", out var stream);
        if (stream == null && !resources.TryContentFileRead(path.WithExtension("rsic"), out stream))
            return null;

        using (stream)
        using (var image = Image.Load<Rgba32>(stream))
        {
            // Keep the original atlas layout within this state, plus a transparent border for displaced draws.
            var left = (int) regions.Min(texture => texture.SubRegion.Left) - 1;
            var top = (int) regions.Min(texture => texture.SubRegion.Top) - 1;
            var width = (int) regions.Max(texture => texture.SubRegion.Right) + 1 - left;
            var bottom = (int) regions.Max(texture => texture.SubRegion.Bottom) + 1;
            var height = bottom - top;
            var atlas = regions[0].SourceTexture;
            var transform = new Vector4((float) atlas.Width / width, (float) atlas.Height / height,
                -(float) left / width, (float) (bottom - atlas.Height) / height);

            // In a packed resource the first state starts at the beginning of its original atlas.
            var atlasOrigin = state.RSI.First().Frame0 is AtlasTexture first ? (Vector2i) first.SubRegion.TopLeft : default;
            var imageWidth = image.Width;
            var imagePixels = image.GetPixelSpan();
            var columns = imageWidth / state.Size.X;
            var frames = new Frame[regions.Length];
            for (var i = 0; i < regions.Length; i++)
            {
                var position = (Vector2i) regions[i].SubRegion.TopLeft;
                var input = separateFrames
                    ? new Vector2i(i % columns * state.Size.X, i / columns * state.Size.Y)
                    : position - atlasOrigin;
                var pixels = new Rgba32[state.Size.X * state.Size.Y];
                for (var y = 0; y < state.Size.Y; y++)
                for (var x = 0; x < state.Size.X; x++)
                    pixels[y * state.Size.X + x] = imagePixels[(input.Y + y) * imageWidth + input.X + x];
                frames[i] = new Frame(position - new Vector2i(left, top), pixels);
            }

            using var sourceData = new MemoryStream();
            Span<byte> offset = new byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(offset, width);
            BinaryPrimitives.WriteInt32LittleEndian(offset[4..], height);
            BinaryPrimitives.WriteInt32LittleEndian(offset[8..], state.Size.X);
            BinaryPrimitives.WriteInt32LittleEndian(offset[12..], state.Size.Y);
            sourceData.Write(offset);
            var pixelData = new byte[frames[0].Pixels.Length * 4];
            foreach (var frame in frames)
            {
                BinaryPrimitives.WriteInt32LittleEndian(offset, frame.Position.X);
                BinaryPrimitives.WriteInt32LittleEndian(offset[4..], frame.Position.Y);
                sourceData.Write(offset[..8]);
                WritePixels(frame.Pixels, pixelData);
                sourceData.Write(pixelData);
            }

            return new Source(new Vector2i(width, height), frames, transform, HashBuffer(sourceData));
        }
    }

    internal static void BakeFrame(Frame frame, Vector2i size, Color root, MarkingGradient gradient, Image<Rgba32> output)
    {
        var direction = gradient.EffectiveAngle switch
        {
            0 => Vector2.UnitX,
            90 => Vector2.UnitY,
            180 => -Vector2.UnitX,
            270 => -Vector2.UnitY,
            var angle => new Vector2(MathF.Cos(angle * MathF.PI / 180), MathF.Sin(angle * MathF.PI / 180)),
        };
        var rayStep = direction / Math.Max(Math.Abs(direction.X), Math.Abs(direction.Y));
        var first = float.PositiveInfinity;
        var last = float.NegativeInfinity;
        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);

        for (var y = 0; y < size.Y; y++)
        for (var x = 0; x < size.X; x++)
        {
            if (frame.Pixels[y * size.X + x].A == 0)
                continue;

            var position = new Vector2(x, y);
            var projection = Vector2.Dot(position, direction);
            first = Math.Min(projection, first);
            last = Math.Max(projection, last);
            min = Vector2.Min(position, min);
            max = Vector2.Max(position, max);
        }

        if (first > last)
            return;

        var span = Math.Max(last - first, 1);
        var start = gradient.EffectiveStart / 100f;
        var length = gradient.EffectiveLength / 100f;
        var center = min + (max - min) * new Vector2(gradient.CenterX / 100f, gradient.CenterY / 100f);
        var radius = Vector2.Max((max - min) / 2f, Vector2.One);
        var ramp = new GradientRamp(root, gradient);
        var palette = ramp.Colors.Select(Color.FromSrgb).ToArray();

        for (var y = 0; y < size.Y; y++)
        for (var x = 0; x < size.X; x++)
        {
            var pixel = frame.Pixels[y * size.X + x];
            if (pixel.A == 0)
                continue;

            var position = new Vector2(x, y);
            var projection = Vector2.Dot(position, direction);
            var progress = (projection - first) / span;
            if (gradient.Shape == MarkingGradientShape.Contour && pixel.A >= VisibleAlpha)
            {
                var end = projection;
                for (var step = 1; ; step++)
                {
                    var sample = position + rayStep * step;
                    var sx = (int) MathF.Round(sample.X);
                    var sy = (int) MathF.Round(sample.Y);
                    if (sx < 0 || sx >= size.X || sy < 0 || sy >= size.Y || frame.Pixels[sy * size.X + sx].A < VisibleAlpha)
                        break;
                    end = Vector2.Dot(new Vector2(sx, sy), direction);
                }
                var localSpan = Math.Max(end - first, span * 0.5f);
                progress = (projection - first) / MathHelper.Lerp(span, localSpan, 0.85f);
            }
            else if (gradient.Shape == MarkingGradientShape.Radial)
                progress = ((position - center) / radius).Length();

            // Stagger the edges of the color bands on a 2x2 pixel grid.
            var pattern = ((x & 1) ^ (y & 1)) * 2 + (y & 1);
            var band = ramp.GetBand((progress - start) / length, (pattern - 1.5f) * 0.175f);
            var shaded = Color.ToSrgb(Color.FromSrgb(new Color(pixel.R, pixel.G, pixel.B, pixel.A)) * palette[band]);
            output[frame.Position.X + x, frame.Position.Y + y] = new Rgba32(shaded.R, shaded.G, shaded.B, shaded.A);
        }
    }

    internal sealed class GradientRamp
    {
        private readonly MarkingGradientStop[] _stops;
        private readonly int _steps;
        public readonly Color[] Colors;

        public GradientRamp(Color root, MarkingGradient gradient)
        {
            _steps = gradient.Steps;
            _stops = [new() { Position = 0, Color = root }, .. gradient.Stops ?? [], new() { Position = 100, Color = gradient.TipColor }];
            Colors = new Color[(_stops.Length - 1) * _steps];
            for (var segment = 0; segment < _stops.Length - 1; segment++)
            for (var band = 0; band < _steps; band++)
                Colors[segment * _steps + band] = Color.InterpolateBetween(_stops[segment].Color, _stops[segment + 1].Color,
                    (float) band / (_steps - 1)).WithAlpha(root.A);
        }

        public int GetBand(float progress, float dither = 0f)
        {
            var position = Math.Clamp(progress, 0f, 1f) * 100f;
            var segment = 0;
            while (position > _stops[segment + 1].Position)
                segment++;
            var relative = (position - _stops[segment].Position) / (_stops[segment + 1].Position - _stops[segment].Position);
            return segment * _steps + Math.Clamp((int) (relative * _steps + dither), 0, _steps - 1);
        }
    }

    internal readonly record struct Bake(Texture Texture, Vector4 AtlasTransform);
    internal readonly record struct Frame(Vector2i Position, Rgba32[] Pixels);
    private sealed record Source(Vector2i Size, Frame[] Frames, Vector4 AtlasTransform, byte[] Digest);
    private readonly record struct BakeKey(RSI.State State, Color Root, MarkingGradient Gradient);
    private sealed record CachedBake(Bake Bake, ResPath Path, Rgba32[]? Pixels)
    {
        public Rgba32[]? Pixels { get; set; } = Pixels;
    }
}
