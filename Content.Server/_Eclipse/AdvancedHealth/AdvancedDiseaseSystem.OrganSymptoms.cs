using System.Linq;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Medical;

namespace Content.Server._Eclipse.AdvancedHealth;

public sealed partial class AdvancedDiseaseSystem
{
    [Dependency] private readonly VomitSystem _vomit = default!;

    private ClinicalSymptoms AdvanceOrganSymptoms(Entity<AdvancedHealthComponent> body, float seconds)
    {
        var kidneys = _organs.Function(body, "Kidneys", excludeNutritionalDeficits: true);
        var liver = _organs.Function(body, "Liver", excludeNutritionalDeficits: true);
        var stomach = _organs.Function(body, "Stomach", excludeNutritionalDeficits: true);
        var intestines = _organs.Function(body, "Intestines", excludeNutritionalDeficits: true);
        // Renal failure accumulates waste; the liver's remaining function limits clearance.
        body.Comp.ToxinLoad = Math.Clamp(body.Comp.ToxinLoad
            + (Math.Max(0, 0.7f - kidneys) * 0.18f - liver * 0.025f) * seconds, 0, 100);
        // Prolonged failure to clear toxins damages vital organs instead of stopping at nausea.
        var toxicity = Math.Clamp((body.Comp.ToxinLoad - 75) / 25, 0, 1);
        if (toxicity > 0)
            foreach (var organ in _organs.Organs(body))
                if (Comp<Content.Shared.Body.OrganComponent>(organ).Category?.Id is "Heart" or "Brain")
                    _organs.Damage(organ, toxicity * seconds * 0.03f);
        var nausea = Math.Max(Math.Max(1 - stomach, (1 - intestines) * 0.8f),
            Math.Max(body.Comp.ToxinLoad / 100, Math.Max(0, 0.6f - liver)));
        var gastrointestinalBleeding = HasGastrointestinalBleeding(body);
        var gastricBlood = AdvanceGastricBlood(body, gastrointestinalBleeding, seconds);
        if (gastrointestinalBleeding) nausea = Math.Max(nausea, 0.8f);
        if (nausea < 0.25f && !HasComp<AdvancedDiseaseComponent>(body)) return ClinicalSymptoms.None;
        var state = EnsureComp<AdvancedDiseaseComponent>(body);
        state.VomitCooldown = Math.Max(0, state.VomitCooldown - seconds);
        state.VomitSymptomsRemaining = Math.Max(0, state.VomitSymptomsRemaining - seconds);
        state.NauseaExposure = nausea >= 0.5f ? Math.Min(25, state.NauseaExposure + nausea * seconds)
            : Math.Max(0, state.NauseaExposure - seconds * 2);
        var symptoms = nausea >= 0.25f ? ClinicalSymptoms.Nausea : ClinicalSymptoms.None;
        if (nausea >= 0.5f && state.NauseaExposure >= 25 && state.VomitCooldown <= 0 && !body.Comp.IsUnconscious)
        {
            state.NauseaExposure = 0;
            // Empty-stomach retching must not keep producing liquid or charging resource loss.
            var expelled = _vomit.Vomit(body.Owner, thirstAdded: -12, hungerAdded: -8);
            state.VomitCooldown = expelled ? 90 : 180;
            state.VomitSymptomsRemaining = expelled ? 10 : 0;
            state.VomitWasBloody = expelled && gastricBlood;
        }
        if (state.VomitSymptomsRemaining > 0)
        {
            symptoms |= ClinicalSymptoms.Vomiting;
            if (state.VomitWasBloody) symptoms |= ClinicalSymptoms.Hematemesis;
        }
        return symptoms;
    }

    private bool HasGastrointestinalBleeding(Entity<AdvancedHealthComponent> body)
        => body.Comp.HasBlood && body.Comp.BodyParts.TryGetValue(BodyPartSlot.Abdomen, out var abdomen)
            && abdomen.Wounds.Any(wound => wound.InternalBleedingRate > 0.01f)
            && (_organs.Function(body, "Stomach", excludeNutritionalDeficits: true) < 0.8f
                || _organs.Function(body, "Intestines", excludeNutritionalDeficits: true) < 0.8f);

    private bool AdvanceGastricBlood(Entity<AdvancedHealthComponent> body, bool bleeding, float seconds)
    {
        if (!TryComp<BloodstreamComponent>(body, out var stream)) return false;
        var reference = new Solution(stream.BloodReferenceSolution.Contents);
        if (reference.Volume <= 0) return false;
        // Internal wound loss is already charged by physiology in ml. Convert that actual loss
        // to reagent units and hold it in the stomach, rather than inventing blood on vomiting.
        var loss = bleeding ? Math.Min(body.Comp.BloodVolume,
            body.Comp.BodyParts[BodyPartSlot.Abdomen].Wounds.Sum(wound => Math.Max(0, wound.InternalBleedingRate)) * seconds) : 0;
        var volume = loss / Math.Max(1, body.Comp.MaxBloodVolume) * reference.Volume.Float();
        var bloody = false;
        foreach (var organ in _organs.Organs(body))
        {
            if (Comp<Content.Shared.Body.OrganComponent>(organ).Category?.Id != "Stomach"
                || !_solutions.TryGetSolution(organ.Owner, StomachSystem.DefaultSolutionName, out var entity, out var contents)) continue;
            var added = Math.Min(volume, (contents.MaxVolume - contents.Volume).Float());
            if (added > 0)
            {
                var blood = new Solution(reference.Contents);
                blood.ScaleSolution(added / blood.Volume.Float());
                _solutions.TryAddSolution(entity.Value, blood);
                volume -= added;
            }
            bloody |= contents.Contents.Any(reagent => reference.ContainsPrototype(reagent.Reagent.Prototype));
        }
        return bloody;
    }
}
