using System.Linq;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Containers;
using Content.Shared._Eclipse.MedicalChemistry;
using Content.Shared.Forensics.Components;

namespace Content.Server._Eclipse.AdvancedHealth;

public sealed partial class AdvancedDiseaseSystem
{
    [Dependency] private readonly AdvancedOrganHealthSystem _organs = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedInteractionSystem _clinicalInteraction = default!;

    public void Rejuvenate(Entity<AdvancedHealthComponent> ent)
    {
        if (TryComp<AdvancedDiseaseComponent>(ent, out var state))
        {
            state.Active.Clear(); state.Severity.Clear(); state.Locations.Clear();
            state.SourceControlled.Clear(); state.Known.Clear(); state.CompletedTests.Clear(); state.Symptoms = ClinicalSymptoms.None;
            state.NauseaExposure = state.VomitCooldown = state.VomitSymptomsRemaining = 0;
            state.VomitWasBloody = false;
            Dirty(ent.Owner, state);
            _movement.RefreshMovementSpeedModifiers(ent);
        }
        ResetEffects(ent.Comp);
        ent.Comp.BloodGlucose = 5f;
        Dirty(ent);
    }

    private bool CanHaveCondition(EntityUid body, AdvancedDiseasePrototype prototype)
        => TryComp<AdvancedHealthComponent>(body, out _) && !IsDead(body)
            && (prototype.RequiredOrgan == null || _organs.Organs(body)
                .Any(organ => Comp<OrganComponent>(organ).Category?.Id == prototype.RequiredOrgan));

    private bool IsDead(EntityUid body) => TryComp<MobStateComponent>(body, out var state) && state.CurrentState == MobState.Dead;

    /// <summary>Trusted game systems introduce a condition; this is not a client diagnosis API.</summary>
    public bool ApplyCondition(EntityUid body, ProtoId<AdvancedDiseasePrototype> id, float severity = 0.3f, BodyPartSlot? region = null)
    {
        if (!float.IsFinite(severity) || severity <= 0 || !_proto.TryIndex(id, out var prototype)
            || !CanHaveCondition(body, prototype)) return false;
        var state = EnsureComp<AdvancedDiseaseComponent>(body);
        if (state.Active.ContainsKey(id.Id)) return false;
        state.Active[id.Id] = 0;
        state.Severity[id.Id] = Math.Clamp(severity, 0.01f, 1f);
        state.Locations[id.Id] = region ?? prototype.Region;
        Dirty(body, state);
        _movement.RefreshMovementSpeedModifiers(body);
        return true;
    }

    public void Diagnose(EntityUid body, ClinicalTest test)
    {
        if (!HasComp<AdvancedHealthComponent>(body)) return;
        var state = EnsureComp<AdvancedDiseaseComponent>(body);
        state.CompletedTests.Add(test);
        foreach (var id in state.Active.Keys)
            if (_proto.TryIndex<AdvancedDiseasePrototype>(id, out var prototype) && prototype.Diagnostic == test)
                state.Known[id] = state.Severity.GetValueOrDefault(id, 0.3f);
        RefreshIndications(body);
        Dirty(body, state);
    }

    public void RefreshIndications(EntityUid body)
    {
        if (!TryComp<AdvancedHealthComponent>(body, out var health)) return;
        var wanted = new Dictionary<BodyPartSlot, HashSet<AdvancedTreatmentType>>();
        void Require(BodyPartSlot slot, AdvancedTreatmentType procedure)
        {
            if (!wanted.TryGetValue(slot, out var set)) wanted[slot] = set = new();
            set.Add(procedure);
        }
        if (TryComp<AdvancedDiseaseComponent>(body, out var state))
            foreach (var id in state.Known.Keys)
                if (!state.SourceControlled.Contains(id) && _proto.TryIndex<AdvancedDiseasePrototype>(id, out var prototype)
                    && prototype.Procedure is { } procedure
                    && health.BodyParts.TryGetValue(state.Locations.GetValueOrDefault(id, prototype.Region), out var part))
                    Require(part.Slot, procedure);
        foreach (var organ in _organs.Organs(body))
            if (organ.Comp.Integrity is > 0 and < 80 && organ.Comp.Region.IsCore()
                && organ.Comp.Region is not (BodyPartSlot.Head or BodyPartSlot.Neck)
                && health.BodyParts.TryGetValue(organ.Comp.Region, out var part) && part.IsSurgicalOpen
                && organ.Comp.ExaminedIntegrity != null)
                Require(part.Slot, AdvancedTreatmentType.OrganRepair);
        var changed = false;
        foreach (var part in health.BodyParts.Values)
        {
            var expected = wanted.GetValueOrDefault(part.Slot);
            if (expected == null && part.ClinicalProcedures.Count == 0
                || expected != null && part.ClinicalProcedures.SetEquals(expected)) continue;
            part.ClinicalProcedures.Clear();
            if (expected != null) part.ClinicalProcedures.UnionWith(expected);
            changed = true;
        }
        if (changed) Dirty(body, health);
    }

