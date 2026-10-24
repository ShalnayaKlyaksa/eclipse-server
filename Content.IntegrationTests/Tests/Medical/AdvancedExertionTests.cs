using Robust.Shared.Maths;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client.UserInterface.Systems.DamageOverlays;
using Content.Client.UserInterface.Systems.DamageOverlays.Overlays;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Damage.Components;
using Content.Shared.DoAfter;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Medical;

public sealed partial class AdvancedExertionTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Serializable, NetSerializable]
    private sealed partial class WorkTestEvent : SimpleDoAfterEvent;

    private async Task<EntityUid> SpawnHuman()
    {
        var map = await Pair.CreateTestMap();
        EntityUid uid = default;
        await Server.WaitPost(() => uid = SEntMan.SpawnEntity("MobHuman", map.MapCoords));
        return uid;
    }

    [Test]
    public async Task SprintExhaustionPreservesWalkingAndDoesNotCauseStaminaParalysis()
    {
        var uid = await SpawnHuman();
        await Server.WaitAssertion(() =>
        {
            var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(uid);
            var mover = SEntMan.GetComponent<InputMoverComponent>(uid);
            var input = SEntMan.System<SharedMoverController>();
            var system = SEntMan.System<SharedAdvancedExertionSystem>();
            var stamina = SEntMan.EnsureComponent<StaminaComponent>(uid);
            var previousDamage = stamina.StaminaDamage;
            Assert.That(mover.Sprinting, Is.False, "Normal gait is walking.");
            input.SetSprinting((uid, mover), 0, true);
            Assert.That(mover.Sprinting, Is.True, "Shift requests running.");
            system.Advance((uid, exertion), 40f, true, false);
            Assert.That(exertion.Exhausted, Is.True);
            Assert.That(mover.Sprinting, Is.False);
            Assert.That(mover.CanMove, Is.True);
            Assert.That(stamina.StaminaDamage, Is.EqualTo(previousDamage));
            Assert.That(stamina.Critical, Is.False);
            Assert.That(system.CanWork(uid, PhysicalWorkClass.Light), Is.True);
            Assert.That(system.CanWork(uid, PhysicalWorkClass.Medium), Is.True);
            Assert.That(system.CanWork(uid, PhysicalWorkClass.Heavy), Is.False);
            system.Advance((uid, exertion), 5f, false, false);
            Assert.That(exertion.Exhausted, Is.False);
            Assert.That(exertion.Reserve, Is.InRange(25, 100));
            Assert.That(mover.Sprinting, Is.True, "Held Shift becomes usable after recovery hysteresis.");
        });
    }

    [Test]
    public async Task WorkRequiresFunctionalHandsAndSleepRecoversDebt()
    {
        var uid = await SpawnHuman();
        await Server.WaitAssertion(() =>
        {
            var system = SEntMan.System<SharedAdvancedExertionSystem>();
            var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(uid);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(uid);
            Assert.That(system.CanWork(uid, PhysicalWorkClass.Heavy, 2, 1), Is.True);
            health.BodyParts[BodyPartSlot.LeftUpperArm].NerveIntegrity = 0;
            Assert.That(system.CanWork(uid, PhysicalWorkClass.Heavy, 2, 1), Is.False);
            Assert.That(system.CanWork(uid, PhysicalWorkClass.Medium, 1), Is.True);
            Assert.That(system.SpendWork(uid, PhysicalWorkClass.Medium, float.NaN), Is.False);
            exertion.SleepDebt = 50;
            exertion.Reserve = 20;
            system.Advance((uid, exertion), 100, false, true);
            Assert.That(exertion.SleepDebt, Is.EqualTo(30).Within(0.01));
            Assert.That(exertion.Reserve, Is.EqualTo(100));
            system.Advance((uid, exertion), 10, true, false);
            Assert.That(exertion.SleepDebt, Is.LessThan(30.1), "A short sprint is not equivalent to a sleepless night.");
        });
    }

    [Test]
    public async Task ShortSprintIsRecordedWhenInputStopsBetweenPhysiologyTicks()
    {
        var uid = await SpawnHuman();
        await Server.WaitAssertion(() =>
        {
            var input = SEntMan.System<SharedMoverController>();
            var mover = SEntMan.GetComponent<InputMoverComponent>(uid);
            input.SetSprinting((uid, mover), 0, true);
            input.SetVelocityDirection((uid, mover), Direction.East, 0, true);
            SEntMan.System<SharedPhysicsSystem>().SetLinearVelocity(uid, new Vector2(4, 0));
            var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(uid);
            exertion.RunningSeconds = 0;
            exertion.LastMotionSample = Server.ResolveDependency<IGameTiming>().CurTime - TimeSpan.FromSeconds(0.2);
            input.SetVelocityDirection((uid, mover), Direction.East, 0, false);
            Assert.That(exertion.RunningSeconds, Is.EqualTo(0.2).Within(0.001));
        });
    }

    [Test]
    public async Task CancelledWorkDoesNotChargeItsUnperformedRemainder()
    {
        var uid = await SpawnHuman();
        DoAfterId? id = null;
        await Server.WaitAssertion(() =>
        {
            var args = new DoAfterArgs(SEntMan, uid, 20f, new WorkTestEvent(), uid)
            {
                WorkClass = PhysicalWorkClass.Medium,
                WorkingHands = 1,
                BreakOnMove = false,
            };
            Assert.That(SEntMan.System<SharedDoAfterSystem>().TryStartDoAfter(args, out id), Is.True);
        });
        await Pair.RunTicksSync(30);
        await Server.WaitAssertion(() =>
        {
            SEntMan.System<SharedDoAfterSystem>().Cancel(id);
            Assert.That(SEntMan.GetComponent<AdvancedExertionComponent>(uid).Reserve, Is.GreaterThan(96));
        });
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<AdvancedExertionComponent>(uid).Reserve, Is.GreaterThan(95)));
    }

    [Test]
    public async Task GunshotContaminationDoesNotInventAnInfectionAndDeadBodiesStayDead()
    {
        var uid = await SpawnHuman();
        await Server.WaitPost(() =>
        {
            SEntMan.GetComponent<AdvancedHealthComponent>(uid).BodyParts[BodyPartSlot.Chest].Wounds.Add(new Wound
            {
                Type = WoundType.Gunshot, Severity = WoundSeverity.Minor, InfectionRisk = 1f, IsDirty = true,
            });
        });
        await Pair.RunTicksSync(120);
        await Server.WaitAssertion(() =>
        {
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(uid);
            Assert.That(health.InfectionLoad, Is.Zero);
            SEntMan.System<MobStateSystem>().ChangeMobState(uid, MobState.Dead);
        });
        await Pair.RunTicksSync(120);
        await Server.WaitAssertion(() =>
        {
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(uid);
            Assert.That(SEntMan.GetComponent<MobStateComponent>(uid).CurrentState, Is.EqualTo(MobState.Dead));
            Assert.That(health.HeartRate, Is.Zero);
            Assert.That(health.Consciousness, Is.Zero);
            Assert.That(health.IsUnconscious && health.IsHeartStopped, Is.True);
        });
    }

    [Test]
    public async Task DeadOverlayRejectsLateLivingVitalsAndAllowsAnExplicitRevival()
    {
        await Client.WaitAssertion(() =>
        {
            var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<DamageOverlayUiController>();
            var overlay = (DamageOverlay) typeof(DamageOverlayUiController)
                .GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
            var stale = new AdvancedHealthComponent { Consciousness = 100, IsHeartStopped = false };
            controller.UpdateFromAdvancedHealth(stale, MobState.Dead);
            controller.UpdateFromAdvancedHealth(stale, MobState.Dead);
            Assert.That(overlay.State, Is.EqualTo(MobState.Dead));
            Assert.That(overlay.CritLevel, Is.Zero);
            Assert.That(overlay.AdvancedPhysiology, Is.True);
            controller.UpdateFromAdvancedHealth(stale, MobState.Alive);
            Assert.That(overlay.State, Is.EqualTo(MobState.Alive));
        });
    }

    [Test]
    public void RestingPulseVariationIsBoundedSmoothAndNotConstant()
    {
        var values = Enumerable.Range(0, 600).Select(i => SharedAdvancedExertionSystem.RestingPulseVariation(i, 17)).ToArray();
        Assert.That(values.All(value => value >= -5 && value <= 5), Is.True);
        Assert.That(values.Max() - values.Min(), Is.GreaterThan(5));
        Assert.That(Math.Abs(SharedAdvancedExertionSystem.RestingPulseVariation(1.01, 17)
            - SharedAdvancedExertionSystem.RestingPulseVariation(1, 17)), Is.LessThan(0.1));
    }
}
