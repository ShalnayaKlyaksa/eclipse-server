using Content.Shared.Atmos;
using Content.Shared.EntityEffects;
using Content.Shared.Random;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;
using Content.Server.Eclipse.Botany;

namespace Content.Server.Botany;

public sealed class MutationSystem : EntitySystem
{
    private static ProtoId<RandomPlantMutationListPrototype> RandomPlantMutations = "RandomPlantMutations";

    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedEntityEffectsSystem _entityEffects = default!;
    private RandomPlantMutationListPrototype _randomMutations = default!;

    public override void Initialize()
    {
        _randomMutations = _prototypeManager.Index(RandomPlantMutations);
    }

    /// <summary>
    /// For each random mutation, see if it occurs on this plant this check.
    /// </summary>
    /// <param name="seed"></param>
    /// <param name="severity"></param>
    public void CheckRandomMutations(EntityUid plantHolder, ref SeedData seed, float severity)
    {
        foreach (var mutation in _randomMutations.mutations)
        {
            if (Random(Math.Min(mutation.BaseOdds * severity, 1.0f)))
            {
                if (mutation.AppliesToPlant)
                    _entityEffects.TryApplyEffect(plantHolder, mutation.Effect);

                // Stat adjustments do not persist by being an attached effect, they just change the stat.
                if (mutation.Persists && !seed.Mutations.Any(m => m.Name == mutation.Name))
                    seed.Mutations.Add(mutation);
            }
        }
    }

    /// <summary>
    /// Checks all defined mutations against a seed to see which of them are applied.
    /// </summary>
    public void MutateSeed(EntityUid plantHolder, ref SeedData seed, float severity)
    {
        if (!seed.Unique)
        {
            Log.Error($"Attempted to mutate a shared seed");
            return;
        }

        CheckRandomMutations(plantHolder, ref seed, severity);
    }

    public SeedData Cross(SeedData a, SeedData b)
    {
        SeedData result = b.Clone();

        CrossChemicals(ref result.Chemicals, a.Chemicals);

        CrossFloat(ref result.NutrientConsumption, a.NutrientConsumption);
        CrossFloat(ref result.WaterConsumption, a.WaterConsumption);
        CrossFloat(ref result.IdealHeat, a.IdealHeat);
        CrossFloat(ref result.HeatTolerance, a.HeatTolerance);
        CrossFloat(ref result.IdealLight, a.IdealLight);
        CrossFloat(ref result.LightTolerance, a.LightTolerance);
        CrossFloat(ref result.ToxinsTolerance, a.ToxinsTolerance);
        CrossFloat(ref result.LowPressureTolerance, a.LowPressureTolerance);
        CrossFloat(ref result.HighPressureTolerance, a.HighPressureTolerance);
        CrossFloat(ref result.PestTolerance, a.PestTolerance);
        CrossFloat(ref result.WeedTolerance, a.WeedTolerance);

        CrossFloat(ref result.Endurance, a.Endurance);
        CrossInt(ref result.Yield, a.Yield);
        CrossFloat(ref result.Lifespan, a.Lifespan);
        CrossFloat(ref result.Maturation, a.Maturation);
        CrossFloat(ref result.Production, a.Production);
        CrossFloat(ref result.Potency, a.Potency);

        CrossBool(ref result.Seedless, a.Seedless);
        CrossBool(ref result.Ligneous, a.Ligneous);
        CrossBool(ref result.TurnIntoKudzu, a.TurnIntoKudzu);
        CrossBool(ref result.CanScream, a.CanScream);

        CrossGasses(ref result.ExudeGasses, a.ExudeGasses);
        CrossGasses(ref result.ConsumeGasses, a.ConsumeGasses);

        // LINQ Explanation
        // For the list of mutation effects on both plants, use a 50% chance to pick each one.
        // Union all of the chosen mutations into one list, and pick ones with a Distinct (unique) name.
        result.Mutations = result.Mutations.Where(m => Random(0.5f)).Union(a.Mutations.Where(m => Random(0.5f))).DistinctBy(m => m.Name).ToList();

        // Deliberate species discoveries are obtainable without mutagen.
        foreach (var hybrid in _prototypeManager.EnumeratePrototypes<PlantHybridPrototype>())
        {
            var first = _prototypeManager.Index(hybrid.ParentA).PacketPrototype;
            var second = _prototypeManager.Index(hybrid.ParentB).PacketPrototype;
            if (!((a.PacketPrototype == first && b.PacketPrototype == second)
                  || (a.PacketPrototype == second && b.PacketPrototype == first)))
                continue;
            if (Random(hybrid.Chance))
            {
                var species = _prototypeManager.Index(hybrid.Result);
                result = result.SpeciesChange(species);
                result.HarvestRepeat = species.HarvestRepeat;
                result.Yield = Math.Clamp(result.Yield, 1, Math.Max(1, species.Yield));
                result.Seedless = false;
                result.Viable = true;
                return result;
            }
        }

        // One small variation per generation. Larger harvests and concentration have a resource cost.
        switch (_robustRandom.Next(6))
        {
            case 0:
                result.Potency = Math.Max(result.Potency, Math.Min(result.Potency + 3, 80));
                result.NutrientConsumption = Math.Min(result.NutrientConsumption + 0.03f, 2);
                break;
            case 1:
                result.Endurance = Math.Max(result.Endurance, Math.Min(result.Endurance + 5, 150));
                break;
            case 2:
                result.WaterConsumption = Math.Min(result.WaterConsumption, Math.Max(result.WaterConsumption - 0.03f, 0.15f));
                break;
            case 3:
                result.Production = Math.Min(result.Production, Math.Max(result.Production - 0.25f, 3));
                break;
            case 4:
                result.Lifespan = Math.Max(result.Lifespan, Math.Min(result.Lifespan + 2, 80));
                break;
            case 5:
                if (Random(0.25f) && result.Yield < 6)
                {
                    result.Yield++;
                    result.NutrientConsumption = Math.Min(result.NutrientConsumption + 0.1f, 2);
                }
                break;
        }

        // Crossing does not arbitrarily destroy an otherwise fertile line.
        result.Seedless = a.Seedless && b.Seedless;
        return result;
    }

