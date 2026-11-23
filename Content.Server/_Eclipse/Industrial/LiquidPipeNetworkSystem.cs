using System.Linq;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Atmos;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Eclipse.Industrial;

/// <summary>
/// Liquid pipe topology and conservative transfer between adjacent section buffers.
/// </summary>
public sealed partial class LiquidPipeNetworkSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _mapSystem = default!;

    private static readonly Direction[] CardinalDirections =
        SharedIndustrialProcessorSystem.CardinalDirections;

    private readonly Dictionary<int, LiquidPipeNetwork> _networks = new();
    private int _nextNetworkId = 1;
    private readonly HashSet<EntityUid> _pendingRoots = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LiquidPipeComponent, AnchorStateChangedEvent>(OnPipeAnchorChanged);
        SubscribeLocalEvent<LiquidPipeComponent, EntityTerminatingEvent>(OnPipeTerminating);
        InitializeFluids();
        InitializeProcessors();
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

        // Retire the old membership of every pipe so superseded networks cannot retain the same component.
        foreach (var pipe in connectedPipes)
            RemovePipeFromItsNetwork((pipe, Comp<LiquidPipeComponent>(pipe)));

        var network = CreateNetwork(connectedPipes);
        AssignNetworkToPipes(network, connectedPipes);
        _pendingRoots.ExceptWith(connectedPipes);
    }

    private void OnPipeAnchorChanged(Entity<LiquidPipeComponent> ent, ref AnchorStateChangedEvent args)
    {
        QueueTopologyChange(ent);
    }

    private void OnPipeTerminating(Entity<LiquidPipeComponent> ent, ref EntityTerminatingEvent args)
    {
        _pendingRoots.UnionWith(GetAdjacentPipes(ent));
        RemovePipeFromItsNetwork(ent);
    }

    public void QueueTopologyChange(Entity<LiquidPipeComponent> ent)
    {
        _pendingRoots.UnionWith(GetAdjacentPipes(ent));
        if (Transform(ent).Anchored && !TerminatingOrDeleted(ent))
            _pendingRoots.Add(ent);
        else
            RemovePipeFromItsNetwork(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var roots = _pendingRoots.ToArray();
        _pendingRoots.Clear();
        var visited = new HashSet<EntityUid>();
        foreach (var root in roots)
        {
            if (TerminatingOrDeleted(root) || visited.Contains(root) || !Transform(root).Anchored ||
                !TryComp<LiquidPipeComponent>(root, out var pipe))
                continue;
            RebuildNetworkFrom((root, pipe));
            if (TryGetNetwork(pipe.NetworkId, out var network))
                visited.UnionWith(network.Pipes);
        }
        foreach (var id in _dirtyEndpoints)
            if (_networks.TryGetValue(id, out var network))
                RebuildEndpoints(network);
        _dirtyEndpoints.Clear();
        UpdateFluids(frameTime);
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

        _networks[network.Id] = network;
        return network;
    }

    private void AssignNetworkToPipes(LiquidPipeNetwork network, HashSet<EntityUid> pipes)
    {
        foreach (var pipeUid in pipes)
        {
            var pipe = Comp<LiquidPipeComponent>(pipeUid);
            pipe.NetworkId = network.Id;
            pipe.ConnectedDirections = PipeDirection.None;
            foreach (var neighbor in GetAdjacentPipes((pipeUid, pipe)))
            {
                var offset = Transform(neighbor).LocalPosition - Transform(pipeUid).LocalPosition;
                pipe.ConnectedDirections |= offset.X > 0.5f ? PipeDirection.East : offset.X < -0.5f ? PipeDirection.West :
                    offset.Y > 0 ? PipeDirection.North : PipeDirection.South;
                // Both endpoints are visited; retain each physical edge only once.
                if (pipeUid.CompareTo(neighbor) < 0)
                    network.Edges.Add((pipeUid, neighbor));
            }
            Dirty(pipeUid, pipe);
        }
        _activeNetworks.Add(network.Id);
        RebuildEndpoints(network);
    }

    private void RemovePipeFromItsNetwork(Entity<LiquidPipeComponent> ent)
    {
        if (ent.Comp.NetworkId < 0)
            return;

        if (_networks.TryGetValue(ent.Comp.NetworkId, out var network))
        {
            network.Pipes.Remove(ent);
            if (network.Pipes.Count == 0)
            {
                _networks.Remove(ent.Comp.NetworkId);
                _activeNetworks.Remove(ent.Comp.NetworkId);
            }
        }

        ent.Comp.NetworkId = -1;
        ent.Comp.ConnectedDirections = PipeDirection.None;
        ent.Comp.MachineInputDirections = ent.Comp.MachineOutputDirections = PipeDirection.None;
        Dirty(ent);
    }

    private List<EntityUid> GetAdjacentPipes(Entity<LiquidPipeComponent> ent)
    {
        var result = new List<EntityUid>();
        var xform = Transform(ent);

        if (xform.GridUid is not EntityUid gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return result;


        var pos = _mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);

        foreach (var direction in CardinalDirections)
        {
            ForAnchoredEntities(gridUid, grid, pos.Offset(direction), entity =>
            {
                if (!TerminatingOrDeleted(entity) && HasComp<LiquidPipeComponent>(entity))
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
    public readonly List<(EntityUid First, EntityUid Second)> Edges = new();
    public int EdgeCursor;
    public readonly List<(EntityUid Pipe, EntityUid Machine, Direction Face, LiquidPortMode Mode)> Endpoints = new();
}
