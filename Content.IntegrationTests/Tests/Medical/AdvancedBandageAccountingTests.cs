using System.Reflection;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.AdvancedHealth;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class AdvancedBandageAccountingTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestCase(AdvancedTreatmentType.Bandage, 0.01f)]
    [TestCase(AdvancedTreatmentType.PressureBandage, 0.016f)]
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
