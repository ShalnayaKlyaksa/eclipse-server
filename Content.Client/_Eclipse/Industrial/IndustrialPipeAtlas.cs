using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Content.Client._Eclipse.Industrial;

/// <summary>Assemble all pipe masks once, before sprite resources are loaded.</summary>
public sealed class IndustrialPipeAtlas : IDisposable
{
    private const string Directory = "/Textures/_Eclipse/Structures/Piping/";
    private const int FrameSize = 32;
    // PNG metadata key read by RobustToolbox's RSIC loader.
    private const string RsicMetadataKey = "robusttoolbox_rsic_meta";
    private readonly MemoryContentRoot _root = new();

    public IndustrialPipeAtlas(IResourceManager resources)
    {
        Build(resources, "item_pipe_parts", "item_lp_pipe", "lp", false);
        Build(resources, "liquid_pipe_parts", "liquid_pipe", "pipe", true);
        resources.AddRoot(ResPath.Root, _root);
    }

    private void Build(IResourceManager resources, string parts, string name, string prefix, bool includeContent)
    {
        var states = new List<(string Name, ResPath[] Layers)>();
        ResPath Part(string part) => new(Directory + parts + "/" + part + ".png");
        for (var mask = 0; mask < 16; mask++)
        {
            var layers = new List<ResPath>();
            if ((mask & 1) != 0) layers.Add(Part("north"));
            if ((mask & 2) != 0) layers.Add(Part("south"));
            if ((mask & 4) != 0) layers.Add(Part("west"));
            if ((mask & 8) != 0) layers.Add(Part("east"));
            layers.Add(Part("hub"));
            states.Add((prefix + mask, layers.ToArray()));
        }
        if (includeContent)
            states.Add(("content", new[] { Part("content") }));
        states.Add(("connector", new[] { new ResPath(Directory + "connector_parts/socket.png") }));
        states.Add(("flow_arrow", new[] { new ResPath(Directory + "connector_parts/arrow.png") }));

        var columns = (int) Math.Ceiling(Math.Sqrt(states.Count));
        var rows = (states.Count + columns - 1) / columns;
        using var atlas = new Image<Rgba32>(columns * FrameSize, rows * FrameSize);
        var images = new Dictionary<ResPath, Image<Rgba32>>();
        try
        {
            for (var index = 0; index < states.Count; index++)
            {
                var origin = new Point(index % columns * FrameSize, index / columns * FrameSize);
                foreach (var path in states[index].Layers)
                {
                    if (!images.TryGetValue(path, out var image))
                    {
                        using var stream = resources.ContentFileRead(path);
                        image = Image.Load<Rgba32>(stream);
                        images.Add(path, image);
                        if (image.Width != FrameSize || image.Height != FrameSize)
                            throw new InvalidDataException($"Pipe atlas layer {path} must be {FrameSize}×{FrameSize}.");
                    }

                    atlas.Mutate(context => context.DrawImage(image, origin, 1f));
                }
            }

            var metadata = JsonSerializer.Serialize(new
            {
                version = 1,
                size = new { x = FrameSize, y = FrameSize },
                states = states.Select(state => new { name = state.Name }).ToArray(),
            });
            atlas.Metadata.GetPngMetadata().TextData.Add(new PngTextData(RsicMetadataKey, metadata, "", ""));
            using var encoded = new MemoryStream();
            atlas.SaveAsPng(encoded);
            _root.AddOrUpdateFile(new ResPath(Directory + name + ".rsic"), encoded.ToArray());
        }
        finally
        {
            foreach (var image in images.Values)
                image.Dispose();
        }
    }

    public void Dispose() => _root.Dispose();
}
