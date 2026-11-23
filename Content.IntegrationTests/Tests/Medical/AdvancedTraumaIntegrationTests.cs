using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class AdvancedTraumaIntegrationTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestCase(false)]
    [TestCase(true)]
    public async Task IncomingTraumaNeverMergesIntoTheSurgicalIncision(bool atWoundLimit)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            health.BodyPartHitWeights.Clear();
            health.BodyPartHitWeights[BodyPartSlot.LeftForearm] = 1;
            var part = health.BodyParts[BodyPartSlot.LeftForearm];
            part.IsSurgicalOpen = true;
            var incision = new Wound
            {
                Type = WoundType.Cut, Severity = WoundSeverity.Minor,
                IsSurgicalIncision = true, ExternalBleedingRate = 0.3f,
            };
            part.Wounds.Add(incision);
            if (atWoundLimit)
                for (var i = 0; i < 12; i++)
                    part.Wounds.Add(new Wound { Type = WoundType.Bruise });
            var slash = new DamageSpecifier();
            slash.DamageDict["Slash"] = FixedPoint2.New(8);
            SEntMan.System<DamageableSystem>().TryChangeDamage(patient, slash, ignoreResistances: true);
            Assert.That(incision.ExternalBleedingRate, Is.EqualTo(0.3f));
            Assert.That(incision.StackCount, Is.EqualTo(1));
            Assert.That(part.Wounds.Where(wound => !wound.IsSurgicalIncision)
                .Sum(wound => wound.ExternalBleedingRate), Is.GreaterThan(0));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task TraumaCycleUsesAuthoritativeTimedActionsAndPreservesReusableTools()
    {
        var map = await Pair.CreateTestMap();
        EntityUid doctor = default, patient = default;
        BodyPartState part = null;
        await Server.WaitAssertion(() =>
        {
            doctor = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            part = SEntMan.GetComponent<AdvancedHealthComponent>(patient).BodyParts[BodyPartSlot.LeftForearm];
            part.BoneIntegrity = 30;
            part.Wounds.Add(new Wound { Type = WoundType.Fracture, Severity = WoundSeverity.Severe });
            part.Wounds.Add(new Wound { Type = WoundType.Cut, IsDirty = true, InternalBleedingRate = 0.2f });
        });

        async Task Use(string prototype, bool starts = true, bool cancel = false)
        {
            EntityUid tool = default;
            var reusable = false;
            await Server.WaitAssertion(() =>
            {
                tool = SEntMan.SpawnEntity(prototype, map.MapCoords);
                var treatment = SEntMan.GetComponent<AdvancedTreatmentComponent>(tool);
                Assert.That(treatment.Delay, Is.GreaterThan(0), "Production prototypes must have a real server delay.");
                reusable = treatment.Reusable;
                treatment.Delay = 0.1f;
                Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(doctor, tool), Is.True);
                SEntMan.EnsureComponent<AimTargetComponent>(doctor).CurrentTarget = BodyPartTarget.LeftForearm;
                var interaction = new AfterInteractEvent(doctor, tool, patient,
                    SEntMan.GetComponent<TransformComponent>(patient).Coordinates, true);
                SEntMan.EventBus.RaiseLocalEvent(tool, interaction);
                Assert.That(interaction.Handled, Is.EqualTo(starts));
                Assert.That(SEntMan.Deleted(tool), Is.False, "Supplies cannot be consumed before the action completes.");
                SEntMan.GetComponent<AimTargetComponent>(doctor).CurrentTarget = BodyPartTarget.RightForearm;
                if (cancel)
                    Assert.That(SEntMan.System<SharedHandsSystem>().TryDrop(doctor, tool), Is.True);
            });
            await Pair.RunTicksSync(25);
            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.Deleted(tool), Is.EqualTo(starts && !cancel && !reusable));
                if (!SEntMan.Deleted(tool))
                    SEntMan.DeleteEntity(tool);
            });
        }

        await Use("Scalpel", starts: false);
        await Use("AdvancedWoundIrrigationKit", cancel: true);
        await Server.WaitAssertion(() => Assert.That(part.IsSurgicalSitePrepared, Is.False));
        await Use("AdvancedWoundIrrigationKit");
        await Server.WaitAssertion(() =>
        {
            Assert.That(part.IsSurgicalSitePrepared, Is.True);
            Assert.That(part.Wounds.Any(wound => wound.IsDirty), Is.False);
            var right = SEntMan.GetComponent<AdvancedHealthComponent>(patient).BodyParts[BodyPartSlot.RightForearm];
            Assert.That(right.IsSurgicalSitePrepared, Is.False, "Changing aim during work must not retarget the procedure.");
        });
        await Use("AdvancedRegionalAnesthetic");
        await Use("Scalpel");
        await Server.WaitAssertion(() => Assert.That(part.IsSurgicalOpen, Is.True));
        await Use("AdvancedSurgicalClosureKit", starts: false);
        await Use("AdvancedBoneFixationKit", starts: false);
        await Use("Hemostat");
        await Server.WaitAssertion(() => Assert.That(part.Wounds.Sum(wound => wound.InternalBleedingRate), Is.Zero));
        await Use("AdvancedBoneFixationKit");
        await Server.WaitAssertion(() =>
        {
            Assert.That(part.Wounds.Single(wound => wound.Type == WoundType.Fracture).IsBoneFixed, Is.True);
            Assert.That(part.BoneIntegrity, Is.EqualTo(30), "Fixation is not instantaneous tissue regeneration.");
        });
        await Use("AdvancedSurgicalClosureKit");
        await Server.WaitAssertion(() =>
        {
            Assert.That(part.IsSurgicalOpen, Is.False);
            Assert.That(part.Wounds.Single(wound => wound.IsSurgicalIncision).IsSutured, Is.True);
            Assert.That(part.Wounds.Single(wound => wound.Type == WoundType.Fracture).HealingProgress, Is.LessThan(1));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
