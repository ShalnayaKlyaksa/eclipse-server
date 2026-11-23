using System.Linq;
using System.Reflection;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Body.Systems;
using Content.Shared.Body;
using Content.Shared.Chemistry.Components;
using Content.Shared.Rejuvenate;
using Content.Shared.Chemistry.Reagent;
using Content.Shared._Eclipse.MedicalChemistry;
using Content.Shared.Forensics.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class ClinicalMedicineTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };
    private void Dose(EntityUid patient, string reagent, int amount = 5)
        => Assert.That(SEntMan.System<SharedBloodstreamSystem>().TryAddToBloodstream(patient, new Solution(reagent, amount)), Is.True);

    [Test]
    public async Task RealPatientProfileAndBatchPurityAffectClinicalDrugActivity()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            float Response(int purity, bool matched)
            {
                var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
                var dna = SEntMan.GetComponent<DnaComponent>(patient).DNA;
                var drug = new ReagentId("ClinicalInsulin", [new MedicalBatchData { Purity = purity, PatientDna = matched ? dna : "another-patient" }]);
                Assert.That(SEntMan.System<SharedBloodstreamSystem>().TryAddToBloodstream(patient, new Solution([new(drug, 1)])), Is.True);
                var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
                SEntMan.System<AdvancedDiseaseSystem>().Advance((patient, health), 10);
                return 5 - health.BloodGlucose;
            }
            var matched = Response(100, true);
            var mismatched = Response(100, false);
            var impure = Response(50, true);
            Assert.That(matched, Is.GreaterThan(mismatched));
            Assert.That(impure, Is.EqualTo(matched * 0.5f).Within(0.001));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task AppendectomyRemovesTheRealOrganAndPreventsTheSameConditionRecurring()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var system = SEntMan.System<AdvancedDiseaseSystem>();
            var organs = SEntMan.System<AdvancedOrganHealthSystem>();
            Assert.That(system.ApplyCondition(patient, "ClinicalAppendicitis"), Is.True);
            system.Diagnose(patient, ClinicalTest.Imaging);
            system.CompleteProcedure(patient, BodyPartSlot.Abdomen, AdvancedTreatmentType.SourceControl);
            Assert.That(organs.Organs(patient).Any(organ => SEntMan.GetComponent<OrganComponent>(organ).Category?.Id == "Appendix"), Is.False);
            Dose(patient, "ClinicalAntibiotic");
            system.Advance((patient, SEntMan.GetComponent<AdvancedHealthComponent>(patient)), 120);
            Assert.That(SEntMan.GetComponent<AdvancedDiseaseComponent>(patient).Active.ContainsKey("ClinicalAppendicitis"), Is.False);
            Assert.That(system.ApplyCondition(patient, "ClinicalAppendicitis"), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task OrganRepairNeedsClosedAccessForRecoveryAndBrainRepairAffectsTheActualBrain()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var system = SEntMan.System<AdvancedDiseaseSystem>();
            var organs = SEntMan.System<AdvancedOrganHealthSystem>();
            var heart = organs.Organs(patient).Single(organ => SEntMan.GetComponent<OrganComponent>(organ).Category?.Id == "Heart");
            var brain = organs.Organs(patient).Single(organ => SEntMan.GetComponent<OrganComponent>(organ).Category?.Id == "Brain");
            organs.Damage(heart, 50);
            var chest = health.BodyParts[BodyPartSlot.Chest];
            chest.IsSurgicalOpen = true;
            system.CompleteProcedure(patient, BodyPartSlot.Chest, AdvancedTreatmentType.OrganRepair);
            Assert.That(heart.Comp.Integrity, Is.EqualTo(80));
            system.Advance((patient, health), 100);
            Assert.That(heart.Comp.Integrity, Is.EqualTo(80));
            chest.IsSurgicalOpen = false;
            health.Perfusion = 1;
            system.Advance((patient, health), 100);
            Assert.That(heart.Comp.Integrity, Is.GreaterThan(80));
            organs.Damage(brain, 40);
            var repair = new AdvancedBrainRepairEvent(5);
            SEntMan.EventBus.RaiseLocalEvent(patient, ref repair);
            Assert.That(brain.Comp.Integrity, Is.EqualTo(65));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task DiagnosesRequireTheMatchingTestAndHiddenStateIsNotReplicated()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var system = SEntMan.System<AdvancedDiseaseSystem>();
            Assert.That(system.ApplyCondition(patient, "ClinicalBacterialPneumonia"), Is.True);
            var state = SEntMan.GetComponent<AdvancedDiseaseComponent>(patient);
            Assert.That(state.Known, Is.Empty);
            system.Diagnose(patient, ClinicalTest.Blood);
            Assert.That(state.Known, Is.Empty);
            system.Diagnose(patient, ClinicalTest.Microbiology);
            Assert.That(state.Known.ContainsKey("ClinicalBacterialPneumonia"), Is.True);
            var ev = new ComponentGetState(null, GameTick.Zero);
            SEntMan.EventBus.RaiseComponentEvent(patient, state, ref ev);
            var fields = ev.State!.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(fields.Any(field => field.Name is "Active" or "Severity" or "Locations" or "SourceControlled"), Is.False);
            Assert.That(system.ApplyCondition(patient, "ClinicalPneumothorax", float.NaN), Is.False);
            Assert.That(system.TryInfect(patient, "ClinicalHemothorax", airborne: true), Is.False);
            var slime = SEntMan.SpawnEntity("MobSlimePerson", map.MapCoords);
            Assert.That(system.ApplyCondition(slime, "ClinicalPyelonephritis"), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task ActualBloodstreamAntibioticTreatsBacteriaButNotViralInfection()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var system = SEntMan.System<AdvancedDiseaseSystem>();
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            system.ApplyCondition(patient, "ClinicalBacterialPneumonia", 0.3f);
            system.ApplyCondition(patient, "ClinicalViralBronchitis", 0.6f);
            Dose(patient, "ClinicalAntibiotic");
            system.Advance((patient, health), 40);
            var state = SEntMan.GetComponent<AdvancedDiseaseComponent>(patient);
            Assert.That(state.Active.ContainsKey("ClinicalBacterialPneumonia"), Is.False);
            Assert.That(state.Active.ContainsKey("ClinicalViralBronchitis"), Is.True);
            Assert.That(state.Severity["ClinicalViralBronchitis"], Is.GreaterThan(0.4f));
            Assert.That(health.ToxinLoad, Is.Zero, "A normal exposure must not cause overdose toxicity.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task AbscessNeedsBothSourceControlAndCauseSpecificMedication()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var system = SEntMan.System<AdvancedDiseaseSystem>();
            system.ApplyCondition(patient, "ClinicalAbscess", 0.6f, BodyPartSlot.LeftForearm);
            var part = health.BodyParts[BodyPartSlot.LeftForearm];
            Assert.That(part.ClinicalProcedures, Is.Empty);
            Dose(patient, "ClinicalAntibiotic");
            system.Advance((patient, health), 120);
            var state = SEntMan.GetComponent<AdvancedDiseaseComponent>(patient);
            Assert.That(state.Active.ContainsKey("ClinicalAbscess"), Is.True);
            Assert.That(state.Severity["ClinicalAbscess"], Is.EqualTo(0.25f));
            system.Diagnose(patient, ClinicalTest.Examination);
            Assert.That(part.ClinicalProcedures.Contains(AdvancedTreatmentType.SourceControl), Is.True);
            Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.SourceControl), Is.False);
            part.IsSurgicalOpen = true; part.LocalAnesthesia = 100;
            Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.SourceControl), Is.True);
            system.CompleteProcedure(patient, part.Slot, AdvancedTreatmentType.SourceControl);
            system.Advance((patient, health), 45);
            Assert.That(state.Active.ContainsKey("ClinicalAbscess"), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task PleuralDrainDoesNotStopTheInternalSourceOfBloodLoss()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var chest = health.BodyParts[BodyPartSlot.Chest];
            chest.Wounds.Add(new Wound { Type = WoundType.Gunshot, InternalBleedingRate = 2 });
            var system = SEntMan.System<AdvancedDiseaseSystem>();
            system.ApplyCondition(patient, "ClinicalHemothorax", 0.7f);
            system.Diagnose(patient, ClinicalTest.Imaging);
            system.CompleteProcedure(patient, BodyPartSlot.Chest, AdvancedTreatmentType.ThoracicDrain);
            Assert.That(chest.Wounds.Single().InternalBleedingRate, Is.EqualTo(2));
            system.Advance((patient, health), 1);
            Assert.That(chest.ClinicalProcedures.Contains(AdvancedTreatmentType.ThoracicDrain), Is.True);
            chest.Wounds.Single().InternalBleedingRate = 0;
            system.Advance((patient, health), 120);
            Assert.That(SEntMan.GetComponent<AdvancedDiseaseComponent>(patient).Active.ContainsKey("ClinicalHemothorax"), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task InsulinCanCauseHypoglycemiaAndGlucoseCorrectionDoesNotHealWounds()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var system = SEntMan.System<AdvancedDiseaseSystem>();
            Dose(patient, "ClinicalInsulin");
            system.Advance((patient, health), 40);
            Assert.That(health.BloodGlucose, Is.LessThan(3));
            Assert.That(health.Sedation, Is.GreaterThan(0));
            SEntMan.System<SharedBloodstreamSystem>().FlushChemicals(patient, 100);
            health.BodyParts[BodyPartSlot.LeftForearm].SkinIntegrity = 50;
            Dose(patient, "ClinicalGlucose");
            system.Advance((patient, health), 20);
            Assert.That(health.BloodGlucose, Is.GreaterThan(3));
            Assert.That(health.BodyParts[BodyPartSlot.LeftForearm].SkinIntegrity, Is.EqualTo(50));
            var rejuvenate = new RejuvenateEvent();
            SEntMan.EventBus.RaiseLocalEvent(patient, rejuvenate);
            Assert.That(health.BloodGlucose, Is.EqualTo(5));
            Assert.That(SEntMan.GetComponent<AdvancedDiseaseComponent>(patient).Known, Is.Empty);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
