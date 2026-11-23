using System.Linq;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;

namespace Content.Shared._Eclipse.Industrial;

public abstract partial class SharedIndustrialProcessorSystem
{
    [Dependency] protected readonly SharedSolutionContainerSystem Solutions = default!;

    protected bool HasFluidIngredients(EntityUid uid, IndustrialRecipePrototype recipe)
    {
        if (recipe.FluidInputs.Count == 0 && recipe.Bath.Count == 0)
            return true;
        if (!TryComp<IndustrialFluidProcessorComponent>(uid, out var fluid) ||
            !Solutions.TryGetSolution(uid, fluid.InputSolution, out _, out var solution))
            return false;
        foreach (var reagent in recipe.FluidInputs.Keys.Union(recipe.Bath.Keys))
        {
            if (solution.GetTotalPrototypeQuantity(reagent) <
                recipe.FluidInputs.GetValueOrDefault(reagent) + recipe.Bath.GetValueOrDefault(reagent))
                return false;
        }
        return true;
    }

    protected bool CanAcceptFluidOutputs(EntityUid uid, IndustrialRecipePrototype recipe)
    {
        if (recipe.FluidOutputs.Count == 0)
            return true;
        return TryComp<IndustrialFluidProcessorComponent>(uid, out var fluid) &&
               Solutions.TryGetSolution(uid, fluid.OutputSolution, out _, out var solution) &&
               solution.AvailableVolume >= recipe.FluidOutputs.Values.Aggregate(FixedPoint2.Zero, (sum, value) => sum + value);
    }

    /// <summary>Whole mixtures only: a pipe must never act as a free reagent separator.</summary>
    public FixedPoint2 FluidIntakeLimit(Entity<IndustrialProcessorComponent> ent, Solution incoming)
    {
        if (incoming.Volume <= 0 || !TryComp<IndustrialFluidProcessorComponent>(ent, out var fluid) ||
            !Solutions.TryGetSolution(ent.Owner, fluid.InputSolution, out _, out var stored) ||
            !RecipesByProcessor.TryGetValue(ent.Comp.ProcessorType, out var recipes))
            return FixedPoint2.Zero;
        var best = FixedPoint2.Zero;
        foreach (var recipe in recipes)
        {
            if (ent.Comp.SelectedRecipe is { } selected && recipe.ID != selected.Id || !IsRecipeAvailable(ent, recipe))
                continue;
            // Do not pollute a partly filled tank with ingredients for another automatic recipe.
            if (stored.Contents.Any(q => !recipe.FluidInputs.ContainsKey(q.Reagent.Prototype) &&
                                         !recipe.Bath.ContainsKey(q.Reagent.Prototype)))
                continue;
            var limit = FixedPoint2.Min(incoming.Volume, stored.AvailableVolume);
            foreach (var quantity in incoming.Contents)
            {
                var reagent = quantity.Reagent.Prototype;
                var target = recipe.FluidInputs.GetValueOrDefault(reagent) * 4 + recipe.Bath.GetValueOrDefault(reagent);
                var free = target - stored.GetTotalPrototypeQuantity(reagent);
                if (free <= 0)
                {
                    limit = FixedPoint2.Zero;
                    break;
                }
                limit = FixedPoint2.Min(limit, FixedPoint2.New(free.Float() * incoming.Volume.Float() / quantity.Quantity.Float()));
            }
            if (limit > best)
                best = limit;
        }
        return best;
    }
}
