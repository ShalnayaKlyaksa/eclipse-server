using System.Buffers.Binary;
using System.IO;
using Content.Shared.Humanoid.Markings;
using Robust.Client.Utility;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SpaceWizards.Sodium;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client.Humanoid;

internal sealed partial class MarkingGradientBaker
{
    // Change the version when changing the rasterization or file format.
    internal static readonly ResPath CacheDirectory = new("/Cache/MarkingGradients/v2");

    private static ResPath CachePath(Source source, Color root, MarkingGradient gradient)
    {
        using var stream = new MemoryStream();
        var scalar = new byte[4];
        stream.Write(source.Digest);
        WriteColor(root);
        WriteColor(gradient.TipColor);
        stream.WriteByte((byte) gradient.Shape);
        WriteInt(gradient.Steps);
        WriteInt(gradient.EffectiveAngle);
        WriteInt(gradient.EffectiveStart);
        WriteInt(gradient.EffectiveLength);
        WriteInt(gradient.CenterX);
        WriteInt(gradient.CenterY);
        foreach (var stop in gradient.Stops.AsSpan())
        {
            WriteInt(stop.Position);
            WriteColor(stop.Color);
        }

        return CacheDirectory / $"{Convert.ToHexString(HashBuffer(stream))}.rgba";

        void WriteInt(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(scalar, value);
            stream.Write(scalar);
        }

        void WriteColor(Color color)
        {
            WriteInt(BitConverter.SingleToInt32Bits(color.R));
            WriteInt(BitConverter.SingleToInt32Bits(color.G));
            WriteInt(BitConverter.SingleToInt32Bits(color.B));
            WriteInt(BitConverter.SingleToInt32Bits(color.A));
        }
    }

    private static byte[] HashBuffer(MemoryStream stream)
    {
        return CryptoGenericHashBlake2B.Hash(32, stream.GetBuffer().AsSpan(0, (int) stream.Length), ReadOnlySpan<byte>.Empty);
    }

    private static void WritePixels(ReadOnlySpan<Rgba32> pixels, Span<byte> data)
    {
        for (var i = 0; i < pixels.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(data[(4 * i)..], pixels[i].PackedValue);
    }

    private Image<Rgba32>? TryReadBake(ResPath path, Vector2i size)
    {
        byte[] data;
        try
        {
            if (!resources.UserData.Exists(path))
                return null;

            using var stream = resources.UserData.OpenRead(path);
            if (stream.Length != (long) size.X * size.Y * 4)
                return null;

            data = new byte[size.X * size.Y * 4];
            stream.ReadExactly(data);
        }
        catch (Exception e)
        {
            Logger.DebugS("marking-gradient", $"Could not read {path}: {e.Message}");
            return null;
        }

        var image = new Image<Rgba32>(size.X, size.Y);
        var pixels = image.GetPixelSpan();
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = new Rgba32(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4 * i)));
        return image;
    }

    private void Persist(CachedBake bake)
    {
        if (bake.Pixels is not { } pixels)
            return;

        var data = new byte[pixels.Length * 4];
        WritePixels(pixels, data);
        var directory = resources.UserData;
        var temporary = CacheDirectory / $"{Guid.NewGuid():N}.tmp";
        try
        {
            directory.CreateDir(CacheDirectory);
            using (var stream = directory.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(data);

            directory.Delete(bake.Path);
            directory.Rename(temporary, bake.Path);
            bake.Pixels = null;
        }
        catch (Exception e)
        {
            // A cache failure must not interrupt saving the character.
            Logger.DebugS("marking-gradient", $"Could not write {bake.Path}: {e.Message}");
            try
            {
                directory.Delete(temporary);
            }
            catch (Exception cleanup)
            {
                Logger.DebugS("marking-gradient", $"Could not remove {temporary}: {cleanup.Message}");
            }
        }
    }
}
