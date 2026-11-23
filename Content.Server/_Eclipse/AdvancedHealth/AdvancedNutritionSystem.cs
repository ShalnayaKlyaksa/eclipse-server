using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Body;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Rejuvenate;
using Content.Shared.Standing;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;

namespace Content.Server._Eclipse.AdvancedHealth;

/// <summary>Called by the existing physiology tick; there is no second world-wide update loop.</summary>
public sealed class AdvancedNutritionSystem : EntitySystem
{
    [Dependency] private readonly HungerSystem _hunger = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly AdvancedOrganHealthSystem _organs = default!;

    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly SharedAdvancedExertionSystem _exertion = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AdvancedNutritionComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<AdvancedNutritionComponent, StandAttemptEvent>((Entity<AdvancedNutritionComponent> ent, ref StandAttemptEvent args) =>
        {
            if (ent.Comp.RequiresCrawling) args.Cancel();
        });
    }

    private static float Deficit(float current, float threshold)
        => Math.Clamp(1 - current / Math.Max(1, threshold), 0, 1);

    /// <summary>Restored circulating fluid relieves pressure before nutritional organ strain fully recovers.</summary>
    public float GetCirculatoryDehydration(EntityUid body, AdvancedNutritionComponent? nutrition)
        => nutrition != null && TryComp<ThirstComponent>(body, out var thirst) && thirst.CurrentThirst >= 0
            ? Math.Min(nutrition.DehydrationStrain, Deficit(thirst.CurrentThirst, thirst.ThirstThresholds[ThirstThreshold.Parched])) : 0;

    public void Advance(Entity<AdvancedHealthComponent> body, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0
            || TryComp<MobStateComponent>(body, out var state) && state.CurrentState == MobState.Dead) return;
        var volume = 0f;
        var capacity = 0f;
        foreach (var organ in _organs.Organs(body))
        {
            if (Comp<OrganComponent>(organ).Category?.Id != "Stomach"
                || !_solutions.TryGetSolution(organ.Owner, StomachSystem.DefaultSolutionName, out _, out var contents)) continue;
            volume += contents.Volume.Float();
            capacity += contents.MaxVolume.Float();
        }
        if (body.Comp.StomachVolume != volume || body.Comp.StomachCapacity != capacity)
        {
            body.Comp.StomachVolume = volume;
            body.Comp.StomachCapacity = capacity;
            Dirty(body);
        }
        var hungry = TryComp<HungerComponent>(body, out var hunger)
            && _hunger.GetHunger(hunger) <= hunger.Thresholds[HungerThreshold.Starving];
        var thirsty = TryComp<ThirstComponent>(body, out var thirst)
            && thirst.CurrentThirst >= 0 && thirst.CurrentThirst <= thirst.ThirstThresholds[ThirstThreshold.Parched];
        if (!hungry && !thirsty && !HasComp<AdvancedNutritionComponent>(body)) return;
        var nutrition = EnsureComp<AdvancedNutritionComponent>(body);
        var foodDeficit = hungry && hunger != null ? Deficit(_hunger.GetHunger(hunger), hunger.Thresholds[HungerThreshold.Starving]) : 0;
        var waterDeficit = thirsty && thirst != null ? Deficit(thirst.CurrentThirst, thirst.ThirstThresholds[ThirstThreshold.Parched]) : 0;
        var old = (nutrition.Malnutrition, nutrition.DehydrationStrain);
        var starvationBefore = nutrition.StarvationExposure;
        var dehydrationBefore = nutrition.DehydrationExposure;
        nutrition.StarvationExposure = hungry ? starvationBefore + seconds : Math.Max(0, starvationBefore - seconds * 2);
        nutrition.DehydrationExposure = thirsty ? dehydrationBefore + seconds : Math.Max(0, dehydrationBefore - seconds * 2);
        // Bill only the part of this interval beyond the grace period, including large test/admin steps.
        var starvationActive = Math.Max(0, nutrition.StarvationExposure - nutrition.StarvationGrace)
            - Math.Max(0, starvationBefore - nutrition.StarvationGrace);
        var dehydrationActive = Math.Max(0, nutrition.DehydrationExposure - nutrition.DehydrationGrace)
            - Math.Max(0, dehydrationBefore - nutrition.DehydrationGrace);
        // Ordinary hunger/thirst approaches a bounded deficit instead of becoming terminal on a timer.
        // Actual depletion can still reach full strain; replenishment restores function gradually.
        nutrition.Malnutrition = nutrition.Malnutrition < foodDeficit
            ? Math.Min(foodDeficit, nutrition.Malnutrition + Math.Max(0, starvationActive) * foodDeficit / Math.Max(1, nutrition.StarvationProgressTime))
            : Math.Max(foodDeficit, nutrition.Malnutrition - seconds / Math.Max(1, nutrition.NutritionRecoveryTime));
        nutrition.DehydrationStrain = nutrition.DehydrationStrain < waterDeficit
            ? Math.Min(waterDeficit, nutrition.DehydrationStrain + Math.Max(0, dehydrationActive) * waterDeficit / Math.Max(1, nutrition.DehydrationProgressTime))
            : Math.Max(waterDeficit, nutrition.DehydrationStrain - seconds / Math.Max(1, nutrition.HydrationRecoveryTime));
        var emptyFood = hungry && hunger != null && _hunger.GetHunger(hunger) <= 0;
        var emptyWater = thirsty && thirst != null && thirst.CurrentThirst <= 0;
        var changed = old != (nutrition.Malnutrition, nutrition.DehydrationStrain)
            || nutrition.FoodDepleted != emptyFood || nutrition.WaterDepleted != emptyWater;
        nutrition.FoodDepleted = emptyFood;
        nutrition.WaterDepleted = emptyWater;
        var weakness = 1 - (1 - nutrition.Malnutrition * 0.55f) * (1 - nutrition.DehydrationStrain * 0.4f);
        if (Math.Abs(body.Comp.SystemicWeakness - weakness) > 0.001f)
        {
            body.Comp.SystemicWeakness = weakness;
            Dirty(body);
        }
        if (nutrition.RequiresCrawling) _standing.Down(body.Owner, playSound: false, dropHeldItems: false);
        if (TryComp<AdvancedExertionComponent>(body, out var exertion)) _exertion.RefreshGait((body.Owner, exertion));
        if (!changed && !(hungry && nutrition.Malnutrition >= 0.7f || thirsty && nutrition.DehydrationStrain >= 0.85f)) return;

        foreach (var organ in _organs.Organs(body))
        {
            var category = Comp<OrganComponent>(organ).Category?.Id;
            if (category == "Kidneys")
            {
                _organs.SetCondition(organ, "AdvancedRenalDehydration", nutrition.DehydrationStrain);
                if (waterDeficit >= 0.85f && nutrition.DehydrationStrain >= 0.85f)
                    _organs.Damage(organ, Math.Max(0, dehydrationActive
                        - Math.Max(0, 0.85f - old.DehydrationStrain) * nutrition.DehydrationProgressTime / waterDeficit) * 0.02f);
            }
            if (category is "Liver" or "Stomach" or "Intestines")
                _organs.SetCondition(organ, "AdvancedOrganMalnutrition", nutrition.Malnutrition);
            // Once reserves have been exhausted for a long time, vital muscle and liver also fail.
            // Feeding stops this damage; it does not instantly regenerate the lost tissue.
            if (foodDeficit >= 0.95f && nutrition.Malnutrition >= 0.95f && category is "Heart" or "Liver")
                _organs.Damage(organ, Math.Max(0, starvationActive
                    - Math.Max(0, 0.95f - old.Malnutrition) * nutrition.StarvationProgressTime / foodDeficit) * 0.03f);
        }
        if (foodDeficit >= 0.7f && nutrition.Malnutrition >= 0.7f)
        {
            foreach (var part in body.Comp.BodyParts.Values)
                part.MuscleIntegrity = Math.Max(0, part.MuscleIntegrity - Math.Max(0, starvationActive
                    - Math.Max(0, 0.7f - old.Malnutrition) * nutrition.StarvationProgressTime / foodDeficit) * 0.005f);
            Dirty(body);
        }
        if (changed) Dirty(body.Owner, nutrition);
        if (changed || hungry && nutrition.Malnutrition >= 0.7f) _movement.RefreshMovementSpeedModifiers(body.Owner);
    }

    private void OnRejuvenate(Entity<AdvancedNutritionComponent> ent, ref RejuvenateEvent args)
    {
        var nutrition = ent.Comp;
        if (TryComp<AdvancedHealthComponent>(ent, out var health))
        {
            health.SystemicWeakness = 0;
            Dirty(ent.Owner, health);
        }
        nutrition.FoodDepleted = nutrition.WaterDepleted = false;
        nutrition.Malnutrition = nutrition.DehydrationStrain = nutrition.StarvationExposure = nutrition.DehydrationExposure = 0;
        Dirty(ent.Owner, nutrition);
        _movement.RefreshMovementSpeedModifiers(ent.Owner);
    }
}
