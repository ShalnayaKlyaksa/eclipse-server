using System.Linq;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Eclipse.Industrial;

public sealed partial class LiquidPipeNetworkSystem
{
    private readonly HashSet<int> _dirtyEndpoints = new();

    private void InitializeProcessors()
    {
        SubscribeLocalEvent<IndustrialFluidProcessorComponent, MapInitEvent>((Entity<IndustrialFluidProcessorComponent> ent, ref MapInitEvent _) => InvalidateProcessorConnections(ent));
        SubscribeLocalEvent<IndustrialFluidProcessorComponent, AnchorStateChangedEvent>((Entity<IndustrialFluidProcessorComponent> ent, ref AnchorStateChangedEvent _) => InvalidateProcessorConnections(ent));
        SubscribeLocalEvent<IndustrialFluidProcessorComponent, EntityTerminatingEvent>((Entity<IndustrialFluidProcessorComponent> ent, ref EntityTerminatingEvent _) => InvalidateProcessorConnections(ent));
        SubscribeLocalEvent<IndustrialFluidProcessorComponent, SolutionContainerChangedEvent>(OnProcessorContentsChanged);
        SubscribeLocalEvent<LiquidPipeEndpointComponent, MapInitEvent>((Entity<LiquidPipeEndpointComponent> ent, ref MapInitEvent _) => InvalidateProcessorConnections(ent));
        SubscribeLocalEvent<LiquidPipeEndpointComponent, AnchorStateChangedEvent>((Entity<LiquidPipeEndpointComponent> ent, ref AnchorStateChangedEvent _) => InvalidateProcessorConnections(ent));
        SubscribeLocalEvent<LiquidPipeEndpointComponent, EntityTerminatingEvent>((Entity<LiquidPipeEndpointComponent> ent, ref EntityTerminatingEvent _) => InvalidateProcessorConnections(ent));
        SubscribeLocalEvent<LiquidPipeEndpointComponent, SolutionContainerChangedEvent>((Entity<LiquidPipeEndpointComponent> ent, ref SolutionContainerChangedEvent _) => WakeEndpointNetworks(ent));
    }

    private void OnProcessorContentsChanged(Entity<IndustrialFluidProcessorComponent> ent, ref SolutionContainerChangedEvent args)
        => WakeEndpointNetworks(ent);

    private void WakeEndpointNetworks(EntityUid uid)
    {
        foreach (var pipe in GetProcessorPipes(uid))
            if (Comp<LiquidPipeComponent>(pipe).NetworkId is var id && id >= 0)
                _activeNetworks.Add(id);
    }

    public void InvalidateProcessorConnections(EntityUid uid)
    {
        foreach (var pipe in GetProcessorPipes(uid))
        {
            var id = Comp<LiquidPipeComponent>(pipe).NetworkId;
            if (id >= 0)
            {
                _dirtyEndpoints.Add(id);
                _activeNetworks.Add(id);
            }
        }
    }

