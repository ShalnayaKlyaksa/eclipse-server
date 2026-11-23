using Content.Server.Power.Generator;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.Power;

[RegisterComponent]
public sealed partial class ReagentFuelCellComponent : Component
{
    [DataField] public string InputSolution = "tank";
    [DataField] public string OutputSolution = "output";
    [DataField] public Dictionary<ProtoId<ReagentPrototype>, float> Inputs = new();
    [DataField] public Dictionary<ProtoId<ReagentPrototype>, float> Outputs = new();
    public float FuelCredit;
}

public sealed class ReagentFuelCellSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<ReagentFuelCellComponent, GeneratorGetFuelEvent>(GetFuel);
        SubscribeLocalEvent<ReagentFuelCellComponent, GeneratorUseFuel>(UseFuel);
    }
    private void GetFuel(Entity<ReagentFuelCellComponent> ent, ref GeneratorGetFuelEvent args)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.InputSolution, out _, out var input) ||
            !_solutions.TryGetSolution(ent.Owner, ent.Comp.OutputSolution, out _, out var output) || ent.Comp.Inputs.Count == 0) return;
        var fuel = float.MaxValue;
        foreach (var (reagent, amount) in ent.Comp.Inputs)
            fuel = MathF.Min(fuel, amount > 0 ? input.GetTotalPrototypeQuantity(reagent).Float() / amount : 0);
        var outputPerFuel = 0f;
        foreach (var amount in ent.Comp.Outputs.Values) outputPerFuel += amount;
        if (outputPerFuel > 0) fuel = MathF.Min(fuel, output.AvailableVolume.Float() / outputPerFuel);
        args.Fuel = ent.Comp.FuelCredit + MathF.Max(0, MathF.Floor(fuel * 100) / 100);
    }
    private void UseFuel(Entity<ReagentFuelCellComponent> ent, ref GeneratorUseFuel args)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.InputSolution, out var input, out _) ||
            !_solutions.TryGetSolution(ent.Owner, ent.Comp.OutputSolution, out var output, out _)) return;
        var packets = Math.Max(0, (int) MathF.Ceiling((args.FuelUsed - ent.Comp.FuelCredit - .000001f) * 100));
        ent.Comp.FuelCredit = MathF.Max(0, ent.Comp.FuelCredit + packets / 100f - args.FuelUsed);
        foreach (var (reagent, amount) in ent.Comp.Inputs)
            _solutions.RemoveReagent(input!.Value, reagent, FixedPoint2.FromCents((int) MathF.Ceiling(packets * amount)));
        foreach (var (reagent, amount) in ent.Comp.Outputs)
            _solutions.TryAddReagent(output!.Value, reagent, FixedPoint2.FromCents((int) MathF.Floor(packets * amount)));
    }
}
