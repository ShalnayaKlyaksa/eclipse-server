using Content.IntegrationTests.Fixtures;
using System.Linq;
using System.Globalization;
using Robust.Shared.Localization;
using System.Numerics;
using System.Reflection;
using Content.Client._Eclipse.AdvancedHealth;
using Content.Client.HealthAnalyzer.UI;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Damage;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.GameObjects;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class AdvancedMedicineTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestPrototypes]
    private const string Conditions = """
- type: medicalCondition
  id: MedicalTestNerveBlock
  name: advanced-health-chip-function
  description: advanced-health-chip-function-tooltip
  functionMultiplier: 0
""";

    [Test]
    public async Task LimbFunctionControlsHandsMovementAndExtensions()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var functions = SEntMan.System<SharedMedicalConditionSystem>();
            Assert.That(health.BodyParts[BodyPartSlot.LeftHand].TissueFraction, Is.EqualTo(1f).Within(0.001f));

            var forearm = health.BodyParts[BodyPartSlot.LeftForearm];
            forearm.NerveIntegrity = 0;
            Assert.That(functions.GetArmFunction(health, HandLocation.Left), Is.Zero,
                "Healthy distal tissues cannot compensate for a severed proximal nerve.");
            var hands = SEntMan.System<SharedHandsSystem>();
            var leftHand = hands.EnumerateHands(patient).First(name => hands.TryGetHand(patient, name, out var hand)
                && hand.Value.Location == HandLocation.Left);
            var rightHand = hands.EnumerateHands(patient).First(name => hands.TryGetHand(patient, name, out var hand)
                && hand.Value.Location == HandLocation.Right);
            var item = SEntMan.SpawnEntity("Crowbar", map.MapCoords);
            Assert.That(hands.CanPickupToHand(patient, item, leftHand), Is.False);
            Assert.That(hands.CanPickupToHand(patient, item, rightHand), Is.True);

            forearm.NerveIntegrity = forearm.MaxNerveIntegrity;
            Assert.That(functions.AddCondition((patient, health), BodyPartSlot.RightHand, "MedicalTestNerveBlock"), Is.True);
            Assert.That(functions.GetArmFunction(health, HandLocation.Right), Is.Zero);
            Assert.That(hands.CanPickupToHand(patient, item, rightHand), Is.False);
            Assert.That(functions.RemoveCondition((patient, health), BodyPartSlot.RightHand, "MedicalTestNerveBlock"), Is.True);
            Assert.That(hands.CanPickupToHand(patient, item, rightHand), Is.True);

            health.BodyParts[BodyPartSlot.LeftShin].MuscleIntegrity = 0;
            var speed = new RefreshMovementSpeedModifiersEvent();
            SEntMan.EventBus.RaiseLocalEvent(patient, speed);
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(speed.SprintSpeedModifier, Is.EqualTo(0.5f).Within(0.001f));
        });
    }

    [Test]
    public async Task PhysiologyOwnsMobStateInsteadOfLegacyDamageThresholds()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var state = SEntMan.GetComponent<MobStateComponent>(patient);
            SEntMan.System<MobThresholdSystem>().SetMobStateThreshold(patient, 0, MobState.Dead);
            Assert.That(state.CurrentState, Is.EqualTo(MobState.Alive), "Legacy HP must not kill an advanced patient.");
            health.Oxygenation = 0;
            SEntMan.System<MobStateSystem>().UpdateMobState(patient);
            Assert.That(state.CurrentState, Is.EqualTo(MobState.Critical));
            health.IsHeartStopped = true;
            health.TimeSinceHeartStopped = health.HeartStoppedDeathTime;
            SEntMan.System<MobStateSystem>().UpdateMobState(patient);
            Assert.That(state.CurrentState, Is.EqualTo(MobState.Dead));
        });
    }

    [Test]
    public async Task MedicationRepairsProtectedTissueWithoutMendingFractures()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var part = health.BodyParts[BodyPartSlot.LeftForearm];
            part.SkinIntegrity = 60f;
            part.BoneIntegrity = 40f;
            var wound = new Wound { Type = WoundType.Gunshot, BodyPart = part.Slot };
            part.Wounds.Add(wound);
            part.Wounds.Add(new Wound { Type = WoundType.Fracture, BodyPart = part.Slot });
            var medicine = SEntMan.System<SharedAdvancedMedicineSystem>();
            var dose = new DamageSpecifier { DamageDict = new() { ["Blunt"] = -10 } };
            medicine.ApplyDose((patient, health), dose, true);
            Assert.That(part.SkinIntegrity, Is.EqualTo(60f), "Unprotected open wounds must be treated first.");
            wound.IsBandaged = true;
            medicine.ApplyDose((patient, health), dose, true);
            Assert.That(part.SkinIntegrity, Is.GreaterThan(60f));
            Assert.That(part.BoneIntegrity, Is.EqualTo(40f));
            Assert.That(part.Wounds.Exists(entry => entry.Type == WoundType.Fracture), Is.True);

            health.ToxinLoad = 50f;
            medicine.ApplyDose((patient, health), new DamageSpecifier { DamageDict = new() { ["Poison"] = -10 } }, true);
            Assert.That(health.ToxinLoad, Is.EqualTo(40f), "Antidotes must affect physiology even when the legacy ledger is empty.");

            var rejuvenate = new RejuvenateEvent();
            SEntMan.EventBus.RaiseLocalEvent(patient, rejuvenate);
            Assert.That(part.Wounds, Is.Empty);
            Assert.That(part.BoneIntegrity, Is.EqualTo(part.MaxBoneIntegrity));
            Assert.That(health.ToxinLoad, Is.Zero);
        });
    }

    [Test]
    public void TourniquetAndVentilationReadoutsUseThePhysiologyModel()
    {
        var health = new AdvancedHealthComponent();
        var part = new BodyPartState { Slot = BodyPartSlot.LeftForearm, HasTourniquet = true };
        part.Wounds.Add(new Wound { ExternalBleedingRate = 10f, InternalBleedingRate = 2f });
        Assert.That(health.GetBloodLossRate(part), Is.EqualTo(2.3f).Within(0.001f));
        Assert.That(health.MinuteVentilation, Is.EqualTo(7f));
        health.IsSuffocating = true;
        Assert.That(health.MinuteVentilation, Is.Zero);
        var internalInjury = new BodyPartState { Slot = BodyPartSlot.Chest };
        internalInjury.Wounds.Add(new Wound { Type = WoundType.OrganDamage, InternalBleedingRate = 1f });
        Assert.That(AdvancedTreatmentRules.IsNeeded(internalInjury, AdvancedTreatmentType.Bandage), Is.False);
        Assert.That(AdvancedTreatmentRules.IsNeeded(internalInjury, AdvancedTreatmentType.Suture), Is.False);
    }

    [TestCase(BodyPartSlot.Abdomen, BodyPartSlot.Pelvis, 62)]
    [TestCase(BodyPartSlot.Head, BodyPartSlot.Neck, 31)]
    public async Task ChildInjuriesShareTheParentMapWithoutChangingAnatomy(BodyPartSlot parent, BodyPartSlot child, int y)
    {
        await Client.WaitAssertion(() =>
        {
            var abdomen = new BodyPartState { Slot = parent };
            var pelvis = new BodyPartState { Slot = child };
            pelvis.Wounds.Add(new Wound { Type = WoundType.Fracture, Severity = WoundSeverity.Severe });
            using var doll = new AdvancedHealthBodyDollControl { GroupPelvisWithAbdomen = true, GroupNeckWithHead = true };
            doll.SetState(new[] { abdomen.ToUiState(), pelvis.ToUiState() });
            doll.Measure(new Vector2(128));
            doll.Arrange(new UIBox2(Vector2.Zero, new Vector2(128)));
            var hit = typeof(AdvancedHealthBodyDollControl).GetMethod("TryHitTest", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(doll, new object[] { new Vector2(64, y) });
            Assert.That(hit, Is.EqualTo(parent));
            var states = (System.Collections.Generic.Dictionary<BodyPartSlot, BodyPartUiState>) typeof(AdvancedHealthBodyDollControl)
                .GetField("_states", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(doll)!;
            Assert.That(states.ContainsKey(child), Is.False);
            Assert.That(states[parent].Fractured, Is.True);
            Assert.That(pelvis.Slot, Is.EqualTo(child));
            Assert.That(abdomen.Wounds, Is.Empty, "Grouping is a presentation choice; wounds keep their anatomical owner.");
        });
    }

    [TestCase(1280, 720, "en-US")]
    [TestCase(1280, 720, "ru-RU")]
    [TestCase(1920, 1080, "ru-RU")]
    [TestCase(2560, 1387, "ru-RU")]
    public async Task ChipColumnsFitTheViewport(int width, int height, string culture)
    {
        var map = await Pair.CreateTestMap();
        NetEntity patient = default;
        await Server.WaitPost(() =>
        {
            var entity = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            patient = SEntMan.GetNetEntity(entity);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(entity);
            var chest = health.BodyParts[BodyPartSlot.Chest];
            chest.SkinIntegrity = 55f;
            chest.MuscleIntegrity = 65f;
            chest.IsBandaged = chest.IsSplinted = chest.IsBleeding = true;
            chest.ForeignBodyCount = 1;
            chest.Wounds.Add(new Wound
            {
                Type = WoundType.Gunshot, Severity = WoundSeverity.Severe, BodyPart = BodyPartSlot.Chest,
                ExternalBleedingRate = 0.2f, InternalBleedingRate = 0.1f, IsBandaged = true, HasForeignBody = true,
            });
            chest.Wounds.Add(new Wound { Type = WoundType.Fracture, Severity = WoundSeverity.Severe });
        });
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            Client.ResolveDependency<ILocalizationManager>().SetCulture(CultureInfo.GetCultureInfo(culture));
            using var window = new AdvancedHealthStatusWindow(CEntMan.GetEntity(patient));
            // Exercise the same layout rebuild used by the native resize event, without resizing the host desktop.
            typeof(AdvancedHealthStatusWindow).GetMethod("BuildLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, new object[] { new Vector2i(width, height) });
            var size = new Vector2(width, height);
            window.Measure(size);
            window.Arrange(new UIBox2(Vector2.Zero, size));
            var columns = window.Children.OfType<HealthChipColumns>().Single();
            var panels = columns.Children.Where(child => child.Name is "MedicalChipVitals" or "MedicalChipBody" or "MedicalChipDetails").ToArray();
            var body = panels.Single(panel => panel.Name == "MedicalChipBody");
            var leftDivider = columns.Children.Single(child => child.Name == "MedicalChipLeftDivider");
            var rightDivider = columns.Children.Single(child => child.Name == "MedicalChipRightDivider");
            Assert.That(leftDivider.Position.X + leftDivider.Size.X, Is.LessThan(body.Position.X));
            Assert.That(rightDivider.Position.X, Is.GreaterThan(body.Position.X + body.Size.X));
            Assert.That(rightDivider.Position.X + rightDivider.Size.X,
                Is.LessThan(panels.Single(panel => panel.Name == "MedicalChipDetails").Position.X));
            Assert.That(Vector2.Distance(leftDivider.Size, rightDivider.Size), Is.LessThan(0.01f), "Both dividers must match.");
            Assert.That(columns.Position.Y, Is.LessThanOrEqualTo(16), "No reserved empty header strip.");
            var vitals = panels.Single(panel => panel.Name == "MedicalChipVitals");
            var readouts = vitals.Children.OfType<ScrollContainer>().Single();
            Assert.That(readouts.HScrollEnabled, Is.False);
            var diagnosticStack = readouts.Children.OfType<BoxContainer>().Single();
            var summary = diagnosticStack.Children.Single(child => child.Name == "MedicalChipSummary");
            Assert.That(summary.Size.Y, Is.EqualTo(summary.DesiredSize.Y).Within(1),
                "Summary must fit its contents, not stretch down the screen.");
            var patientCard = vitals.Children.Single(child => child.Name == "MedicalChipPatient");
            Assert.That(patientCard.Position.Y, Is.LessThan(2), "Patient header starts the left column.");
            foreach (var child in vitals.Children.Where(child => child.Visible))
            {
                Assert.That(child.Position.Y + child.Size.Y, Is.LessThanOrEqualTo(vitals.Size.Y + 1),
                    $"Left column overflow: {child.Name}, pos={child.Position}, size={child.Size}, parent={vitals.Size}");
                Assert.That(child.Position.X + child.Size.X, Is.LessThanOrEqualTo(vitals.Size.X + 1),
                    $"Left column too wide: {child.Name}");
            }
            AssertReadableWidths(vitals);
            var details = panels.Single(panel => panel.Name == "MedicalChipDetails");
            var zone = details.Children.Single();
            var conditions = zone.Children.OfType<ScrollContainer>().Single();
            Assert.That(conditions.HScrollEnabled, Is.False, "Condition text must wrap within the panel.");
            var conditionList = conditions.Children.OfType<BoxContainer>().Single();
            foreach (var card in conditionList.Children)
                Assert.That(card.Size.X, Is.LessThanOrEqualTo(conditions.Size.X + 1),
                    "Condition cards must stay inside the right column.");
            var layout = string.Join("; ", columns.Children.Select(child => $"{child.Name}: pos={child.Position} size={child.Size} desired={child.DesiredSize} expand={child.HorizontalExpand}"));
            Assert.That(panels.Length, Is.EqualTo(3));
            foreach (var panel in panels)
            {
                Assert.That(panel.Size.X, Is.GreaterThan(100));
                Assert.That(panel.Position.X + panel.Size.X, Is.LessThanOrEqualTo(columns.Size.X + 1), layout);
                Assert.That(panel.Size.Y, Is.LessThanOrEqualTo(columns.Size.Y + 1), panel.Name);
            }
        });
    }
    private static void AssertReadableWidths(Control parent)
    {
        foreach (var child in parent.Children.Where(child => child.Visible))
        {
            if (parent is BoxContainer or GridContainer)
                Assert.That(child.Position.X + child.Size.X, Is.LessThanOrEqualTo(parent.Size.X + 1),
                    $"Readout exceeds its row: {child.GetType().Name} '{child.Name}', parent={parent.GetType().Name}, pos={child.Position}, size={child.Size}, available={parent.Size}");
            AssertReadableWidths(child);
        }
    }

}
