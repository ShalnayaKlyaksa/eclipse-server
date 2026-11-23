using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.AdvancedHealth;
using Content.Server.Body.Components;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Body;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Rejuvenate;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class HydrationRegressionTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestCase("MobHuman")]
    [TestCase("MobShadowkin")]
    public async Task NineteenPercentHydrationAndOldStrainCannotCauseCirculatoryCollapse(string species)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity(species, map.MapCoords);
            SEntMan.RemoveComponent<RespiratorComponent>(patient);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var thirst = SEntMan.GetComponent<ThirstComponent>(patient);
            SEntMan.System<HungerSystem>().SetHunger(patient, 150);
            SEntMan.System<ThirstSystem>().SetThirst(patient, thirst, thirst.ThirstThresholds[ThirstThreshold.OverHydrated] * 0.19f);
            var nutrition = SEntMan.EnsureComponent<AdvancedNutritionComponent>(patient);
            nutrition.DehydrationStrain = 0.82f;
            nutrition.DehydrationExposure = nutrition.DehydrationGrace + nutrition.DehydrationProgressTime * 0.82f;
            var tick = SEntMan.System<AdvancedHealthTickSystem>();
            for (var i = 0; i < 900; i++)
            {
                tick.Update(1);
                Assert.That(health.IsHeartStopped, Is.False, $"Unexpected arrest at second {i}.");
                Assert.That(health.MeanArterialPressure, Is.GreaterThan(health.AdequatePressure));
                Assert.That(health.Oxygenation, Is.GreaterThan(90));
            }
            Assert.That(SEntMan.GetComponent<MobStateComponent>(patient).CurrentState, Is.EqualTo(MobState.Alive));
            Assert.That(nutrition.DehydrationStrain, Is.LessThanOrEqualTo(0.25f));
            Assert.That(health.BrainFunction, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task PartialFoodAndWaterDeficitsDoNotBecomeTerminalJustByWaiting()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var hunger = SEntMan.GetComponent<HungerComponent>(patient);
            var thirst = SEntMan.GetComponent<ThirstComponent>(patient);
            SEntMan.System<HungerSystem>().SetHunger(patient, hunger.Thresholds[HungerThreshold.Starving] * 0.75f);
            SEntMan.System<ThirstSystem>().SetThirst(patient, thirst, thirst.ThirstThresholds[ThirstThreshold.Parched] * 0.75f);
            SEntMan.System<AdvancedNutritionSystem>().Advance((patient, health), 3600);
            var nutrition = SEntMan.GetComponent<AdvancedNutritionComponent>(patient);
            Assert.That(nutrition.Malnutrition, Is.EqualTo(0.25f).Within(0.001));
            Assert.That(nutrition.DehydrationStrain, Is.EqualTo(0.25f).Within(0.001));
            Assert.That(nutrition.RequiresCrawling, Is.False);
            Assert.That(SEntMan.System<AdvancedOrganHealthSystem>().Organs(patient).All(organ => organ.Comp.Integrity == 100), Is.True);
        });
    }

    [TestCase("MobHuman")]
    [TestCase("MobShadowkin")]
    public async Task AbsorbedFluidRelievesCirculatoryStressWhileKidneysRecoverGradually(string species)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity(species, map.MapCoords);
            SEntMan.RemoveComponent<RespiratorComponent>(patient);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var thirst = SEntMan.GetComponent<ThirstComponent>(patient);
            var system = SEntMan.System<AdvancedNutritionSystem>();
            SEntMan.System<ThirstSystem>().SetThirst(patient, thirst, 0);
            system.Advance((patient, health), 660);
            var nutrition = SEntMan.GetComponent<AdvancedNutritionComponent>(patient);
            Assert.That(nutrition.DehydrationStrain, Is.EqualTo(1));
            var kidney = SEntMan.System<AdvancedOrganHealthSystem>().Organs(patient)
                .Single(organ => SEntMan.GetComponent<OrganComponent>(organ).Category?.Id == "Kidneys");
            var integrity = kidney.Comp.Integrity;
            SEntMan.System<ThirstSystem>().SetThirst(patient, thirst, thirst.ThirstThresholds[ThirstThreshold.OverHydrated] * 0.19f);
            for (var i = 0; i < 10; i++) SEntMan.System<AdvancedHealthTickSystem>().Update(1);
            Assert.That(nutrition.DehydrationStrain, Is.GreaterThan(0.9f), "Existing organ strain must not disappear instantly.");
            Assert.That(kidney.Comp.Integrity, Is.EqualTo(integrity), "Replenishment must stop additional dehydration injury.");
            Assert.That(health.MeanArterialPressure, Is.GreaterThan(health.AdequatePressure));
            Assert.That(health.IsHeartStopped, Is.False);
        });
    }

    [Test]
    public async Task CardiacArrestRecordsTheActualTriggerAndRejuvenationClearsIt()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            SEntMan.RemoveComponent<RespiratorComponent>(patient);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            health.Oxygenation = 0;
            health.HeartRate = 0;
            SEntMan.System<AdvancedHealthTickSystem>().Update(1);
            Assert.That(health.IsHeartStopped, Is.True);
            Assert.That(health.ArrestTrigger, Is.EqualTo(CardiacArrestTrigger.Hypoxia));
            SEntMan.EventBus.RaiseLocalEvent(patient, new RejuvenateEvent());
            Assert.That(health.IsHeartStopped, Is.False);
            Assert.That(health.ArrestTrigger, Is.EqualTo(CardiacArrestTrigger.Unknown));
        });
    }
}
