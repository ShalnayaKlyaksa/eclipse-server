using System.Linq;
using Content.Shared._Eclipse.Industrial;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Eclipse.Industrial;

public sealed partial class ItemPipeNetworkSystem : EntitySystem
{
    [Dependency] private IndustrialProcessorSystem _processor = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;

    private static readonly Direction[] CardinalDirections =
        [Direction.North, Direction.South, Direction.East, Direction.West];

    private readonly Dictionary<int, ItemPipeNetwork> _networks = new();
    private int _nextNetworkId = 1;
    private readonly HashSet<EntityUid> _pendingRoots = new();
    public int TopologyRebuilds { get; private set; }

    public void HandlePipeRemoved(Entity<ItemPipeComponent> ent)
    {
        var neighbors = GetAdjacentPipes(ent);
        RemovePipeFromItsNetwork(ent);
        _pendingRoots.UnionWith(neighbors);
    }

    private void RebuildComponents(IEnumerable<EntityUid> roots)
    {
        var visited = new HashSet<EntityUid>();
        foreach (var root in roots)
        {
            if (TerminatingOrDeleted(root) || visited.Contains(root) ||
                !TryComp<ItemPipeComponent>(root, out var pipe) || !Transform(root).Anchored)
                continue;
            RebuildNetworkFrom((root, pipe));
            if (TryGetNetwork(pipe.NetworkId, out var network))
                visited.UnionWith(network.Pipes);
        }
    }

    public void InvalidateEndpoints(int networkId)
    {
        if (_networks.TryGetValue(networkId, out var network))
            network.EndpointsDirty = true;
    }

    public bool TryGetNetwork(int networkId, out ItemPipeNetwork network)
    {
        return _networks.TryGetValue(networkId, out network!);
    }

    public void RebuildNetworkFrom(Entity<ItemPipeComponent> ent)
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

        // A rebuild replaces membership for the entire connected component, not just its starting pipe.
        // Otherwise the previous networks retain these pipes and keep processing duplicate transfers.
        foreach (var pipe in connectedPipes)
            RemovePipeFromItsNetwork((pipe, Comp<ItemPipeComponent>(pipe)));

