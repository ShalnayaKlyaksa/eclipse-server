using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.AdvancedHealth;
using Content.Server.Body.Components;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Alert;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Metabolism;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class PhysiologyProgressionTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task MixedAndSingleIngredientMealsEmptyAtTheSameTotalRateAndPublishFullness()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var nutrition = SEntMan.System<AdvancedNutritionSystem>();
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var organs = SEntMan.System<AdvancedOrganHealthSystem>();
            var plain = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var mixed = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var stomachs = new[] { plain, mixed }.Select(body => organs.Organs(body)
                .Single(organ => SEntMan.GetComponent<OrganComponent>(organ).Category?.Id == "Stomach").Owner).ToArray();
            for (var i = 0; i < stomachs.Length; i++)
            {
                Assert.That(solutions.TryGetSolution(stomachs[i], StomachSystem.DefaultSolutionName, out var entity, out _), Is.True);
                var meal = i == 0 ? new Solution("Nutriment", 30) : new Solution("Nutriment", 10);
                if (i != 0) { meal.AddReagent("Water", 10); meal.AddReagent("Vitamin", 10); }
                Assert.That(SEntMan.System<StomachSystem>().TryTransferSolution(stomachs[i], meal), Is.True);
            }
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(mixed);
            nutrition.Advance((mixed, health), 1);
            Assert.That(health.StomachVolume, Is.EqualTo(30));
            Assert.That(health.StomachCapacity, Is.EqualTo(50));
            for (var step = 0; step < 40; step++)
            {
#pragma warning disable RA0002 // Set only these digestion clocks; the real metabolism system performs each transfer.
                foreach (var stomach in stomachs)
                    SEntMan.GetComponent<MetabolizerComponent>(stomach).NextUpdate = Server.ResolveDependency<IGameTiming>().CurTime;
#pragma warning restore RA0002
                SEntMan.System<MetabolizerSystem>().Update(1);
            }
            nutrition.Advance((mixed, health), 1);
            Assert.That(health.StomachVolume, Is.EqualTo(20).Within(0.01));
            nutrition.Advance((plain, SEntMan.GetComponent<AdvancedHealthComponent>(plain)), 1);
            Assert.That(SEntMan.GetComponent<AdvancedHealthComponent>(plain).StomachVolume, Is.EqualTo(health.StomachVolume).Within(0.01));
        });
    }

    [TestCase("starvation", 5200)]
    [TestCase("dehydration", 1500)]
    [TestCase("ClinicalSepticShock", 600)]
    [TestCase("ClinicalBacterialPneumonia", 1500)]
    public async Task UntreatedSevereIllnessAndDeficitsCanKillThroughActualPhysiology(string cause, int duration)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            // Isolate these causes from the test map's lack of breathable atmosphere.
            SEntMan.RemoveComponent<RespiratorComponent>(patient);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            if (cause == "starvation") SEntMan.System<HungerSystem>().SetHunger(patient, 0);
            else if (cause == "dehydration")
                SEntMan.System<ThirstSystem>().SetThirst(patient, SEntMan.GetComponent<ThirstComponent>(patient), 0);
            else Assert.That(SEntMan.System<AdvancedDiseaseSystem>().ApplyCondition(patient, cause, 0.9f), Is.True);
            var state = SEntMan.GetComponent<MobStateComponent>(patient);
            var tick = SEntMan.System<AdvancedHealthTickSystem>();
            for (var time = 0; time < duration && state.CurrentState != MobState.Dead; time += 5) tick.Update(5);
            Assert.That(state.CurrentState, Is.EqualTo(MobState.Dead), $"{cause}: oxygen={health.Oxygenation}, pressure={health.MeanArterialPressure}, shock={health.Shock}, heart stopped={health.IsHeartStopped}");
            Assert.That(health.BloodVolume, Is.EqualTo(health.MaxBloodVolume), "The isolated cause must kill without external blood loss.");
            Assert.That(health.BodyTemperature, Is.LessThan(health.NormalBodyTemperature + 5), "Hyperthermia must not be the cause of death.");
        });
    }

    [Test]
    public async Task MildIllnessDoesNotBecomeAnAutomaticDeathTimer()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            SEntMan.RemoveComponent<RespiratorComponent>(patient);
            Assert.That(SEntMan.System<AdvancedDiseaseSystem>().ApplyCondition(patient, "ClinicalViralBronchitis", 0.2f), Is.True);
            for (var time = 0; time < 600; time += 5) SEntMan.System<AdvancedHealthTickSystem>().Update(5);
            Assert.That(SEntMan.GetComponent<MobStateComponent>(patient).CurrentState, Is.EqualTo(MobState.Alive));
        });
    }

    [Test]
    public async Task SystemicDeficitsChangeTheLiveHealthAlertAndAffectTheWholeBody()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var alerts = SEntMan.System<AlertsSystem>();
            alerts.ShowAlert(patient, "HumanHealth", 0);
            SEntMan.System<HungerSystem>().SetHunger(patient, 0);
            SEntMan.System<AdvancedNutritionSystem>().Advance((patient, health), 1200);
            SEntMan.System<AdvancedHealthSystem>().RefreshConditionAlert((patient, health), 5);
            Assert.That(health.CachedAlertSeverity, Is.GreaterThanOrEqualTo(3));
            Assert.That(alerts.GetActiveAlerts(patient)!.Values.Single(alert => alert.Type.Id == "HumanHealth").Severity, Is.GreaterThanOrEqualTo(3));
            var functions = SEntMan.System<SharedMedicalConditionSystem>();
            Assert.That(functions.GetFunction(health, BodyPartSlot.Head), Is.LessThan(0.5));
            Assert.That(functions.GetFunction(health, BodyPartSlot.Chest), Is.LessThan(0.5));
            Assert.That(health.BodyParts[BodyPartSlot.Chest].MuscleFraction, Is.LessThan(1));
        });
    }

    [Test]
    public async Task RealSleepRecoversAllDebtAndUsesADistinctAlert()
    {
        await Server.WaitPost(() => Server.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>().SetCVar(Robust.Shared.CVars.NetTickrate, 60));
        await Pair.RunTicksSync(10);
        var map = await Pair.CreateTestMap();
        EntityUid patient = default;
        await Server.WaitAssertion(() =>
        {
            patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            // External injury should wake a sleeper; keep this test map's vacuum out of the test.
            SEntMan.RemoveComponent<RespiratorComponent>(patient);
            SEntMan.RemoveComponent<Content.Server.Atmos.Components.BarotraumaComponent>(patient);
            SEntMan.RemoveComponent<Content.Shared.Temperature.Components.TemperatureDamageComponent>(patient);
            SEntMan.System<HungerSystem>().SetHunger(patient, 150);
            SEntMan.System<ThirstSystem>().SetThirst(patient, SEntMan.GetComponent<ThirstComponent>(patient), 450);
            var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(patient);
            exertion.SleepDebt = 65;
            Assert.That(SEntMan.System<SleepingSystem>().TrySleeping(patient), Is.True);
        });
        await Pair.RunSeconds(40);
        await Server.WaitAssertion(() =>
        {
            var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(patient);
            Assert.That(exertion.SleepDebt, Is.LessThan(50), "Sleeping must carry recovery through the middle of the range.");
            Assert.That(SEntMan.HasComponent<SleepingComponent>(patient), Is.True);
            SEntMan.System<SharedAdvancedExertionSystem>().Advance((patient, exertion), 150, false, true);
            Assert.That(exertion.SleepDebt, Is.Zero);
            Assert.That(exertion.ReserveCapacity, Is.EqualTo(100));
            Assert.That(SEntMan.System<AlertsSystem>().IsShowingAlert(patient, "AdvancedSleepy"), Is.False);
            Assert.That(SEntMan.System<SleepingSystem>().TryWaking(patient), Is.True);
        });
        await Pair.RunTicksSync(10);
    }
}
