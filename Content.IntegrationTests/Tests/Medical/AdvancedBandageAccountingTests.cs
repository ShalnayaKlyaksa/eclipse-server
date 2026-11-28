using System.Reflection;
using System.Linq;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class AdvancedBandageAccountingTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task HemostaticPowderReducesExternalBleedingOnTheSelectedPart()
    {
        var map = await Pair.CreateTestMap();
        EntityUid powder = default;
        var wound = new Wound { ExternalBleedingRate = 2, InternalBleedingRate = 1 };
        await Server.WaitAssertion(() =>
        {
            var medic = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            powder = SEntMan.SpawnEntity("AdvancedHemostaticPowder", map.MapCoords);
            SEntMan.GetComponent<AimTargetComponent>(medic).CurrentTarget = BodyPartTarget.LeftForearm;
            SEntMan.GetComponent<AdvancedHealthComponent>(patient).BodyParts[BodyPartSlot.LeftForearm].Wounds.Add(wound);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(medic, powder), Is.True);
            var use = new AfterInteractEvent(medic, powder, patient, map.GridCoords, true);
            SEntMan.EventBus.RaiseLocalEvent(powder, use);
            Assert.That(use.Handled, Is.True);
            Assert.That(wound.ExternalBleedingRate, Is.EqualTo(2));
        });

        await Pair.RunTicksSync(180);
        await Server.WaitAssertion(() =>
        {
            Assert.That(wound.ExternalBleedingRate, Is.EqualTo(.6f).Within(.001f));
            Assert.That(wound.InternalBleedingRate, Is.EqualTo(1));
            Assert.That(SEntMan.Deleted(powder), Is.True);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task ContaminatedDressingCanBeRemovedFromThePatientMenu()
    {
        var map = await Pair.CreateTestMap();
        EntityUid patient = default;
        var wound = new Wound { ExternalBleedingRate = 2f, InternalBleedingRate = 0.1f };
        await Server.WaitAssertion(() =>
        {
            var doctor = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var part = health.BodyParts[BodyPartSlot.LeftForearm];
            part.Wounds.Add(wound);
            wound.ApplyBandage(2f);
            part.RefreshBandageState();

            var disease = SEntMan.System<AdvancedDiseaseSystem>();
            var room = SEntMan.GetComponent<RoomSanitationComponent>(patient);
            room.Grade = 2;
            disease.Advance((patient, health), 90);
            Assert.That(part.IsBandageDirty, Is.False);
            room.Grade = 0;
            part.BandageExposure = 89;
            disease.Advance((patient, health), 1);
            Assert.That(part.IsBandageDirty, Is.True);
            Assert.That(wound.IsDirty, Is.True);
            Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.WoundCleaning), Is.False);

            var verbs = SEntMan.System<SharedVerbSystem>();
            var label = Loc.GetString("advanced-health-remove-dirty-bandage",
                ("part", Loc.GetString("advanced-health-part-leftforearm")));
            var remove = verbs.GetLocalVerbs(patient, doctor, typeof(Verb)).Single(verb => verb.Text == label);
            verbs.ExecuteVerb(remove, doctor, patient);
        });

        await Pair.RunTicksSync(150);
        await Server.WaitAssertion(() =>
        {
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var part = health.BodyParts[BodyPartSlot.LeftForearm];
            Assert.That(part.HasBandage, Is.False);
            Assert.That(part.IsBandageDirty, Is.False);
            Assert.That(wound.ExternalBleedingRate, Is.GreaterThan(0));
            Assert.That(wound.InternalBleedingRate, Is.EqualTo(0.1f));
            Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.WoundCleaning), Is.True);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task NeglectedDressingCanCauseWoundFeverAtItsOwnLocation()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var part = health.BodyParts[BodyPartSlot.RightForearm];
            var wound = new Wound { ExternalBleedingRate = 2 };
            part.Wounds.Add(wound);
            wound.ApplyBandage(2);
            part.RefreshBandageState();
            var disease = SEntMan.System<AdvancedDiseaseSystem>();
            disease.Advance((patient, health), 90);
            Assert.That(part.IsBandageDirty, Is.True);
            Assert.That(SEntMan.TryGetComponent<AdvancedDiseaseComponent>(patient, out var state)
                && state.Active.ContainsKey("WoundFever"), Is.False);
            disease.Advance((patient, health), 170);
            state = SEntMan.GetComponent<AdvancedDiseaseComponent>(patient);
            Assert.That(state.Active.ContainsKey("WoundFever"), Is.True);
            Assert.That(state.Locations["WoundFever"], Is.EqualTo(part.Slot));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [TestCase(AdvancedTreatmentType.Bandage, AdvancedBandageRollComponent.BleedPerPercent)]
    [TestCase(AdvancedTreatmentType.PressureBandage, AdvancedBandageRollComponent.BleedPerPercent * 1.6f)]
    public async Task PartialWrapSpendsOnlyAppliedPercentAndClampsToRollAndBleeding(
        AdvancedTreatmentType treatment, float litresPerPercent)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var part = health.BodyParts[BodyPartSlot.LeftForearm];
            part.Wounds.Clear();
            var wound = new Wound { ExternalBleedingRate = 0.5f / 0.06f, InternalBleedingRate = 2 };
            part.Wounds.Add(wound);
            var tool = SEntMan.SpawnEntity(null, map.MapCoords);
            var roll = SEntMan.EnsureComponent<AdvancedBandageRollComponent>(tool);
            roll.Durability = 12;
            var system = SEntMan.System<AdvancedTreatmentSystem>();
            // Exercise the actual server accounting independently of the client's quality report and work cost.
            var apply = typeof(AdvancedTreatmentSystem).GetMethod("ApplyBandageSegments", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(apply, Is.Not.Null, "The server bandage accounting method must be available.");
            bool Apply(int percent) => (bool) apply.Invoke(system,
                new object[] { patient, patient, tool, part.Slot, part, treatment, percent, false })!;

            Assert.That(Apply(7), Is.True);
            Assert.That(roll.Durability, Is.EqualTo(5));
            Assert.That(wound.ExternalBleedingRate * 0.06f, Is.EqualTo(0.5f - 7 * litresPerPercent).Within(0.0001f));
            Assert.That(wound.InternalBleedingRate, Is.EqualTo(2), "Wrapping cannot treat internal bleeding.");
            Assert.That(Apply(100), Is.True);
            Assert.That(roll.Durability, Is.Zero, "A client cannot apply more than the remaining roll.");
            Assert.That(wound.ExternalBleedingRate * 0.06f, Is.EqualTo(0.5f - 12 * litresPerPercent).Within(0.0001f));

            // A nearly closed wound must not consume a full roll, even if the client asks for one.
            tool = SEntMan.SpawnEntity(null, map.MapCoords);
            roll = SEntMan.EnsureComponent<AdvancedBandageRollComponent>(tool);
            roll.Durability = 100;
            wound.ExternalBleedingRate = litresPerPercent * 1.5f / 0.06f;
            Assert.That(Apply(100), Is.True);
            Assert.That(roll.Durability, Is.EqualTo(98));
            Assert.That(wound.ExternalBleedingRate, Is.Zero.Within(0.0001f));
            Assert.That(Apply(1), Is.False);
            Assert.That(roll.Durability, Is.EqualTo(98));
        });
    }
}
