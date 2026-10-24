using Content.Shared._Eclipse.Industrial;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Eclipse.Industrial;

/// <summary>
/// Liquid pipe flood-fill networking. Fluid transfer logic is TODO.
/// </summary>
public sealed class LiquidPipeNetworkSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;

    private static readonly Direction[] CardinalDirections =
        SharedIndustrialProcessorSystem.CardinalDirections;

    private readonly Dictionary<int, LiquidPipeNetwork> _networks = new();
    private int _nextNetworkId = 1;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LiquidPipeComponent, AnchorStateChangedEvent>(OnPipeAnchorChanged);
        SubscribeLocalEvent<LiquidPipeComponent, EntityTerminatingEvent>(OnPipeTerminating);
    }

    public bool TryGetNetwork(int networkId, out LiquidPipeNetwork network)
    {
        return _networks.TryGetValue(networkId, out network!);
    }

    public void RebuildNetworkFrom(Entity<LiquidPipeComponent> ent)
    {
        RemovePipeFromItsNetwork(ent);

        var xform = Transform(ent);
        if (!xform.Anchored)
        {
            ent.Comp.NetworkId = -1;
            Dirty(ent);
            return;
        }

        var connectedPipes = FloodFillPipes(ent);
        if (connectedPipes.Count == 0)
        {
            ent.Comp.NetworkId = -1;
            Dirty(ent);
            return;
        }

        var network = CreateNetwork(connectedPipes);
        AssignNetworkToPipes(network, connectedPipes);
    }

    public void RebuildNetworksNearProcessor(Entity<IndustrialProcessorComponent> ent)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not EntityUid gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var pos = _mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var rebuilt = new HashSet<EntityUid>();

        foreach (var direction in CardinalDirections)
        {
            ForAnchoredEntities(gridUid, grid, pos.Offset(direction), entity =>
            {
                if (!HasComp<LiquidPipeComponent>(entity) || rebuilt.Contains(entity))
                    return;

                RebuildNetworkFrom((entity, Comp<LiquidPipeComponent>(entity)));
                rebuilt.Add(entity);
            });
        }
    }

    private void OnPipeAnchorChanged(Entity<LiquidPipeComponent> ent, ref AnchorStateChangedEvent args)
    {
        RebuildNetworkFrom(ent);
        RebuildAdjacentPipeNetworks(ent);
    }

    private void OnPipeTerminating(Entity<LiquidPipeComponent> ent, ref EntityTerminatingEvent args)
    {
        var adjacentPipes = GetAdjacentPipes(ent);
        RemovePipeFromItsNetwork(ent);

        foreach (var adjacent in adjacentPipes)
        {
            if (Exists(adjacent))
                RebuildNetworkFrom((adjacent, Comp<LiquidPipeComponent>(adjacent)));
        }
    }

    private void RebuildAdjacentPipeNetworks(Entity<LiquidPipeComponent> ent)
    {
        foreach (var adjacent in GetAdjacentPipes(ent))
        {
            if (Exists(adjacent))
                RebuildNetworkFrom((adjacent, Comp<LiquidPipeComponent>(adjacent)));
        }
    }

    private HashSet<EntityUid> FloodFillPipes(EntityUid startPipe)
    {
        var result = new HashSet<EntityUid>();
        var queue = new Queue<EntityUid>();
        queue.Enqueue(startPipe);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!result.Add(current))
                continue;

            foreach (var adjacent in GetAdjacentPipes((current, Comp<LiquidPipeComponent>(current))))
            {
                if (!result.Contains(adjacent))
                    queue.Enqueue(adjacent);
            }
        }

        return result;
    }

    private LiquidPipeNetwork CreateNetwork(HashSet<EntityUid> pipes)
    {
        var network = new LiquidPipeNetwork
        {
            Id = _nextNetworkId++,
            EffectiveTier = PipeTier.Perfect,
        };

        foreach (var pipeUid in pipes)
        {
            network.Pipes.Add(pipeUid);
            var pipe = Comp<LiquidPipeComponent>(pipeUid);
            network.EffectiveTier = PipeTierHelper.GetWeakest(network.EffectiveTier, pipe.Tier);
        }

        var specs = PipeTierHelper.GetSpecs(network.EffectiveTier);
        network.ThroughputPerSecond = specs.ThroughputPerSecond;
        network.TransferDelay = specs.TransferDelay;

        _networks[network.Id] = network;
        return network;
    }

    private void AssignNetworkToPipes(LiquidPipeNetwork network, HashSet<EntityUid> pipes)
    {
        foreach (var pipeUid in pipes)
        {
            var pipe = Comp<LiquidPipeComponent>(pipeUid);
            pipe.NetworkId = network.Id;
            Dirty(pipeUid, pipe);
        }
    }

    private void RemovePipeFromItsNetwork(Entity<LiquidPipeComponent> ent)
    {
        if (ent.Comp.NetworkId < 0)
            return;

        if (_networks.TryGetValue(ent.Comp.NetworkId, out var network))
        {
            network.Pipes.Remove(ent);
            if (network.Pipes.Count == 0)
                _networks.Remove(ent.Comp.NetworkId);
        }

        ent.Comp.NetworkId = -1;
        Dirty(ent);
    }

    private List<EntityUid> GetAdjacentPipes(Entity<LiquidPipeComponent> ent)
    {
        var result = new List<EntityUid>();
        var xform = Transform(ent);

        if (xform.GridUid is not EntityUid gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return result;

        if (!xform.Anchored)
            return result;

        var pos = _mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);

        foreach (var direction in CardinalDirections)
        {
            ForAnchoredEntities(gridUid, grid, pos.Offset(direction), entity =>
            {
                if (HasComp<LiquidPipeComponent>(entity))
                    result.Add(entity);
            });
        }

        return result;
    }

    private void ForAnchoredEntities(
        EntityUid gridUid,
        MapGridComponent grid,
        Vector2i indices,
        Action<EntityUid> action)
    {
        var enumerator = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, indices);
        while (enumerator.MoveNext(out var entity) && entity != null)
            action(entity.Value);
    }
}

public sealed class LiquidPipeNetwork
{
    public int Id;
    public HashSet<EntityUid> Pipes = new();
    public PipeTier EffectiveTier = PipeTier.Perfect;
    public float ThroughputPerSecond;
    public float TransferDelay;
}