        var network = CreateNetwork(connectedPipes);
        AssignNetworkToPipes(network, connectedPipes);
        _pendingRoots.ExceptWith(connectedPipes);
        TopologyRebuilds++;
    }

    public void InvalidateProcessorConnections(Entity<IndustrialProcessorComponent> ent)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not EntityUid gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var pos = _mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);
        foreach (var direction in CardinalDirections)
        {
            var enumerator = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, pos.Offset(direction));
            while (enumerator.MoveNext(out var entity) && entity != null)
            {
                if (TryComp<ItemPipeComponent>(entity, out var pipe))
                    InvalidateEndpoints(pipe.NetworkId);
            }
        }
    }

    public void QueueTopologyChange(Entity<ItemPipeComponent> ent)
    {
        _pendingRoots.UnionWith(GetAdjacentPipes(ent));
        if (Transform(ent).Anchored && !TerminatingOrDeleted(ent))
            _pendingRoots.Add(ent);
        else
            RemovePipeFromItsNetwork(ent);
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

            foreach (var adjacent in GetConnectedAdjacentPipes(current))
            {
                if (!result.Contains(adjacent))
                    queue.Enqueue(adjacent);
            }
        }

        return result;
    }

    private IEnumerable<EntityUid> GetConnectedAdjacentPipes(EntityUid pipeUid)
    {
        var xform = Transform(pipeUid);
        if (!xform.Anchored || xform.GridUid is not EntityUid gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            yield break;
        }

        var pos = _mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);

        foreach (var direction in CardinalDirections)
        {
            var enumerator = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, pos.Offset(direction));
            while (enumerator.MoveNext(out var entity) && entity != null)
            {
                if (entity == pipeUid || TerminatingOrDeleted(entity.Value) || !HasComp<ItemPipeComponent>(entity))
                    continue;

                if (ItemPipeConnectionHelper.ArePipesConnected(pipeUid, entity.Value, direction, EntityManager, _mapSystem))
                    yield return entity.Value;
            }
        }
    }

    private ItemPipeNetwork CreateNetwork(HashSet<EntityUid> pipes)
    {
        var network = new ItemPipeNetwork
        {
            Id = _nextNetworkId++,
            EffectiveTier = PipeTier.Perfect,
        };

        foreach (var pipeUid in pipes)
        {
            network.Pipes.Add(pipeUid);
            var pipe = Comp<ItemPipeComponent>(pipeUid);
            network.EffectiveTier = PipeTierHelper.GetWeakest(network.EffectiveTier, pipe.Tier);
        }

        var specs = PipeTierHelper.GetSpecs(network.EffectiveTier);
        network.ThroughputPerSecond = specs.ThroughputPerSecond;

        _networks[network.Id] = network;
        return network;
    }

    private void AssignNetworkToPipes(ItemPipeNetwork network, HashSet<EntityUid> pipes)
    {
        foreach (var pipeUid in pipes)
        {
            var pipe = Comp<ItemPipeComponent>(pipeUid);
            pipe.NetworkId = network.Id;
            Dirty(pipeUid, pipe);
        }
    }

    private void RemovePipeFromItsNetwork(Entity<ItemPipeComponent> ent)
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

    private List<EntityUid> GetAdjacentPipes(Entity<ItemPipeComponent> ent)
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
                if (HasComp<ItemPipeComponent>(entity))
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

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_pendingRoots.Count > 0)
        {
            var roots = _pendingRoots.ToArray();
            _pendingRoots.Clear();
            RebuildComponents(roots);
        }
        foreach (var network in _networks.Values)
        {
            network.SecondAccumulator += frameTime;
            if (network.SecondAccumulator >= 1f)
            {
                network.SecondAccumulator %= 1f;
                network.TransfersThisSecond = 0;
                network.MachineTransfersThisSecond.Clear();
            }
            if (network.ThroughputPerSecond <= 0)
                continue;
            var interval = 1f / network.ThroughputPerSecond;
            // Bound catch-up work after a long frame. Idle networks never accumulate a burst.
            network.TimeSinceLastTransfer = MathF.Min(network.TimeSinceLastTransfer + frameTime, 1f);
            var attempts = 0;
            while (network.TimeSinceLastTransfer + 0.00001f >= interval &&
                   attempts++ < MathF.Ceiling(network.ThroughputPerSecond))
            {
                network.TimeSinceLastTransfer = MathF.Max(0, network.TimeSinceLastTransfer - interval);
                if (network.TransfersThisSecond >= network.ThroughputPerSecond)
                    continue;
                TryTransferOne(network);
            }
        }
    }

    private bool TryTransferOne(ItemPipeNetwork network)
    {
        if (network.EndpointsDirty)
        {
            CollectProcessors(network, PortMode.Output, network.Sources);
            CollectProcessors(network, PortMode.Input, network.Sinks);
            network.EndpointsDirty = false;
            network.EndpointRebuilds++;
        }
        for (var i = 0; i < network.Sources.Count; i++)
        {
            var sourceIndex = (network.SourceCursor + i) % network.Sources.Count;
            var sourceUid = network.Sources[sourceIndex];
            if (TerminatingOrDeleted(sourceUid) || !Transform(sourceUid).Anchored ||
                !TryComp<IndustrialProcessorComponent>(sourceUid, out var sourceComp))
                continue;
            var transfers = network.MachineTransfersThisSecond.GetValueOrDefault(sourceUid);
            if (transfers >= sourceComp.MaxAutoTransferPerSecond)
                continue;
            for (var j = 0; j < network.Sinks.Count; j++)
            {
                var sinkIndex = (network.SinkCursor + j) % network.Sinks.Count;
                var sinkUid = network.Sinks[sinkIndex];
                if (sourceUid == sinkUid || TerminatingOrDeleted(sinkUid) || !Transform(sinkUid).Anchored ||
                    !TryComp<IndustrialProcessorComponent>(sinkUid, out var sinkComp))
                    continue;
                if (!_processor.TryPipeTransfer((sourceUid, sourceComp), (sinkUid, sinkComp)))
                    continue;
                network.TransfersThisSecond++;
                network.MachineTransfersThisSecond[sourceUid] = transfers + 1;
                network.SourceCursor = (sourceIndex + 1) % network.Sources.Count;
                network.SinkCursor = (sinkIndex + 1) % network.Sinks.Count;
                return true;
            }
        }
        return false;
    }

    private void CollectProcessors(ItemPipeNetwork network, PortMode mode, List<EntityUid> result)
    {
        result.Clear();
        var seen = new HashSet<EntityUid>();
        foreach (var pipeUid in network.Pipes)
        {
            if (TerminatingOrDeleted(pipeUid) || !TryComp<ItemPipeComponent>(pipeUid, out var pipe))
                continue;
            if (mode == PortMode.Input && pipe.TransferMode == PipeTransferMode.Extract ||
                mode == PortMode.Output && pipe.TransferMode == PipeTransferMode.Insert)
                continue;
            CollectProcessorsOnPipeSide(pipeUid, mode, seen, result);
        }
        result.Sort();
    }

    private void CollectProcessorsOnPipeSide(
        EntityUid pipeUid,
        PortMode requiredMode,
        HashSet<EntityUid> seen,
        List<EntityUid> result)
    {
        var pipeXform = Transform(pipeUid);
        if (!pipeXform.Anchored || pipeXform.GridUid is not EntityUid gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var pipePos = _mapSystem.TileIndicesFor(gridUid, grid, pipeXform.Coordinates);

        foreach (var direction in CardinalDirections)
        {
            ForAnchoredEntities(gridUid, grid, pipePos.Offset(direction), entity =>
            {
                if (TerminatingOrDeleted(entity) || !HasComp<IndustrialProcessorComponent>(entity) || seen.Contains(entity))
                    return;

                var processor = entity;
                var processorComp = Comp<IndustrialProcessorComponent>(processor);
                if (processorComp.GetItemPortMode(direction.GetOpposite()) != requiredMode)
                    return;

                if (!ItemPipeConnectionHelper.IsProcessorConnectedToPipe(processor, pipeUid, direction.GetOpposite(), EntityManager))
                    return;

                seen.Add(processor);
                result.Add(processor);
            });
        }
    }
}

public sealed class ItemPipeNetwork
{
    public bool EndpointsDirty = true;
    public int EndpointRebuilds;
    public List<EntityUid> Sources = new();
    public List<EntityUid> Sinks = new();
    public int SourceCursor;
    public int SinkCursor;
    public int Id;
    public HashSet<EntityUid> Pipes = new();
    public PipeTier EffectiveTier = PipeTier.Perfect;
    public float ThroughputPerSecond;
    public float TimeSinceLastTransfer;
    public float SecondAccumulator;
    public int TransfersThisSecond;
    public Dictionary<EntityUid, int> MachineTransfersThisSecond = new();
}
