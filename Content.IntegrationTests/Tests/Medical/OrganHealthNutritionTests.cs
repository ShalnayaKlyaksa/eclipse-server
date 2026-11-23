using System.Linq;
using System.Reflection;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Body;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids.Components;
using Content.Shared.Medical;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class OrganHealthNutritionTests : GameTest
{
    [TestPrototypes]
    private const string ConditionPrototype = @"
- type: medicalCondition
  id: TestOrganSupport
  name: advanced-organ-working
  description: advanced-organ-examined
  beneficial: true
  functionMultiplier: 1.2
";
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    private Entity<AdvancedOrganHealthComponent> Organ(EntityUid body, string category)
        => SEntMan.System<AdvancedOrganHealthSystem>().Organs(body)
            .Single(organ => SEntMan.GetComponent<OrganComponent>(organ).Category?.Id == category);

    private Solution FillStomach(EntityUid body, int amount = 10)
    {
        var solutions = SEntMan.System<SharedSolutionContainerSystem>();
        Assert.That(solutions.TryGetSolution(Organ(body, "Stomach").Owner, StomachSystem.DefaultSolutionName, out var entity, out var solution), Is.True);
        Assert.That(solutions.TryAddSolution(entity!.Value, new Solution("Water", amount)), Is.True);
        return solution!;
    }

    private float PuddleVolume()
    {
        var total = 0f;
        var query = SEntMan.EntityQueryEnumerator<PuddleComponent>();
        while (query.MoveNext(out var uid, out _))
            if (SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(uid, "puddle", out _, out var solution))
                total += (float) solution.Volume;
        return total;
    }

    [Test]
    public async Task OrgansHaveIndependentDamageAndHiddenConditionsAreNotNetworked()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var organs = SEntMan.System<AdvancedOrganHealthSystem>();
            var heart = Organ(patient, "Heart");
            var lungs = Organ(patient, "Lungs");
            organs.Damage(heart, 30);
            Assert.That(heart.Comp.Function, Is.EqualTo(0.7f).Within(0.001));
            Assert.That(lungs.Comp.Function, Is.EqualTo(1));
            Assert.That(heart.Comp.KnownConditions, Is.Empty);
            Assert.That(heart.Comp.ExaminedIntegrity, Is.Null);
            var get = new ComponentGetState(null, GameTick.Zero);
            SEntMan.EventBus.RaiseComponentEvent(heart.Owner, heart.Comp, ref get);
            var fields = get.State!.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fields.Any(field => field.Name == "Conditions" || field.Name == "Integrity" || field.Name == "Discovered"), Is.False,
                "Hidden state must never be included in the network snapshot.");
            organs.Examine(heart, MedicalDetection.Examination);
            Assert.That(heart.Comp.ExaminedIntegrity, Is.EqualTo(70));
            Assert.That(heart.Comp.KnownConditions, Is.Empty, "A basic scan cannot reveal surgical diagnoses.");
            organs.Examine(heart, MedicalDetection.Surgery);
            Assert.That(heart.Comp.KnownConditions.Single().Prototype.Id, Is.EqualTo("AdvancedOrganTrauma"));
            organs.SetCondition(heart, "TestOrganSupport", 1);
            Assert.That(heart.Comp.KnownConditions.Any(condition => condition.Prototype.Id == "TestOrganSupport"), Is.True);
            Assert.That(heart.Comp.Function, Is.EqualTo(0.84f).Within(0.001));
            organs.Repair(heart, 30);
            Assert.That(heart.Comp.Function, Is.EqualTo(1));
            Assert.That(heart.Comp.Conditions.Any(condition => condition.Prototype.Id == "AdvancedOrganTrauma"), Is.False);
        });
    }

    [Test]
    public async Task TransplantPreservesOrganStateAndRemovingTheHeartRemovesItsFunction()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var donor = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var recipient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var system = SEntMan.System<AdvancedOrganHealthSystem>();
            var containers = SEntMan.System<SharedContainerSystem>();
            var heart = Organ(donor, "Heart");
            system.Damage(heart, 40);
            system.Examine(heart, MedicalDetection.Surgery);
            containers.TryGetContainer(donor, BodyComponent.ContainerID, out var donorBody);
            containers.TryGetContainer(recipient, BodyComponent.ContainerID, out var recipientBody);
            Assert.That(containers.Remove(heart.Owner, donorBody!), Is.True);
            Assert.That(system.Function(donor, "Heart"), Is.Zero);
            Assert.That(SEntMan.GetComponent<AdvancedHealthComponent>(donor).HasOrganDysfunction, Is.True);
            var oldHeart = Organ(recipient, "Heart");
            containers.Remove(oldHeart.Owner, recipientBody!);
            containers.Insert(heart.Owner, recipientBody!);
            Assert.That(heart.Comp.Integrity, Is.EqualTo(60));
            Assert.That(heart.Comp.KnownConditions.Single().Prototype.Id, Is.EqualTo("AdvancedOrganTrauma"));
            Assert.That(system.Function(recipient, "Heart"), Is.EqualTo(0.6f).Within(0.001));
            SEntMan.DeleteEntity(oldHeart.Owner);
        });
    }

    [Test]
    public async Task ShortDeficitsHaveGraceAndHydrationRestoresFunctionGradually()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var nutrition = SEntMan.System<AdvancedNutritionSystem>();
            var hunger = SEntMan.System<HungerSystem>();
            var thirst = SEntMan.System<ThirstSystem>();
            hunger.SetHunger(patient, 0);
            thirst.SetThirst(patient, SEntMan.GetComponent<ThirstComponent>(patient), 0);
            nutrition.Advance((patient, health), 120);
            var state = SEntMan.GetComponent<AdvancedNutritionComponent>(patient);
            Assert.That(state.Malnutrition, Is.Zero);
            Assert.That(state.DehydrationStrain, Is.Zero);
            Assert.That(Organ(patient, "Kidneys").Comp.Integrity, Is.EqualTo(100));
            nutrition.Advance((patient, health), 120);
            Assert.That(state.DehydrationStrain, Is.EqualTo(0.125f).Within(0.001));
            var impaired = Organ(patient, "Kidneys").Comp.Function;
            Assert.That(impaired, Is.LessThan(1));
            hunger.SetHunger(patient, 150);
            thirst.SetThirst(patient, SEntMan.GetComponent<ThirstComponent>(patient), 450);
            nutrition.Advance((patient, health), 1);
            Assert.That(state.DehydrationStrain, Is.GreaterThan(0));
            Assert.That(Organ(patient, "Kidneys").Comp.Function, Is.GreaterThan(impaired).And.LessThan(1));
            nutrition.Advance((patient, health), 300);
            Assert.That(state.DehydrationStrain, Is.Zero);
            Assert.That(Organ(patient, "Kidneys").Comp.Function, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ProlongedDeficitsDamageKidneysAndMuscleWithoutInstantFoodHealing()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var nutrition = SEntMan.System<AdvancedNutritionSystem>();
            var hunger = SEntMan.System<HungerSystem>();
            var thirst = SEntMan.System<ThirstSystem>();
            hunger.SetHunger(patient, 0);
            thirst.SetThirst(patient, SEntMan.GetComponent<ThirstComponent>(patient), 0);
            nutrition.Advance((patient, health), 2000);
            var kidney = Organ(patient, "Kidneys");
            var integrity = kidney.Comp.Integrity;
            var muscle = health.BodyParts[BodyPartSlot.LeftThigh].MuscleIntegrity;
            Assert.That(integrity, Is.LessThan(100));
            Assert.That(muscle, Is.LessThan(health.BodyParts[BodyPartSlot.LeftThigh].MaxMuscleIntegrity));
            var exertion = SEntMan.System<SharedAdvancedExertionSystem>();
            Assert.That(exertion.CanWork(patient, PhysicalWorkClass.Heavy), Is.False);
            Assert.That(exertion.CanWork(patient, PhysicalWorkClass.Light), Is.True);
            hunger.SetHunger(patient, 150);
            thirst.SetThirst(patient, SEntMan.GetComponent<ThirstComponent>(patient), 450);
            nutrition.Advance((patient, health), 2000);
            Assert.That(SEntMan.GetComponent<AdvancedNutritionComponent>(patient).Malnutrition, Is.Zero);
            Assert.That(kidney.Comp.Integrity, Is.EqualTo(integrity));
            Assert.That(health.BodyParts[BodyPartSlot.LeftThigh].MuscleIntegrity, Is.EqualTo(muscle));
        });
    }

    [Test]
    public async Task DeficitProgressionDoesNotDependOnUpdatePartitionAndContinuesAtMaximumLoad()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var single = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var stepped = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var nutrition = SEntMan.System<AdvancedNutritionSystem>();
            var hunger = SEntMan.System<HungerSystem>();
            var thirst = SEntMan.System<ThirstSystem>();
            foreach (var mob in new[] { single, stepped })
            {
                hunger.SetHunger(mob, 0);
                thirst.SetThirst(mob, SEntMan.GetComponent<ThirstComponent>(mob), 0);
            }
            var one = SEntMan.GetComponent<AdvancedHealthComponent>(single);
            var many = SEntMan.GetComponent<AdvancedHealthComponent>(stepped);
            nutrition.Advance((single, one), 1200);
            for (var i = 0; i < 1200; i++) nutrition.Advance((stepped, many), 1);
            Assert.That(SEntMan.GetComponent<AdvancedNutritionComponent>(stepped).Malnutrition,
                Is.EqualTo(SEntMan.GetComponent<AdvancedNutritionComponent>(single).Malnutrition).Within(0.001));
            Assert.That(Organ(stepped, "Kidneys").Comp.Integrity, Is.EqualTo(Organ(single, "Kidneys").Comp.Integrity).Within(0.01));
            Assert.That(many.BodyParts[BodyPartSlot.LeftThigh].MuscleIntegrity,
                Is.EqualTo(one.BodyParts[BodyPartSlot.LeftThigh].MuscleIntegrity).Within(0.01));
            var before = Organ(stepped, "Kidneys").Comp.Integrity;
            nutrition.Advance((stepped, many), 10);
            Assert.That(Organ(stepped, "Kidneys").Comp.Integrity, Is.LessThan(before));
        });
    }
    [TestCase(BodyPartSlot.Abdomen, true)]
    [TestCase(BodyPartSlot.Chest, false)]
    public async Task DigestiveTraumaCausesRealVomitingAndOnlyGastrointestinalBleedingProducesBlood(BodyPartSlot bleedingSlot, bool bloody)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var thirst = SEntMan.GetComponent<ThirstComponent>(patient);
            var before = thirst.CurrentThirst;
            FillStomach(patient);
            SEntMan.System<AdvancedOrganHealthSystem>().Damage(Organ(patient, "Stomach"), 80);
            health.BodyParts[bleedingSlot].Wounds.Add(new Wound { InternalBleedingRate = 1 });
            SEntMan.System<AdvancedDiseaseSystem>().Advance((patient, health), 40);
            var disease = SEntMan.GetComponent<AdvancedDiseaseComponent>(patient);
            Assert.That(disease.Symptoms.HasFlag(ClinicalSymptoms.Nausea), Is.True);
            Assert.That(disease.Symptoms.HasFlag(ClinicalSymptoms.Vomiting), Is.True);
            Assert.That(disease.Symptoms.HasFlag(ClinicalSymptoms.Hematemesis), Is.EqualTo(bloody));
            Assert.That(thirst.CurrentThirst, Is.LessThan(before), "Symptoms must cause the real vomiting action and resource loss.");
        });
    }

    [Test]
    public async Task RenalFailureAccumulatesToxinsAndHealthyKidneysDoNot()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var clinical = SEntMan.System<AdvancedDiseaseSystem>();
            clinical.Advance((patient, health), 30);
            Assert.That(health.ToxinLoad, Is.Zero);
            SEntMan.System<AdvancedOrganHealthSystem>().Damage(Organ(patient, "Kidneys"), 95);
            clinical.Advance((patient, health), 30);
            Assert.That(health.ToxinLoad, Is.GreaterThan(2));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StarvationAndDehydrationDoNotCauseVomitingOrFalseDigestiveBleeding(bool abdominalBleeding)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var stomach = FillStomach(patient);
            SEntMan.System<HungerSystem>().SetHunger(patient, 0);
            SEntMan.System<ThirstSystem>().SetThirst(patient, SEntMan.GetComponent<ThirstComponent>(patient), 0);
            SEntMan.System<AdvancedNutritionSystem>().Advance((patient, health), 1200);
            if (abdominalBleeding)
                health.BodyParts[BodyPartSlot.Abdomen].Wounds.Add(new Wound { InternalBleedingRate = 1 });
            var nutrition = SEntMan.GetComponent<AdvancedNutritionComponent>(patient);
            Assert.That(nutrition.Malnutrition, Is.EqualTo(1));
            Assert.That(nutrition.DehydrationStrain, Is.EqualTo(1));
            Assert.That(nutrition.RequiresCrawling, Is.True);
            Assert.That(nutrition.RecoveryMultiplier, Is.Zero);
            var clinical = SEntMan.System<AdvancedDiseaseSystem>();
            for (var i = 0; i < 900; i++) clinical.Advance((patient, health), 1);
            Assert.That(health.ToxinLoad, Is.Zero, "Reversible dehydration must not be interpreted as intrinsic renal failure.");
            Assert.That(Organ(patient, "Stomach").Comp.Function, Is.EqualTo(0.7f).Within(0.001), "Nutritional penalties still apply.");
            Assert.That(stomach.Volume.Float(), Is.EqualTo(10), "Starvation must not empty the actual stomach.");
            Assert.That(PuddleVolume(), Is.Zero);
            if (SEntMan.TryGetComponent<AdvancedDiseaseComponent>(patient, out var disease))
                Assert.That(disease.Symptoms & (ClinicalSymptoms.Nausea | ClinicalSymptoms.Vomiting | ClinicalSymptoms.Hematemesis), Is.EqualTo(ClinicalSymptoms.None));
        });
    }

    [Test]
    public async Task EmptyStomachCannotGenerateMorePuddlesOrConsumeMoreFoodAndWater()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var vomit = SEntMan.System<VomitSystem>();
            var hunger = SEntMan.GetComponent<HungerComponent>(patient);
            var thirst = SEntMan.GetComponent<ThirstComponent>(patient);
            var foodBefore = SEntMan.System<HungerSystem>().GetHunger(hunger);
            var waterBefore = thirst.CurrentThirst;
            for (var i = 0; i < 20; i++) Assert.That(vomit.Vomit(patient), Is.False);
            Assert.That(PuddleVolume(), Is.Zero);
            Assert.That(SEntMan.System<HungerSystem>().GetHunger(hunger), Is.EqualTo(foodBefore));
            Assert.That(thirst.CurrentThirst, Is.EqualTo(waterBefore));

            var stomach = FillStomach(patient, 5);
            Assert.That(vomit.Vomit(patient), Is.True);
            Assert.That(stomach.Volume.Float(), Is.Zero);
            var spilled = PuddleVolume();
            Assert.That(spilled, Is.EqualTo(5), "Only actual stomach contents may reach the floor.");
            Assert.That(thirst.CurrentThirst, Is.LessThan(waterBefore));
            foodBefore = SEntMan.System<HungerSystem>().GetHunger(hunger);
            waterBefore = thirst.CurrentThirst;
            for (var i = 0; i < 20; i++) Assert.That(vomit.Vomit(patient), Is.False);
            Assert.That(PuddleVolume(), Is.EqualTo(spilled));
            Assert.That(SEntMan.System<HungerSystem>().GetHunger(hunger), Is.EqualTo(foodBefore));
            Assert.That(thirst.CurrentThirst, Is.EqualTo(waterBefore));
        });
    }

    [Test]
    public async Task MildDigestiveInjuryCausesNauseaWithoutGuaranteedVomiting()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var stomach = FillStomach(patient);
            SEntMan.System<AdvancedOrganHealthSystem>().Damage(Organ(patient, "Stomach"), 30);
            for (var i = 0; i < 20; i++) SEntMan.System<AdvancedDiseaseSystem>().Advance((patient, health), 30);
            var symptoms = SEntMan.GetComponent<AdvancedDiseaseComponent>(patient).Symptoms;
            Assert.That(symptoms.HasFlag(ClinicalSymptoms.Nausea), Is.True);
            Assert.That(symptoms.HasFlag(ClinicalSymptoms.Vomiting), Is.False);
            Assert.That(stomach.Volume.Float(), Is.EqualTo(10));
            Assert.That(PuddleVolume(), Is.Zero);
        });
    }

    [Test]
    public async Task ResolvedNauseaCannotTriggerVomitingFromOldExposure()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var stomach = FillStomach(patient);
            var state = SEntMan.EnsureComponent<AdvancedDiseaseComponent>(patient);
            state.NauseaExposure = 100;
            SEntMan.System<AdvancedDiseaseSystem>().Advance((patient, SEntMan.GetComponent<AdvancedHealthComponent>(patient)), 1);
            Assert.That(state.Symptoms, Is.EqualTo(ClinicalSymptoms.None));
            Assert.That(stomach.Volume.Float(), Is.EqualTo(10));
            Assert.That(PuddleVolume(), Is.Zero);
        });
    }

    [Test]
    public async Task DigestiveBleedingFillsTheActualStomachAndCannotInventExtraVomit()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var stomach = FillStomach(patient, 5);
            var blood = new Solution(SEntMan.GetComponent<Content.Shared.Body.Components.BloodstreamComponent>(patient).BloodReferenceSolution.Contents);
            var unitsPerMl = blood.Volume.Float() / health.MaxBloodVolume;
            SEntMan.System<AdvancedOrganHealthSystem>().Damage(Organ(patient, "Stomach"), 80);
            health.BodyParts[BodyPartSlot.Abdomen].Wounds.Add(new Wound { InternalBleedingRate = 1 });
            var clinical = SEntMan.System<AdvancedDiseaseSystem>();
            clinical.Advance((patient, health), 10);
            Assert.That(stomach.Volume.Float(), Is.EqualTo(5 + 10 * unitsPerMl).Within(0.01));
            SEntMan.System<AdvancedNutritionSystem>().Advance((patient, health), 1);
            Assert.That(health.StomachVolume, Is.EqualTo(stomach.Volume.Float()));
            clinical.Advance((patient, health), 30);
            Assert.That(stomach.Volume.Float(), Is.Zero);
            Assert.That(PuddleVolume(), Is.EqualTo(5 + 40 * unitsPerMl).Within(0.01));
            Assert.That(SEntMan.GetComponent<AdvancedDiseaseComponent>(patient).Symptoms.HasFlag(ClinicalSymptoms.Hematemesis), Is.True);
        });
    }

}
