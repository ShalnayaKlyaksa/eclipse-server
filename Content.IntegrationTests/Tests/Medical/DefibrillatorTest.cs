#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._Eclipse.AdvancedHealth;
using Content.Server.Body.Systems;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Body;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Medical;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Medical;

/// <summary>
/// Tests for defibrilators.
/// </summary>
[TestOf(typeof(DefibrillatorComponent))]
public sealed class DefibrillatorTest : InteractionTest
{
    // We need two hands to use a defbrillator.
    protected override string PlayerPrototype => "MobHuman";

    private static readonly EntProtoId DefibrillatorProtoId = "Defibrillator";
    private static readonly EntProtoId TargetProtoId = "MobHuman";
    private static readonly ProtoId<DamageTypePrototype> BluntDamageTypeId = "Blunt";

    /// <summary>
    /// Kills a target mob, heals them and then revives them with a defibrillator.
    /// </summary>
    [Test]
    public async Task KillAndReviveTest()
    {
        var damageableSystem = SEntMan.System<DamageableSystem>();
        var mobThresholdsSystem = SEntMan.System<MobThresholdSystem>();

        // Don't let the player and target suffocate.
        await AddAtmosphere();

        await SpawnTarget(TargetProtoId);

        // Exercise the damage-threshold revival path used by mobs without AdvancedHealth.
        await Server.WaitPost(() => SEntMan.RemoveComponent<AdvancedHealthComponent>(STarget!.Value));
        var targetMobState = Comp<MobStateComponent>();
        var targetDamageable = Comp<DamageableComponent>();

        // Check that the target has no damage and is not crit or dead.
        Assert.Multiple(() =>
        {
            Assert.That(targetMobState.CurrentState, Is.EqualTo(MobState.Alive), "Target mob was not alive when spawned.");
            Assert.That(damageableSystem.GetTotalDamage(STarget!.Value), Is.EqualTo(FixedPoint2.Zero), "Target mob was damaged when spawned.");
        });

        // Get the damage needed to kill or crit the target.
        var critThreshold = mobThresholdsSystem.GetThresholdForState(STarget.Value, MobState.Critical);
        var deathThreshold = mobThresholdsSystem.GetThresholdForState(STarget.Value, MobState.Dead);
        var critDamage = new DamageSpecifier(ProtoMan.Index(BluntDamageTypeId), (critThreshold + deathThreshold) / 2);
        var deathDamage = new DamageSpecifier(ProtoMan.Index(BluntDamageTypeId), deathThreshold);

        // Kill the target by applying blunt damage.
        await Server.WaitPost(() => damageableSystem.SetDamage((STarget.Value, targetDamageable), deathDamage));
        await RunTicks(3);

        // Check that the target is dead.
        Assert.Multiple(() =>
        {
            Assert.That(targetMobState.CurrentState, Is.EqualTo(MobState.Dead), "Target mob did not die from deadly damage amount.");
            Assert.That(damageableSystem.GetTotalDamage(STarget!.Value), Is.EqualTo(deathThreshold), "Target mob had the wrong total damage amount after being killed.");
        });

        // Spawn a defib and activate it.
        var defib = await PlaceInHands(DefibrillatorProtoId, enableToggleable: true);
        var cooldown = Comp<DefibrillatorComponent>(defib).ZapDelay;

        // Wait for the cooldown.
        await RunSeconds((float)cooldown.TotalSeconds);

        // ZAP!
        await Interact();

        // Check that the target is still dead since it is over the crit threshold.
        // And it should have taken some extra damage.
        Assert.Multiple(() =>
        {
            Assert.That(targetMobState.CurrentState, Is.EqualTo(MobState.Dead), "Target mob was revived despite being over the death damage threshold.");
            Assert.That(damageableSystem.GetTotalDamage(STarget!.Value), Is.GreaterThan(deathThreshold), "Target mob did not take damage from being defibrillated.");
        });

        // Set the damage halfway between the crit and death thresholds so that the target can be revived.
        await Server.WaitPost(() => damageableSystem.SetDamage((STarget.Value, targetDamageable), critDamage));
        await RunTicks(3);

        // Check that the target is still dead.
        Assert.That(targetMobState.CurrentState, Is.EqualTo(MobState.Dead), "Target mob revived on its own.");

        // ZAP!
        await RunSeconds((float)cooldown.TotalSeconds);
        await Interact();

        // The target should be revived, but in crit.
        Assert.That(targetMobState.CurrentState, Is.EqualTo(MobState.Critical), "Target mob was not revived from being defibrillated.");
    }

    [Test]
    public async Task NeuroregenAllowsDefibrillationAfterBrainDeath()
    {
        await Server.WaitPost(() => Server.ResolveDependency<IConfigurationManager>().SetCVar(CVars.NetTickrate, 30));
        await AddAtmosphere();
        await SpawnTarget(TargetProtoId);
        var patient = STarget!.Value;
        var health = Comp<AdvancedHealthComponent>();
        var head = health.BodyParts[BodyPartSlot.Head];
        var state = Comp<MobStateComponent>();
        var organs = SEntMan.System<AdvancedOrganHealthSystem>();
        var brain = organs.Organs(patient).Single(organ => SEntMan.GetComponent<OrganComponent>(organ).Category?.Id == "Brain");

        await Server.WaitPost(() =>
        {
            organs.Damage(brain, 100);
            head.OrganIntegrity = 0;
            head.IsDestroyed = true;
            health.Oxygenation = 0;
            health.TimeBelowLethalOxygen = 60;
            health.HypoxiaExposure = 75;
            health.VascularTone = 0.2f;
            SEntMan.System<RespiratorSystem>().UpdateSaturation(patient, -10f);
            SEntMan.System<MobStateSystem>().ChangeMobState(patient, MobState.Dead);
        });

        var defib = await PlaceInHands(DefibrillatorProtoId);
        await RunSeconds((float) Comp<DefibrillatorComponent>(defib).ZapDelay.TotalSeconds);
        await Interact();
        Assert.That(state.CurrentState, Is.EqualTo(MobState.Dead));

        var vial = await Spawn("AdvancedMedicineVialNeuroregen");
        var syringe = ToServer(await PlaceInHands("Syringe"));
        await Interact(vial, TargetCoords);
        await Interact(vial, TargetCoords);
        await UseInHand();
        await Server.WaitAssertion(() =>
        {
            var injector = SEntMan.System<InjectorSystem>();
            Assert.That(injector.TryInjectTreatmentAmount(syringe, SPlayer, patient, 10, out var injected), Is.True);
            Assert.That(injected, Is.EqualTo(FixedPoint2.New(10)));
        });

        await RunSeconds(21);
        Assert.That(head.OrganFraction, Is.GreaterThan(0.5f));
        Assert.That(head.IsDestroyed, Is.False);
        Assert.That(brain.Comp.Integrity, Is.GreaterThan(50f));
        Assert.That(state.CurrentState, Is.EqualTo(MobState.Dead));

        defib = await PlaceInHands(DefibrillatorProtoId);
        await RunSeconds((float) Comp<DefibrillatorComponent>(defib).ZapDelay.TotalSeconds);
        await Interact();
        await RunSeconds(35);
        Assert.That(state.CurrentState, Is.EqualTo(MobState.Alive),
            $"Pulse {health.HeartRate}, SpO2 {health.Oxygenation}, perfusion {health.Perfusion}, consciousness {health.Consciousness}, arrest {health.ArrestTrigger}.");
        Assert.That(health.IsHeartStopped, Is.False);
    }
}
