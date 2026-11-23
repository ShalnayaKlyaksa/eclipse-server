using System.Linq;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.Industrial;

public sealed partial class LiquidPipeNetworkSystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private readonly HashSet<int> _activeNetworks = new();
    private float _fluidElapsed;
    private bool _transferring;

    private void InitializeFluids()
    {
        SubscribeLocalEvent<LiquidPipeComponent, SolutionContainerChangedEvent>(OnContentsChanged);
    }

    private void OnContentsChanged(Entity<LiquidPipeComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (args.SolutionId != LiquidPipeComponent.SolutionName)
            return;
        _appearance.SetData(ent, SolutionContainerVisuals.FillFraction, args.Solution.FillFraction);
        _appearance.SetData(ent, SolutionContainerVisuals.Color, args.Solution.GetColor(_prototypes));
        if (!_transferring && ent.Comp.NetworkId >= 0)
            _activeNetworks.Add(ent.Comp.NetworkId);
    }

    private void UpdateFluids(float frameTime)
    {
        if (_activeNetworks.Count == 0)
        {
            _fluidElapsed = 0;
            return;
        }
        _fluidElapsed += frameTime;
        if (_fluidElapsed < 0.25f)
            return;
        var elapsed = MathF.Min(_fluidElapsed, 1f);
        _fluidElapsed = 0;
        _transferring = true;
        try
        {
            foreach (var id in _activeNetworks.ToArray())
            {
                if (!_networks.TryGetValue(id, out var network) || !TransferFluid(network, elapsed))
                    _activeNetworks.Remove(id);
            }
        }
        finally
        {
            _transferring = false;
        }
    }

    /// <summary>
    /// Move real solution down fill-level differences, through physical edges.
    /// One network-wide budget honours its weakest tier; rotating the first edge
    /// prevents a busy branch from permanently starving the others.
    /// </summary>
    private bool TransferFluid(LiquidPipeNetwork network, float elapsed)
    {
        var operations = network.Edges.Count + network.Endpoints.Count;
        if (operations == 0)
            return false;
        var budget = FixedPoint2.New(network.ThroughputPerSecond * elapsed);
        var moved = false;
        var start = network.EdgeCursor;
        for (var i = 0; i < operations && budget > 0; i++)
        {
            var index = (start + i) % operations;
            network.EdgeCursor = (index + 1) % operations;
            if (index >= network.Edges.Count)
            {
                var transferred = TransferEndpoint(network.Endpoints[index - network.Edges.Count], budget);
                budget -= transferred;
                moved |= transferred > 0;
                continue;
            }
            var (a, b) = network.Edges[index];
            if (!TryComp<LiquidPipeComponent>(a, out var pipeA) || pipeA.NetworkId != network.Id ||
                !TryComp<LiquidPipeComponent>(b, out var pipeB) || pipeB.NetworkId != network.Id ||
                !Transform(a).Anchored || !Transform(b).Anchored || TerminatingOrDeleted(a) || TerminatingOrDeleted(b) ||
                !_solutions.TryGetSolution(a, LiquidPipeComponent.SolutionName, out var first, out var firstSolution) ||
                !_solutions.TryGetSolution(b, LiquidPipeComponent.SolutionName, out var second, out var secondSolution) ||
                firstSolution.MaxVolume <= 0 || secondSolution.MaxVolume <= 0)
                continue;
            if (firstSolution.FillFraction < secondSolution.FillFraction)
            {
                (first, second) = (second, first);
                (firstSolution, secondSolution) = (secondSolution, firstSolution);
            }
            var difference = firstSolution.FillFraction - secondSolution.FillFraction;
            var equalizingVolume = FixedPoint2.New(difference /
                (1f / firstSolution.MaxVolume.Float() + 1f / secondSolution.MaxVolume.Float()));
            var amount = FixedPoint2.Min(budget, equalizingVolume, secondSolution.AvailableVolume);
            if (amount <= 0 || !_solutions.TryTransferSolution(second.Value, firstSolution, amount))
                continue;
            // TryTransferSolution updates the recipient; update the donor too so
            // reactions, replication and the empty-colour reset see its new state.
            _solutions.UpdateChemicals(first.Value);
            budget -= amount;
            moved = true;
        }
        return moved;
    }
}
