using Content.Shared._Eclipse.Industrial;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;

namespace Content.Server._Eclipse.Industrial;

public sealed partial class IndustrialProcessorSystem
{
    private void OnFluidContentsChanged(Entity<IndustrialProcessorComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (ent.Comp.InTransaction || !TryComp<IndustrialFluidProcessorComponent>(ent, out var fluid) ||
            args.SolutionId != fluid.InputSolution && args.SolutionId != fluid.OutputSolution)
            return;
        ent.Comp.WaitingForOutput = false;
        if (ent.Comp.IsWorking && ent.Comp.CurrentRecipe is { } id &&
            PrototypeManager.TryIndex(id, out IndustrialRecipePrototype? recipe) && !HasFluidIngredients(ent, recipe))
            CancelCycle(ent);
        else if (ent.Comp.AutoStart)
            TryStartProcessing(ent);
        UpdateAppearance(ent);
        UpdateUserInterface(ent);
    }

    private List<(Entity<SolutionComponent> Entity, Solution Snapshot)> SnapshotFluids(EntityUid uid)
    {
        var snapshots = new List<(Entity<SolutionComponent>, Solution)>();
        if (!TryComp<IndustrialFluidProcessorComponent>(uid, out var fluid))
            return snapshots;
        foreach (var name in new HashSet<string> { fluid.InputSolution, fluid.OutputSolution })
            if (Solutions.TryGetSolution(uid, name, out var entity, out var solution))
                snapshots.Add((entity.Value, solution.Clone()));
        return snapshots;
    }

    private bool CommitFluids(EntityUid uid, IndustrialRecipePrototype recipe)
    {
        if (!HasFluidIngredients(uid, recipe) || !CanAcceptFluidOutputs(uid, recipe))
            return false;
        if (!TryComp<IndustrialFluidProcessorComponent>(uid, out var fluid))
            return recipe.FluidInputs.Count == 0 && recipe.FluidOutputs.Count == 0 && recipe.Bath.Count == 0;
        if (recipe.FluidInputs.Count > 0)
        {
            if (!Solutions.TryGetSolution(uid, fluid.InputSolution, out var entity, out var solution))
                return false;
            foreach (var (reagent, amount) in recipe.FluidInputs)
                if (solution.RemoveReagent(reagent.Id, amount, ignoreReagentData: true) != amount)
                    return false;
            Solutions.UpdateChemicals(entity.Value);
        }
        if (recipe.FluidOutputs.Count > 0)
        {
            if (!Solutions.TryGetSolution(uid, fluid.OutputSolution, out var entity, out var solution))
                return false;
            foreach (var (reagent, amount) in recipe.FluidOutputs)
                solution.AddReagent(reagent.Id, amount);
            Solutions.UpdateChemicals(entity.Value);
        }
        return HasBathAfterCommit(uid, recipe);
    }

    private bool HasBathAfterCommit(EntityUid uid, IndustrialRecipePrototype recipe)
    {
        if (recipe.Bath.Count == 0)
            return true;
        if (!TryComp<IndustrialFluidProcessorComponent>(uid, out var fluid) ||
            !Solutions.TryGetSolution(uid, fluid.InputSolution, out _, out var solution))
            return false;
        foreach (var (reagent, amount) in recipe.Bath)
            if (solution.GetTotalPrototypeQuantity(reagent) < amount)
                return false;
        return true;
    }

    private void RestoreFluids(List<(Entity<SolutionComponent> Entity, Solution Snapshot)> snapshots)
    {
        foreach (var (entity, snapshot) in snapshots)
        {
            if (TerminatingOrDeleted(entity))
                continue;
            Solutions.RemoveAllSolution(entity);
            Solutions.TryAddSolution(entity, snapshot);
        }
    }
}