    public void CompleteProcedure(EntityUid body, BodyPartSlot region, AdvancedTreatmentType procedure)
    {
        if (procedure == AdvancedTreatmentType.OrganRepair)
        {
            foreach (var organ in _organs.Organs(body))
                if (organ.Comp.Region == region && organ.Comp.Integrity is > 0 and < 80)
                {
                    _organs.Repair(organ, 80 - organ.Comp.Integrity);
                    organ.Comp.SurgicallyStabilized = true;
                }
            var part = Comp<AdvancedHealthComponent>(body).BodyParts[region];
            part.OrganIntegrity = Math.Max(part.OrganIntegrity, part.MaxOrganIntegrity * 0.8f);
            part.PostoperativeRecovery = true;
        }
        if (TryComp<AdvancedDiseaseComponent>(body, out var state))
        {
            foreach (var id in state.Known.Keys.ToArray())
            {
                var prototype = _proto.Index<AdvancedDiseasePrototype>(id);
                if (prototype.Procedure != procedure || state.Locations.GetValueOrDefault(id, prototype.Region) != region) continue;
                state.SourceControlled.Add(id);
                state.Severity[id] = Math.Min(state.Severity.GetValueOrDefault(id, 0.3f), 0.2f);
                if (id == "ClinicalAppendicitis")
                {
                    var containers = EntityManager.System<SharedContainerSystem>();
                    if (containers.TryGetContainer(body, BodyComponent.ContainerID, out var container))
                        foreach (var organ in _organs.Organs(body).ToArray())
                            if (Comp<OrganComponent>(organ).Category?.Id == "Appendix") containers.Remove(organ.Owner, container);
                    var health = Comp<AdvancedHealthComponent>(body);
                    health.ExpectedInternalOrgans.Remove("Appendix");
                    _organs.RefreshBody((body, health));
                }
                if (procedure == AdvancedTreatmentType.ThoracicDrain && id != "ClinicalHemothorax") RemoveCondition(state, id);
            }
            Dirty(body, state);
        }
        RefreshIndications(body);
    }

    private static void RemoveCondition(AdvancedDiseaseComponent state, string id)
    {
        state.Active.Remove(id); state.Severity.Remove(id); state.Known.Remove(id);
        state.ClearProgress.Remove(id); state.Locations.Remove(id); state.SourceControlled.Remove(id);
    }

    private static void ResetEffects(AdvancedHealthComponent health)
    {
        health.ClinicalPain = health.ClinicalShock = health.ClinicalLungImpairment = health.ClinicalPressureLoss = 0;
        health.ClinicalFever = health.Analgesia = health.Sedation = health.RespiratoryDepression = health.ClinicalPulseDrive = 0;
    }

    private ClinicalMedicationEffects Medication(EntityUid body)
    {
        Solution? blood = null, metabolites = null;
        if (TryComp<BloodstreamComponent>(body, out var stream))
        {
            _solutions.TryGetSolution(body, stream.BloodSolutionName, out _, out blood);
            _solutions.TryGetSolution(body, stream.MetabolitesSolutionName, out _, out metabolites);
        }
        var dna = TryComp<DnaComponent>(body, out var identity) ? identity.DNA : null;
        return ClinicalMedicationMath.Calculate(_proto.EnumeratePrototypes<ClinicalMedicationPrototype>()
            .Select(profile => (profile, (blood?.GetTotalPrototypeQuantity(profile.Reagent).Float() ?? 0f)
                + (metabolites?.GetTotalPrototypeQuantity(profile.Reagent).Float() ?? 0f),
                (blood == null ? 0f : MedicalBatch.GetActivity(blood, profile.Reagent, dna))
                + (metabolites == null ? 0f : MedicalBatch.GetActivity(metabolites, profile.Reagent, dna)))));
    }