    private void CrossChemicals(ref Dictionary<string, SeedChemQuantity> val, Dictionary<string, SeedChemQuantity> other)
    {
        // Go through chemicals from the pollen in swab
        foreach (var otherChem in other)
        {
            // if both have same chemical, randomly pick potency ratio from the two.
            if (val.ContainsKey(otherChem.Key))
            {
                val[otherChem.Key] = Random(0.5f) ? otherChem.Value : val[otherChem.Key];
            }
            // if target plant doesn't have this chemical, has 50% chance to add it.
            else
            {
                if (Random(0.5f))
                {
                    var fixedChem = otherChem.Value;
                    fixedChem.Inherent = false;
                    val.Add(otherChem.Key, fixedChem);
                }
            }
        }

        // if the target plant has chemical that the pollen in swab does not, 50% chance to remove it.
        foreach (var thisChem in val.ToArray())
        {
            if (!other.ContainsKey(thisChem.Key))
            {
                if (Random(0.5f))
                {
                    if (val.Count > 1)
                    {
                        val.Remove(thisChem.Key);
                    }
                }
            }
        }
    }

    private void CrossGasses(ref Dictionary<Gas, float> val, Dictionary<Gas, float> other)
    {
        // Go through gasses from the pollen in swab
        foreach (var otherGas in other)
        {
            // if both have same gas, randomly pick ammount from the two.
            if (val.ContainsKey(otherGas.Key))
            {
                val[otherGas.Key] = Random(0.5f) ? otherGas.Value : val[otherGas.Key];
            }
            // if target plant doesn't have this gas, has 50% chance to add it.
            else
            {
                if (Random(0.5f))
                {
                    val.Add(otherGas.Key, otherGas.Value);
                }
            }
        }
        // if the target plant has gas that the pollen in swab does not, 50% chance to remove it.
        foreach (var thisGas in val.ToArray())
        {
            if (!other.ContainsKey(thisGas.Key))
            {
                if (Random(0.5f))
                {
                    val.Remove(thisGas.Key);
                }
            }
        }
    }
    private void CrossFloat(ref float val, float other)
    {
        val += (other - val) * 0.25f;
    }

    private void CrossInt(ref int val, int other)
    {
        var change = (other - val) * 0.25f;
        val += (int)Math.Truncate(change);
        if (Random(Math.Abs(change % 1)))
            val += Math.Sign(change);
    }

    private void CrossBool(ref bool val, bool other)
    {
        val = Random(0.5f) ? val : other;
    }

    private bool Random(float p)
    {
        return _robustRandom.Prob(p);
    }
}