    private IEnumerable<EntityUid> GetProcessorPipes(EntityUid uid)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            yield break;
        var tile = _mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);
        foreach (var direction in CardinalDirections)
        {
            var enumerator = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tile.Offset(direction));
            while (enumerator.MoveNext(out var other) && other != null)
                if (!TerminatingOrDeleted(other.Value) && HasComp<LiquidPipeComponent>(other.Value))
                    yield return other.Value;
        }
    }

    private void RebuildEndpoints(LiquidPipeNetwork network)
    {
        network.Endpoints.Clear();
        foreach (var uid in network.Pipes)
        {
            var pipe = Comp<LiquidPipeComponent>(uid);
            pipe.ConnectedDirections &= ~(pipe.MachineInputDirections | pipe.MachineOutputDirections);
            pipe.MachineInputDirections = pipe.MachineOutputDirections = PipeDirection.None;
            var xform = Transform(uid);
            if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
                continue;
            var tile = _mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);
            foreach (var direction in CardinalDirections)
            {
                var enumerator = _mapSystem.GetAnchoredEntitiesEnumerator(gridUid, grid, tile.Offset(direction));
                while (enumerator.MoveNext(out var other) && other != null)
                {
                    if (TerminatingOrDeleted(other.Value)) continue;
                    var mode = LiquidPortMode.Disabled;
                    if (HasComp<IndustrialFluidProcessorComponent>(other.Value) && TryComp<IndustrialProcessorComponent>(other.Value, out var processor))
                        mode = processor.GetLiquidPortMode(direction.GetOpposite());
                    else if (TryComp<LiquidPipeEndpointComponent>(other.Value, out var tank))
                        mode = tank.GetPortMode(direction.GetOpposite());
                    if (mode == LiquidPortMode.Disabled)
                        continue;
                    network.Endpoints.Add((uid, other.Value, direction.GetOpposite(), mode));
                    if (mode == LiquidPortMode.Input)
                        pipe.MachineInputDirections |= direction.ToPipeDirection();
                    else
                        pipe.MachineOutputDirections |= direction.ToPipeDirection();
                }
            }
            pipe.ConnectedDirections |= pipe.MachineInputDirections | pipe.MachineOutputDirections;
            Dirty(uid, pipe);
        }
    }

    private FixedPoint2 TransferEndpoint((EntityUid Pipe, EntityUid Machine, Direction Face, LiquidPortMode Mode) endpoint, FixedPoint2 budget)
    {
        var (pipe, uid, face, mode) = endpoint;
        if (TerminatingOrDeleted(uid) || !Transform(uid).Anchored || !Transform(pipe).Anchored) return FixedPoint2.Zero;
        string solutionName;
        IndustrialProcessorComponent? processor = null;
        LiquidPipeEndpointComponent? tank = null;
        if (TryComp(uid, out processor) && TryComp<IndustrialFluidProcessorComponent>(uid, out var fluid))
        {
            if (processor.InTransaction || processor.GetLiquidPortMode(face) != mode) return FixedPoint2.Zero;
            solutionName = mode == LiquidPortMode.Input ? fluid.InputSolution : fluid.OutputSolution;
        }
        else if (TryComp(uid, out tank) && tank.GetPortMode(face) == mode)
            solutionName = mode == LiquidPortMode.Input ? tank.InputSolution : tank.OutputSolution;
        else return FixedPoint2.Zero;
        if (!_solutions.TryGetSolution(pipe, LiquidPipeComponent.SolutionName, out var pipeEntity, out var pipeSolution) ||
            !_solutions.TryGetSolution(uid, solutionName, out var machineEntity, out var machineSolution)) return FixedPoint2.Zero;
        if (mode == LiquidPortMode.Input && tank is { AllowedInputs.Count: > 0 } &&
            pipeSolution.Contents.Any(r => !tank.AllowedInputs.Contains(r.Reagent.Prototype))) return FixedPoint2.Zero;
        var source = mode == LiquidPortMode.Input ? pipeEntity.Value : machineEntity.Value;
        var recipient = mode == LiquidPortMode.Input ? machineEntity.Value : pipeEntity.Value;
        var sourceSolution = mode == LiquidPortMode.Input ? pipeSolution : machineSolution;
        var destinationSolution = mode == LiquidPortMode.Input ? machineSolution : pipeSolution;
        var amount = FixedPoint2.Min(budget, sourceSolution.Volume, destinationSolution.AvailableVolume);
        if (mode == LiquidPortMode.Input && processor != null && processor.ProcessorType != IndustrialProcessorType.FluidTank)
            amount = FixedPoint2.Min(amount, EntityManager.System<IndustrialProcessorSystem>().FluidIntakeLimit((uid, processor), sourceSolution));
        if (amount <= 0 || !_solutions.TryTransferSolution(recipient, sourceSolution, amount))
            return FixedPoint2.Zero;
        _solutions.UpdateChemicals(source);
        return amount;
    }
}
