using System.Linq;
using Content.Server.Nutrition.Components;
using Content.Shared._Eclipse.Cooking;
using Content.Shared.Chemistry.Components;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;

namespace Content.Server._Eclipse.Cooking;

public sealed partial class CulinarySystem
{
    private CulinaryMethod? PreparationMethod(EntityUid tool)
    {
        if (HasComp<CulinaryPreparationToolComponent>(tool)) return CulinaryMethod.Grind;
        return TryComp<UtensilComponent>(tool, out var utensil) && (utensil.Types & UtensilType.Knife) != 0
            ? CulinaryMethod.Chop : null;
    }

    private void OnPrepareFood(Entity<EdibleComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || HasComp<CulinaryStationComponent>(ent) || HasComp<CulinaryVesselComponent>(ent)) return;
        var method = PreparationMethod(args.Used);
        if (method == null || method == CulinaryMethod.Chop && HasComp<SliceableFoodComponent>(ent)) return;
        if (!FoodSolution(ent, out var food)) return;
        Stamp(ent, food);
        var data = ReadData(food.Comp.Solution);
        if (data == null) return;
        args.Handled = true;
        if ((data.Methods & (1u << (int) method.Value)) != 0)
        {
            Notify(ent, args.User, "culinary-preparation-done");
            return;
        }
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, CulinaryMath.Seconds(method.Value),
            new CulinaryPrepareDoAfterEvent(), ent, ent, args.Used)
        {
            NeedHand = true, BreakOnMove = true, BreakOnDamage = true, BreakOnHandChange = true,
        });
    }

    private void OnPreparedFood(Entity<EdibleComponent> ent, ref CulinaryPrepareDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used == null || PreparationMethod(args.Used.Value) is not {} method) return;
        args.Handled = ProcessFood(ent, method);
        if (args.Handled) Notify(ent, args.User, method == CulinaryMethod.Chop ? "culinary-chopped" : "culinary-ground");
    }

    private void OnSlicedFood(Entity<EdibleComponent> ent, ref BeforeFoodSlicedEvent args)
        => ProcessFood(ent, CulinaryMethod.Chop);

    private void OnStirred(Entity<CulinaryStationComponent> ent, ref CulinaryStirDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.Working || ent.Comp.AllowedMethods.Any(CulinaryMath.IsHot)
            || args.Used == null || !TryComp<UtensilComponent>(args.Used, out var utensil)
            || (utensil.Types & UtensilType.Spoon) == 0 || !Batch(ent, out var batch) || TotalVolume(ent) <= 0) return;
        CommitIngredients(ent, batch);
        var solution = batch.Comp.Solution;
        foreach (var reagent in solution.Contents.ToArray())
        {
            if (reagent.Reagent.Data?.OfType<CulinaryData>().FirstOrDefault() is not {} ingredient) continue;
            var prepared = (CulinaryData) ingredient.Clone();
            // Cold salads can be served; stirring does not cook raw protein reagents or alter properties.
            prepared.Cooked = true;
            ReplaceReagentData(solution, reagent, prepared);
        }
        _solutions.UpdateChemicals(batch);
        ent.Comp.Ready = true; ent.Comp.Status = "culinary-status-ready";
        args.Handled = true; Publish(ent);
    }

    /// <summary>Prepare a physical ingredient while retaining its item, volume, chemistry and per-reagent history.</summary>
    public bool ProcessFood(EntityUid uid, CulinaryMethod method)
    {
        if (method is not (CulinaryMethod.Chop or CulinaryMethod.Grind) || !FoodSolution(uid, out var food)) return false;
        Stamp(uid, food);
        var solution = food.Comp.Solution;
        var data = ReadData(solution);
        if (data == null || (data.Methods & (1u << (int) method)) != 0) return false;
        foreach (var reagent in solution.Contents.ToArray())
            if (reagent.Reagent.Data?.OfType<CulinaryData>().FirstOrDefault() is {} ingredient)
                ReplaceReagentData(solution, reagent, CulinaryMath.Process(ingredient, method, data));
        _solutions.UpdateChemicals(food);
        return true;
    }

    private static bool Prepared(Solution solution)
    {
        var data = solution.Contents.SelectMany(r => r.Reagent.Data?.OfType<CulinaryData>() ?? []).ToArray();
        return data.Length > 0 && data.All(d => d.Cooked || d.Methods != 0);
    }

    /// <summary>Use a filled vessel to pour in; use an empty vessel to take a batch to another appliance.</summary>
    public bool TransferVessel(Entity<CulinaryStationComponent> ent, EntityUid uid, CulinaryVesselComponent vessel, EntityUid? user = null)
    {
        if (ent.Comp.Working || !FoodSolution(uid, out var contents) || !Batch(ent, out var batch)) return false;
        if (contents.Comp.Solution.Volume > 0)
        {
            var amount = FixedPoint2.Min(contents.Comp.Solution.Volume, FixedPoint2.New(ent.Comp.Capacity) - TotalVolume(ent));
            if (amount <= 0) { if (user != null) Notify(ent, user.Value, "culinary-full"); return false; }
            Stamp(uid, contents);
            ent.Comp.Composition.AddRange(vessel.Composition.Count > 0 ? vessel.Composition : [MetaData(uid).EntityName]);
            ent.Comp.Flavors.UnionWith(vessel.Flavors);
            _solutions.AddSolution(batch, _solutions.SplitSolution(contents, amount));
            ent.Comp.Ready = ent.Comp.Ingredients.ContainedEntities.Count == 0 && Prepared(batch.Comp.Solution);
            if (contents.Comp.Solution.Volume <= 0) { vessel.Composition.Clear(); vessel.Flavors.Clear(); }
        }
        else
        {
            CommitIngredients(ent, batch);
            var amount = FixedPoint2.Min(batch.Comp.Solution.Volume, contents.Comp.Solution.AvailableVolume);
            if (amount <= 0) { if (user != null) Notify(ent, user.Value, "culinary-empty"); return false; }
            vessel.Composition = new(ent.Comp.Composition); vessel.Flavors = new(ent.Comp.Flavors);
            _solutions.AddSolution(contents, _solutions.SplitSolution(batch, amount));
            if (TryComp<FlavorProfileComponent>(uid, out var flavor)) flavor.Flavors.UnionWith(vessel.Flavors);
            if (batch.Comp.Solution.Volume <= 0)
            { ent.Comp.Ready = false; ent.Comp.Composition.Clear(); ent.Comp.Flavors.Clear(); }
        }
        ent.Comp.Progress = 0;
        ent.Comp.Status = ent.Comp.Ready ? "culinary-status-ready" : "culinary-status-idle";
        Publish(ent);
        return true;
    }
}
