using System.Linq;
using Content.Shared._Eclipse.Industrial;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Eclipse.Industrial;

public sealed class ItemPipeNetworkSystem : EntitySystem
{
    [Dependency] private readonly IndustrialProcessorSystem _processor = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;

    private static readonly Direction[] CardinalDirections =
        [Direction.North, Direction.South, Direction.East, Direction.West];

    private readonly Dictionary<int, ItemPipeNetwork> _networks = new();
    private int _nextNetworkId = 1;

    public void HandlePipeRemoved(Entity<ItemPipeComponent> ent)
    {
        var adjacentPipes = GetAdjacentPipes(ent);
        RemovePipeFromItsNetwork(ent);

        foreach (var adjacent in adjacentPipes)
        {
            if (Exists(adjacent))
                RebuildNetworkFrom((adjacent, Comp<ItemPipeComponent>(adjacent)));
        }
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
                if (!HasComp<ItemPipeComponent>(entity) || rebuilt.Contains(entity))
                    return;

                RebuildNetworkFrom((entity, Comp<ItemPipeComponent>(entity)));
                rebuilt.Add(entity);
            });
        }
    }

    public void RebuildAdjacentPipeNetworks(Entity<ItemPipeComponent> ent)
    {
        foreach (var adjacent in GetConnectedAdjacentPipes(ent).Concat(GetAdjacentPipes(ent)))
        {
            if (Exists(adjacent))
                RebuildNetworkFrom((adjacent, Comp<ItemPipeComponent>(adjacent)));
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
                if (entity == pipeUid || !HasComp<ItemPipeComponent>(entity))
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
        network.TransferDelay = specs.TransferDelay;

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

        if (!xform.Anchored)
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

        foreach (var network in _networks.Values)
        {
            network.SecondAccumulator += frameTime;
            if (network.SecondAccumulator >= 1f)
            {
                network.SecondAccumulator = 0f;
                network.TransfersThisSecond = 0;
                network.MachineTransfersThisSecond.Clear();
            }

            network.TimeSinceLastTransfer += frameTime;
            if (network.TimeSinceLastTransfer < network.TransferDelay)
                continue;

            if (TryTransferOne(network))
                network.TimeSinceLastTransfer = 0f;
        }
    }

    private bool TryTransferOne(ItemPipeNetwork network)
    {
        if (network.TransfersThisSecond >= (int) network.ThroughputPerSecond)
            return false;

        var sources = GetConnectedOutputProcessors(network)
            .OrderBy(uid => uid)
            .ToList();

        foreach (var sourceUid in sources)
        {
            Entity<IndustrialProcessorComponent> source = (sourceUid, Comp<IndustrialProcessorComponent>(sourceUid));
            var transfers = network.MachineTransfersThisSecond.GetValueOrDefault(sourceUid, 0);
            if (transfers >= source.Comp.MaxAutoTransferPerSecond)
                continue;

            if (!_processor.TryGetFirstOutputProto(source, out var protoId))
                continue;

            var sinks = GetConnectedInputProcessors(network, protoId)
                .Where(uid => uid != sourceUid)
                .OrderBy(uid => uid)
                .ToList();

            foreach (var sinkUid in sinks)
            {
                Entity<IndustrialProcessorComponent> sink = (sinkUid, Comp<IndustrialProcessorComponent>(sinkUid));
                if (_processor.TryPipeTransfer(source, sink, protoId))
                {
                    network.TransfersThisSecond++;
                    network.MachineTransfersThisSecond[sourceUid] = transfers + 1;
                    return true;
                }
            }
        }

        return false;
    }

    private List<EntityUid> GetConnectedOutputProcessors(ItemPipeNetwork network)
    {
        var result = new List<EntityUid>();
        var seen = new HashSet<EntityUid>();

        foreach (var pipeUid in network.Pipes)
        {
            CollectProcessorsOnPipeSide(pipeUid, PortMode.Output, network.Pipes, seen, result);
        }

        return result;
    }

    private List<EntityUid> GetConnectedInputProcessors(ItemPipeNetwork network, string protoId)
    {
        var result = new List<EntityUid>();
        var seen = new HashSet<EntityUid>();

        foreach (var pipeUid in network.Pipes)
        {
            CollectProcessorsOnPipeSide(pipeUid, PortMode.Input, network.Pipes, seen, result);
        }

        result.RemoveAll(uid => !_processor.CanAcceptInputItem((uid, Comp<IndustrialProcessorComponent>(uid)), protoId));

        return result;
    }

    private void CollectProcessorsOnPipeSide(
        EntityUid pipeUid,
        PortMode requiredMode,
        HashSet<EntityUid> networkPipes,
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
                if (!HasComp<IndustrialProcessorComponent>(entity) || seen.Contains(entity))
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
    public int Id;
    public HashSet<EntityUid> Pipes = new();
    public PipeTier EffectiveTier = PipeTier.Perfect;
    public float ThroughputPerSecond;
    public float TransferDelay;
    public float TimeSinceLastTransfer;
    public float SecondAccumulator;
    public int TransfersThisSecond;
    public Dictionary<EntityUid, int> MachineTransfersThisSecond = new();
}
