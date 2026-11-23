using System.Collections.Generic;
using Robust.Client.ResourceManagement;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client._Eclipse.Industrial;

/// <summary>Assemble all pipe masks once, before sprite resources are loaded.</summary>
public sealed class IndustrialPipeAtlas : IDisposable
{
    private const string Directory = "/Textures/_Eclipse/Structures/Piping/";
    private readonly CompositeRsiSource _source;

    public IndustrialPipeAtlas(IResourceManager resources)
    {
        _source = new CompositeRsiSource(resources);
        Build("item_pipe_parts", "item_lp_pipe", "lp", false);
        Build("liquid_pipe_parts", "liquid_pipe", "pipe", true);
    }

    private void Build(string parts, string name, string prefix, bool includeContent)
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
        _source.Add(new ResPath(Directory + name + ".rsi"), new Vector2i(32, 32), states);
    }

    public void Dispose() => _source.Dispose();
}