    /// <summary>Called once by the existing physiological tick, before its simulation gate.</summary>
    public void Advance(Entity<AdvancedHealthComponent> ent, float elapsed)
    {
        if (!float.IsFinite(elapsed) || elapsed <= 0 || IsDead(ent)) return;
        var health = ent.Comp;
        var previous = (health.ClinicalPain, health.ClinicalShock, health.ClinicalLungImpairment,
            health.ClinicalPressureLoss, health.ClinicalFever, health.Analgesia, health.Sedation,
            health.RespiratoryDepression, health.ClinicalPulseDrive, health.BloodGlucose, health.ToxinLoad);
        ResetEffects(health);
        var medication = Medication(ent);
        health.Analgesia = medication.Analgesia;
        health.Sedation = medication.Sedation;
        health.RespiratoryDepression = medication.RespiratoryDepression;
        health.ClinicalPulseDrive = medication.PulseDrive;
        var renalRisk = 1f + (1f - _organs.Function(ent, "Kidneys"));
        health.ToxinLoad = Math.Clamp(health.ToxinLoad + medication.Toxicity * renalRisk * elapsed, 0f, 100f);
        var glucoseRise = 0f;
        var organImpairment = new Dictionary<string, float>();
        var symptoms = AdvanceOrganSymptoms(ent, elapsed);
        if (medication.Toxicity > 0) symptoms |= ClinicalSymptoms.DrugReaction;
        if (medication.Sedation > 0.1f) symptoms |= ClinicalSymptoms.Sedation;

        if (TryComp<AdvancedDiseaseComponent>(ent, out var state))
        {
            var previousSymptoms = state.Symptoms;
            var previousCount = state.Active.Count;
            var complications = new List<(ProtoId<AdvancedDiseasePrototype>, BodyPartSlot)>();
            foreach (var id in state.Active.Keys.ToArray())
            {
                if (!_proto.TryIndex<AdvancedDiseasePrototype>(id, out var disease) || !CanHaveCondition(ent, disease))
                { RemoveCondition(state, id); continue; }
                state.Active[id] += elapsed;
                var severity = state.Severity.GetValueOrDefault(id, 0.3f);
                if (id == "ClinicalHemothorax" && health.BodyParts.TryGetValue(BodyPartSlot.Chest, out var chest)
                    && chest.Wounds.Any(wound => wound.InternalBleedingRate > 0.001f))
                {
                    state.SourceControlled.Remove(id);
                    severity = Math.Min(1f, severity + 0.002f * elapsed);
                }
                var activity = medication.Activity.GetValueOrDefault(disease.Treatment);
                var recovery = disease.NaturalRecovery * Math.Clamp(health.ImmuneDefense / 100f, 0f, 1f)
                    + activity / Math.Max(10f, disease.ClearTime);
                var growth = disease.Progression;
                if (disease.Cause is ClinicalCause.Viral or ClinicalCause.Bacterial or ClinicalCause.Fungal)
                    growth *= Math.Clamp(1.2f - health.ImmuneDefense / 100f, 0.2f, 1.2f);
                severity = Math.Clamp(severity + (growth - recovery) * elapsed, 0f, 1f);
                var uncontrolled = disease.RequiresSourceControl && !state.SourceControlled.Contains(id);
                if (uncontrolled) severity = Math.Max(0.25f, severity);
                if (disease.Chronic) severity = Math.Max(0.1f, severity);
                if (severity <= 0.001f) { RemoveCondition(state, id); continue; }
                state.Severity[id] = severity;
                if (state.Known.ContainsKey(id)) state.Known[id] = severity;
                health.ClinicalPain += disease.PainPerSecond * 30f * severity;
                health.ClinicalShock += disease.ShockPerSecond * 30f * severity;
                var immediateRelief = disease.Treatment is MedicationClass.Bronchodilator or MedicationClass.Adrenaline
                    ? 1f - activity * 0.8f : 1f;
                health.ClinicalLungImpairment = Math.Max(health.ClinicalLungImpairment, disease.LungImpairment * severity * immediateRelief);
                health.ClinicalPressureLoss = Math.Max(health.ClinicalPressureLoss, disease.PressureLoss * severity);
                health.ClinicalFever = Math.Max(health.ClinicalFever, disease.Fever * severity);
                glucoseRise += disease.GlucoseRise * severity;
                if (disease.RequiredOrgan is { } category && disease.OrganImpairment > 0)
                    organImpairment[category] = Math.Max(organImpairment.GetValueOrDefault(category), disease.OrganImpairment * severity);
                health.ImmuneDefense = Math.Max(0, health.ImmuneDefense - disease.ImmuneDrain * severity * elapsed);
                if (severity >= 0.85f && disease.Complication is { } complication)
                    complications.Add((complication, state.Locations.GetValueOrDefault(id, disease.Region)));
            }
            foreach (var (complication, region) in complications) ApplyCondition(ent, complication, 0.3f, region);
            if (health.ClinicalFever > 0.5f) symptoms |= ClinicalSymptoms.Fever;
            if (health.ClinicalLungImpairment > 0.1f || health.RespiratoryDepression > 0.3f) symptoms |= ClinicalSymptoms.Breathlessness;
            if (health.ClinicalPain > 5) symptoms |= ClinicalSymptoms.Pain;
            if (health.ClinicalPressureLoss > 0.2f) symptoms |= ClinicalSymptoms.CirculatoryFailure;
            state.Symptoms = symptoms;
            if (state.Known.Count > 0 || previousSymptoms != symptoms || state.Active.Count != previousCount) Dirty(ent.Owner, state);
            if (state.Active.Count != previousCount) _movement.RefreshMovementSpeedModifiers(ent);
        }
        health.ClinicalFever = Math.Max(0, health.ClinicalFever - medication.Antipyretic);
        foreach (var organ in _organs.Organs(ent))
        {
            var category = Comp<OrganComponent>(organ).Category?.Id;
            _organs.SetCondition(organ, "ClinicalFunctionalImpairment", category != null ? organImpairment.GetValueOrDefault(category) : 0);
            if (organ.Comp.SurgicallyStabilized && health.Perfusion >= 0.6f
                && health.BodyParts.TryGetValue(organ.Comp.Region, out var recoveryRegion)
                && !recoveryRegion.IsSurgicalOpen && !recoveryRegion.IsDestroyed
                && !recoveryRegion.Wounds.Any(wound => wound.InternalBleedingRate > 0.001f))
            {
                _organs.Repair(organ, elapsed * 0.05f);
                if (organ.Comp.Integrity >= 100) organ.Comp.SurgicallyStabilized = false;
            }
        }
        if (health.TransfusionReaction > 0)
        {
            var reaction = health.TransfusionReaction;
            health.ClinicalShock += reaction * 35;
            health.ClinicalPain += reaction * 20;
            health.ClinicalPressureLoss = Math.Max(health.ClinicalPressureLoss, reaction * 0.4f);
            health.ClinicalFever = Math.Max(health.ClinicalFever, reaction * 2);
            health.OxygenCarryingCapacity = Math.Max(0.1f, health.OxygenCarryingCapacity - reaction * elapsed * 0.001f);
            health.ToxinLoad = Math.Min(100, health.ToxinLoad + reaction * elapsed * 0.03f);
            health.TransfusionReaction = Math.Max(0, reaction - elapsed * 0.002f);
            Dirty(ent);
        }
        health.ClinicalPressureLoss = Math.Max(0, health.ClinicalPressureLoss
            - medication.Activity.GetValueOrDefault(MedicationClass.Adrenaline) * 0.25f);
        var glucoseRecovery = Math.Clamp(5f - health.BloodGlucose, -0.01f, 0.01f);
        health.BloodGlucose = Math.Clamp(health.BloodGlucose + (glucoseRise + medication.Glucose + glucoseRecovery) * elapsed, 0, 40);
        if (health.BloodGlucose < 3)
        {
            health.Sedation = Math.Max(health.Sedation, Math.Clamp((3f - health.BloodGlucose) / 3f, 0, 0.98f));
            ApplyCondition(ent, "ClinicalHypoglycemia", 0.3f);
        }
        else if (TryComp<AdvancedDiseaseComponent>(ent, out var glycemia) && glycemia.Active.ContainsKey("ClinicalHypoglycemia"))
        {
            RemoveCondition(glycemia, "ClinicalHypoglycemia");
            Dirty(ent.Owner, glycemia);
            _movement.RefreshMovementSpeedModifiers(ent);
        }
        if (health.BloodGlucose > 16) ApplyCondition(ent, "ClinicalKetoacidosis", 0.3f);

        foreach (var part in health.BodyParts.Values)
        {
            var dirty = part.Wounds.Any(wound => wound.IsDirty);
            // Reuse the station's sanitation reading and the existing wound infection pathway.
            // One spill does not immediately invalidate an operation: exposure accumulates over time.
            if (part.IsSurgicalOpen)
            {
                var grade = Comp<Content.Shared._Eclipse.Industrial.RoomSanitationComponent>(ent).Grade;
                part.OpenSurgicalExposure += elapsed * (grade >= 2 ? 0.05f : grade > 0 ? 0.15f : 1f);
                if (part.OpenSurgicalExposure >= 120)
                {
                    foreach (var incision in part.Wounds.Where(w => w.IsSurgicalIncision))
                    {
                        incision.IsDirty = true;
                        incision.InfectionRisk = Math.Max(incision.InfectionRisk, 0.1f);
                    }
                    part.OpenSurgicalExposure = 0;
                    Dirty(ent);
                }
            }
            if (part.PostoperativeRecovery && !part.IsSurgicalOpen && !part.IsDestroyed
                && health.Perfusion >= 0.6f && !part.Wounds.Any(wound => wound.InternalBleedingRate > 0.001f))
            {
                part.OrganIntegrity = Math.Min(part.MaxOrganIntegrity, part.OrganIntegrity + part.MaxOrganIntegrity * 0.0005f * elapsed);
                if (part.OrganIntegrity >= part.MaxOrganIntegrity) part.PostoperativeRecovery = false;
                Dirty(ent);
            }
            part.DirtyExposure = dirty ? part.DirtyExposure + elapsed : 0;
            if (dirty && part.DirtyExposure > 120 && _random.Prob(Math.Min(1f, elapsed * 0.0003f)))
                ApplyCondition(ent, "WoundFever", 0.2f, part.Slot);
            foreach (var wound in part.Wounds)
                if (wound.ExternalBleedingRate > 0f)
                    wound.ExternalBleedingRate = Math.Min(100f, wound.ExternalBleedingRate * (1f + medication.BleedingRisk * elapsed));
        }
        if (_random.Prob(Math.Min(1f, elapsed * 0.15f))) Spread(ent);
        RefreshIndications(ent);
        var current = (health.ClinicalPain, health.ClinicalShock, health.ClinicalLungImpairment,
            health.ClinicalPressureLoss, health.ClinicalFever, health.Analgesia, health.Sedation,
            health.RespiratoryDepression, health.ClinicalPulseDrive, health.BloodGlucose, health.ToxinLoad);
        if (current != previous) Dirty(ent);
    }

    private void Spread(EntityUid body)
    {
        if (!TryComp<AdvancedDiseaseComponent>(body, out var state)) return;
        var transmissible = state.Active.Keys.Where(id => _proto.Index<AdvancedDiseasePrototype>(id).Airborne).ToArray();
        if (transmissible.Length == 0) return;
        var nearby = new HashSet<Entity<AdvancedHealthComponent>>();
        _lookup.GetEntitiesInRange(Transform(body).Coordinates, SpreadRange, nearby);
        foreach (var target in nearby)
            if (target.Owner != body && !IsDead(target) && _clinicalInteraction.InRangeUnobstructed(body, target.Owner, range: SpreadRange))
                foreach (var id in transmissible) TryInfect(target, id, true);
    }
}
