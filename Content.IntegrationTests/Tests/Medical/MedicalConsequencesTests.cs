using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.AdvancedHealth;
using Content.Server.Body.Systems;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Alert;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Standing;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class MedicalConsequencesTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: advancedDisease
  id: TestSprintBlockingCondition
  name: clinical-condition-unknown-desc
  description: clinical-condition-unknown-desc
  speedModifier: 0.5
";
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task SlippingTearsTheActualWoundAndDoesNotRemoveItsForeignBody()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(uid);
            var part = health.BodyParts[BodyPartSlot.LeftForearm];
            var wound = new Wound { HasForeignBody = true, ForeignBody = ForeignBodyKind.Glass, ExternalBleedingRate = 1, Pain = 5 };
            part.Wounds.Add(wound);
            part.ForeignBodyCount = 1;
            var system = SEntMan.System<AdvancedTreatmentInjurySystem>();
            Assert.That(system.ApplyInjury(uid, uid, part.Slot, AdvancedTreatmentType.ForeignBodyRemoval, null, float.NaN), Is.False);
            Assert.That(system.ApplyInjury(uid, uid, part.Slot, AdvancedTreatmentType.ForeignBodyRemoval, null, 1), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(wound.ExternalBleedingRate, Is.GreaterThan(1.5f));
                Assert.That(wound.Pain, Is.GreaterThan(15));
                Assert.That(part.SkinFraction, Is.LessThan(1));
                Assert.That(health.Pain, Is.GreaterThan(0));
                Assert.That(wound.HasForeignBody, Is.True);
                Assert.That(part.ForeignBodyCount, Is.EqualTo(1));
                Assert.That(wound.IsDirty, Is.True);
            });
            var bleeding = wound.ExternalBleedingRate;
            Assert.That(system.ApplyInjury(uid, uid, part.Slot, AdvancedTreatmentType.ForeignBodyRemoval, null, 1), Is.False,
                "Repeated packets in the same instant must not multiply the injury.");
            Assert.That(wound.ExternalBleedingRate, Is.EqualTo(bleeding));
        });
    }

    [Test]
    public async Task ExhaustionAndSleepDebtHaveTheirOwnAlertsAndDoNotChangeMedicalCondition()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(patient);
            var alerts = SEntMan.System<AlertsSystem>();
            exertion.SleepDebt = 100;
            var exertionSystem = SEntMan.System<SharedAdvancedExertionSystem>();
            Assert.That(exertionSystem.TryDrainReserve((patient, exertion), 100), Is.True);
            exertionSystem.Advance((patient, exertion), 1, false, false);
            SEntMan.System<AdvancedHealthTickSystem>().Update(1);

            Assert.That(alerts.IsShowingAlert(patient, "AdvancedExhausted"), Is.True);
            Assert.That(alerts.IsShowingAlert(patient, "AdvancedSleepy"), Is.True);
            Assert.That(health.CachedAlertSeverity, Is.Zero);
            Assert.That(SEntMan.GetComponent<MobStateComponent>(patient).CurrentState, Is.EqualTo(MobState.Alive));
            Assert.That(SEntMan.System<StandingStateSystem>().IsDown(patient), Is.False);
        });
    }

    [Test]
    public async Task SuffocationWarnsBeforeCollapseAndPublishesCurrentVitals()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var tick = SEntMan.System<AdvancedHealthTickSystem>();
            var mobState = SEntMan.GetComponent<MobStateComponent>(patient);
            SEntMan.System<RespiratorSystem>().UpdateSaturation(patient, -100);

            tick.Update(1);
            Assert.That(health.IsSuffocating, Is.True);
            Assert.That(health.Oxygenation, Is.GreaterThan(95));
            Assert.That(health.CachedAlertSeverity, Is.GreaterThanOrEqualTo(2));
            Assert.That(mobState.CurrentState, Is.EqualTo(MobState.Alive));

            tick.Update(20);
            Assert.That(mobState.CurrentState, Is.EqualTo(MobState.Critical));
            Assert.That(health.SmoothedConditionBadness, Is.GreaterThanOrEqualTo(1f - health.Oxygenation / 100f));
            Assert.That(SEntMan.System<AlertsSystem>().IsShowingAlert(patient, "HumanCrit"), Is.True);
        });
    }

    [Test]
    public async Task EmptyFoodAndWaterForceSlowCrawlingAndPreventEnduranceRecovery()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var uid = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(uid);
            SEntMan.System<HungerSystem>().SetHunger(uid, 0);
            SEntMan.System<ThirstSystem>().SetThirst(uid, SEntMan.GetComponent<ThirstComponent>(uid), 0);
            SEntMan.System<AdvancedHealthTickSystem>().Update(1);
            var nutrition = SEntMan.GetComponent<AdvancedNutritionComponent>(uid);
            Assert.That(nutrition.RequiresCrawling, Is.True);
            Assert.That(health.CachedAlertSeverity, Is.GreaterThanOrEqualTo(3));
            Assert.That(nutrition.MovementMultiplier, Is.LessThanOrEqualTo(0.2f));
            Assert.That(SEntMan.System<StandingStateSystem>().Stand(uid), Is.False);
            var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(uid);
            exertion.Reserve = 50;
            SEntMan.System<SharedAdvancedExertionSystem>().Advance((uid, exertion), 10, false, false);
            Assert.That(exertion.Reserve, Is.LessThanOrEqualTo(5));
            Assert.That(exertion.Exhausted, Is.True);
            Assert.That(SEntMan.System<SharedMedicalConditionSystem>().CanSprint(uid), Is.False);
            var before = exertion.Reserve;
            SEntMan.System<SharedAdvancedExertionSystem>().Advance((uid, exertion), 10, false, true);
            Assert.That(exertion.Reserve, Is.LessThanOrEqualTo(before), "Sleep cannot replace food and water.");
        });
    }

    [Test]
    public async Task TogglingGaitDuringBlockedSprintDoesNotAccumulateEffort()
    {
        var map = await Pair.CreateTestMap();
        EntityUid pawn = default;
        await Server.WaitAssertion(() =>
        {
            var uid = pawn = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            SEntMan.GetComponent<AdvancedHealthComponent>(uid).NeedsOxygen = false;
            Assert.That(SEntMan.System<AdvancedDiseaseSystem>().ApplyCondition(uid, "TestSprintBlockingCondition"), Is.True);
            var input = SEntMan.System<SharedMoverController>();
            var system = SEntMan.System<SharedAdvancedExertionSystem>();
            var mover = SEntMan.GetComponent<InputMoverComponent>(uid);
            var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(uid);
            SEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(uid, new Vector2(3, 0));
            input.SetVelocityDirection((uid, mover), Robust.Shared.Maths.Direction.East, 0, true);
            for (var i = 0; i < 30; i++)
            {
                exertion.LastMotionSample = Server.ResolveDependency<IGameTiming>().CurTime - TimeSpan.FromSeconds(0.2);
                input.SetSprinting((uid, mover), 0, false);
                system.SetCalmWalking(uid, i % 2 == 0);
                system.RecordMotion(uid, mover);
                Assert.That(mover.Sprinting, Is.False);
            }
            Assert.That(exertion.RunningSeconds, Is.Zero);
            system.Advance((uid, exertion), 1, exertion.RunningSeconds > 0, false);
            Assert.That(exertion.Effort, Is.Zero);
            Assert.That(exertion.RecoveryLoad, Is.Zero);
        });
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(pawn);
            var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(pawn);
            Assert.That(exertion.Effort, Is.Zero);
            Assert.That(health.HeartRate, Is.LessThan(health.RestingHeartRate + 12));
            Assert.That(health.MeanArterialPressure, Is.LessThan(health.BaselinePressure + 8),
                "A blocked sprint must not create a cardiovascular workload.");
        });
    }
}
