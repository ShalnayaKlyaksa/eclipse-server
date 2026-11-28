using Content.IntegrationTests.Fixtures;
using System.Collections.Generic;
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
using Content.Shared.Armor;
using Content.Shared.Inventory;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Shared.Containers;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Movement.Components;
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
            var movement = SEntMan.GetComponent<MovementSpeedModifierComponent>(patient);
            Assert.That(speed.WalkSpeedModifier * movement.BaseWalkSpeed, Is.EqualTo(0.6f * movement.BaseSprintSpeed).Within(0.001f));
            Assert.That(speed.SprintSpeedModifier, Is.EqualTo(0.5f * 1.25f).Within(0.001f));
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
            Assert.That(health.Consciousness, Is.Zero);
            Assert.That(health.Perfusion, Is.Zero);

            SEntMan.System<Content.Shared.Administration.Systems.RejuvenateSystem>().PerformRejuvenate(patient);
            Assert.That(state.CurrentState, Is.EqualTo(MobState.Alive));
            Assert.That(health.Consciousness, Is.EqualTo(100));
            Assert.That(health.IsHeartStopped, Is.False);
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
            var nutrition = SEntMan.EnsureComponent<AdvancedNutritionComponent>(entity);
            nutrition.Malnutrition = 0.7f;
            nutrition.DehydrationStrain = 0.8f;
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
            Assert.That(Descendants(vitals).Single(control => control.Name == "MedicalChipRadiation").Visible,
                Is.True, "Radiation must remain visible at zero exposure.");
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
            foreach (var mode in Enum.GetValues<MedicalBodyViewMode>())
            {
                window.SelectView(mode);
                window.Measure(size);
                window.Arrange(new UIBox2(Vector2.Zero, size));
                var controls = Descendants(body).ToArray();
                var modes = controls.Single(control => control.Name == "MedicalChipViewModes");
                Assert.That(modes.Children.OfType<Button>().Count(), Is.EqualTo(3), "Fractures belong in overview, not a separate bone mode.");
                var doll = controls.OfType<AdvancedHealthBodyDollControl>().Single();
                Assert.That(modes.Children.OfType<Button>().Count(button => button.Pressed), Is.EqualTo(1));
                Assert.That(doll.ViewMode, Is.EqualTo(mode));
                Assert.That(modes.GlobalPosition.X + modes.Size.X, Is.LessThanOrEqualTo(doll.GlobalPosition.X + 1));
                Assert.That(doll.Size.X, Is.GreaterThan(100), "Layer selector must leave room for anatomy.");
                foreach (var image in doll.Children.OfType<TextureRect>().Where(image => image.Visible))
                {
                    Assert.That(image.Texture.Width, Is.EqualTo(48));
                    Assert.That(image.Texture.Height, Is.EqualTo(48));
                    Assert.That(image.Size.X, Is.EqualTo(image.Size.Y).Within(0.01), "Organ images must scale uniformly.");
                }
                AssertReadableWidths(vitals);
            }
        });
    }

    [Test]
    public async Task ChipLayersKeepFracturesInOverviewAndShowRadiationOrgansAndArmor()
    {
        var map = await Pair.CreateTestMap();
        NetEntity patient = default;
        float piercingProtection = 0;
        await Server.WaitAssertion(() =>
        {
            var entity = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            patient = SEntMan.GetNetEntity(entity);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(entity);
            health.BodyParts[BodyPartSlot.Neck].BoneIntegrity = 25;
            health.BodyParts[BodyPartSlot.Neck].Wounds.Add(new Wound
            {
                BodyPart = BodyPartSlot.Neck, Type = WoundType.Fracture, Severity = WoundSeverity.Moderate,
            });
            health.BodyParts[BodyPartSlot.Chest].OrganIntegrity = 46;
            var irradiation = new DamageSpecifier();
            irradiation.DamageDict["Radiation"] = FixedPoint2.New(35);
            SEntMan.System<DamageableSystem>().TryChangeDamage(entity, irradiation, ignoreResistances: true);
            Assert.That(health.RadiationLoad, Is.EqualTo(35), "Radiation damage must reach advanced physiology.");
            var vest = SEntMan.SpawnEntity("ClothingOuterArmorBulletproof", map.MapCoords);
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(entity, vest, "outerClothing"), Is.True);
            var query = new CoefficientQueryEvent(~SlotFlags.POCKET);
            SEntMan.EventBus.RaiseLocalEvent(entity, query);
            piercingProtection = 1 - query.DamageModifiers.Coefficients.GetValueOrDefault("Piercing", 1);
            Assert.That(piercingProtection, Is.GreaterThan(0));
        });
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var local = CEntMan.GetEntity(patient);
            var health = CEntMan.GetComponent<AdvancedHealthComponent>(local);
            using var window = new AdvancedHealthStatusWindow(local);
            var doll = Descendants(window).OfType<AdvancedHealthBodyDollControl>().Single();
            typeof(AdvancedHealthStatusWindow).GetMethod("OnPartSelected", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, new object[] { BodyPartSlot.Head });
            var conditions = (List<(string Key, string Text, string Description, Color Color)>) typeof(AdvancedHealthStatusWindow)
                .GetField("_conditionRows", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.That(conditions.Any(row => row.Text.Contains(Loc.GetString("advanced-health-wound-fracture"))),
                Is.True, "Neck fractures must appear under the head in overview.");
            var radiation = (HealthMenuProgressBar) typeof(AdvancedHealthStatusWindow)
                .GetField("_radBar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.That(radiation.Fraction, Is.EqualTo(0.35f).Within(0.001));
            window.SelectView(MedicalBodyViewMode.Organs);
            Assert.That(doll.GetLayerValue(BodyPartSlot.Chest), Is.Null, "Organ mode must not use regional tissue values.");
            var organs = (List<MedicalOrganMarker>) typeof(AdvancedHealthBodyDollControl)
                .GetField("_organs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(doll)!;
            Assert.That(organs.Any(organ => organ.Category == "Heart"), Is.True, "Organ view must render actual installed organs.");
            var heart = organs.Single(organ => organ.Category == "Heart");
            window.SelectOrgan(heart.Entity);
            Assert.That(window.SelectedOrgan, Is.EqualTo(heart.Entity));
            Assert.That(doll.SelectedOrgan, Is.EqualTo(heart.Entity));
            Assert.That(doll.SelectedSlot, Is.EqualTo(BodyPartSlot.Head), "Organ selection must not change the selected body part.");
            var hit = typeof(AdvancedHealthBodyDollControl).GetMethod("TryHitTest", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(hit.Invoke(doll, new object[] { doll.GetSlotCentroidLocal(BodyPartSlot.Chest) }), Is.Null,
                "The organ layer must not expose body-part hit targets.");
            var images = doll.Children.OfType<TextureRect>().Where(view => view.Visible).ToArray();
            Assert.That(images.Length, Is.EqualTo(organs.Sum(organ => organ.Category switch
            {
                "Ears" => 0,
                "Eyes" or "Lungs" or "Kidneys" => 2,
                _ => 1,
            })));
            Assert.That(Descendants(window).Any(control => control.Name == "MedicalChipOrganPicker"), Is.False,
                "Organs are selected on the map, without a duplicate list.");
            var heartImage = images.Single(image => image.Name == "MedicalOrgan_heart");
            Assert.That(Array.IndexOf(images, heartImage), Is.GreaterThan(Array.IndexOf(images, images.First(image => image.Name == "MedicalOrgan_lung_left"))),
                "The heart must receive overlapping clicks before the lungs.");
            heartImage.SetSize = new Vector2(48, 48);
            heartImage.Measure(new Vector2(48, 48));
            heartImage.Arrange(new UIBox2(0, 0, 48, 48));
            var alphaHit = typeof(MedicalOrganTexture).GetMethod("HasPoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(alphaHit.Invoke(heartImage, new object[] { Vector2.Zero }), Is.False,
                "Transparent organ corners must let clicks pass through.");
            Assert.That(alphaHit.Invoke(heartImage, new object[] { new Vector2(24, 24) }), Is.True);
            foreach (var button in Descendants(window).OfType<Button>().Where(button => button.Name?.StartsWith("MedicalChipMode") == true))
            {
                Assert.That(button.Text, Is.Empty);
                Assert.That(button.ToolTip, Is.Not.Empty);
                Assert.That(button.Children.Any(child => child is MedicalViewIcon), Is.True);
            }
            Assert.That(images.Any(image => image.Name == "MedicalOrgan_eye_right_mirrored"), Is.True);
            Assert.That(images.Any(image => image.Name is "MedicalOrgan_eye_left" or "MedicalOrgan_ear_left" or "MedicalOrgan_ear_right"), Is.False);
            Assert.That(images.Any(image => image.Name == "MedicalOrgan_intestines"), Is.True);
            Assert.That(images.All(image => image.Texture.Width == 48 && image.Texture.Height == 48), Is.True);
            window.SelectView(MedicalBodyViewMode.Protection);
            Assert.That(doll.Children.OfType<TextureRect>().Any(view => view.Visible), Is.False, "Organ sprites must not leak into other layers.");
            Assert.That(doll.GetLayerValue(BodyPartSlot.Chest), Is.EqualTo(piercingProtection).Within(0.001));
            Assert.That(doll.GetLayerValue(BodyPartSlot.LeftHand), Is.EqualTo(piercingProtection).Within(0.001),
                "Current global armour must not be misrepresented as regional resistance.");
            window.SelectView(MedicalBodyViewMode.General);
            Assert.That(doll.SelectedSlot, Is.EqualTo(BodyPartSlot.Head));
        });
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    [Test]
    public async Task HumanAnatomyFollowsSexWithoutDuplicatingOrgansOnProfileUpdates()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var human = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var containers = SEntMan.System<SharedContainerSystem>();
            Assert.That(containers.TryGetContainer(human, BodyComponent.ContainerID, out var body), Is.True);
            string[] Categories() => body!.ContainedEntities
                .Where(entity => SEntMan.HasComponent<OrganComponent>(entity))
                .Select(entity => SEntMan.GetComponent<OrganComponent>(entity).Category?.Id).ToArray();
            Assert.That(Categories().Count(category => category == "Intestines"), Is.EqualTo(1));
            Assert.That(Categories(), Does.Contain("Prostate"));
            var profiles = SEntMan.System<HumanoidProfileSystem>();
            var female = new HumanoidCharacterProfile().WithSex(Sex.Female);
            profiles.ApplyProfileTo(human, female);
            Assert.That(Categories(), Does.Contain("Uterus"));
            Assert.That(Categories(), Does.Contain("OvaryLeft"));
            Assert.That(Categories(), Does.Contain("OvaryRight"));
            Assert.That(Categories(), Does.Contain("VaginalCanal"));
            Assert.That(Categories(), Does.Not.Contain("Prostate"));
            var count = body!.ContainedEntities.Count;
            profiles.ApplyProfileTo(human, female.WithAge(35));
            Assert.That(body.ContainedEntities.Count, Is.EqualTo(count), "Applying the same anatomy must not restore or duplicate organs.");
            profiles.ApplyProfileTo(human, female.WithSex(Sex.Unsexed));
            Assert.That(Categories(), Does.Not.Contain("Uterus"));
            Assert.That(Categories(), Does.Not.Contain("Prostate"));
            Assert.That(Categories().Count(category => category == "Intestines"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task OtherHumanoidSpeciesReceiveAnatomyWithoutAddingItToIncompatibleBodies()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var containers = SEntMan.System<SharedContainerSystem>();
            var profiles = SEntMan.System<HumanoidProfileSystem>();
            var cases = new (string Prototype, bool Intestines, bool Reproductive)[]
            {
                ("MobHuman", true, true), ("MobDwarf", true, true),
                ("MobVulpkanin", true, true), ("MobTajaran", true, true),
                ("MobReptilian", true, true), ("MobMoth", true, true),
                ("MobAnthropomorph", true, true), ("MobAvian", true, true),
                ("MobArkane", true, true), ("MobShadowkin", true, true),
                ("MobImperial", true, true), ("MobMolei", true, true),
                ("MobLavrite", true, true), ("MobEclipseKobold", true, true),
                ("MobSaurian", true, true), ("MobTherian", true, true),
                ("MobElir", true, true), ("MobGingerbread", true, true),
                ("MobVox", true, false), ("MobArachnid", true, false),
                ("MobSlimePerson", false, false), ("MobSlimefolk", false, false),
                ("MobDiona", false, false), ("MobIpc", false, false),
                ("MobSkeletonPerson", false, false),
            };
            foreach (var (prototype, intestines, reproductive) in cases)
            {
                var mob = SEntMan.SpawnEntity(prototype, map.MapCoords);
                Assert.That(containers.TryGetContainer(mob, BodyComponent.ContainerID, out var body), Is.True, prototype);
                string[] Categories() => body!.ContainedEntities
                    .Where(entity => SEntMan.HasComponent<OrganComponent>(entity))
                    .Select(entity => SEntMan.GetComponent<OrganComponent>(entity).Category?.Id).ToArray();
                Assert.That(Categories().Count(category => category == "Intestines"), Is.EqualTo(intestines ? 1 : 0), prototype);
                Assert.That(Categories().Contains("Prostate"), Is.EqualTo(reproductive), prototype);
                var species = SEntMan.GetComponent<HumanoidProfileComponent>(mob).Species;
                var female = new HumanoidCharacterProfile().WithSpecies(species).WithSex(Sex.Female);
                profiles.ApplyProfileTo(mob, female);
                foreach (var category in new[] { "Uterus", "OvaryLeft", "OvaryRight", "VaginalCanal" })
                    Assert.That(Categories().Contains(category), Is.EqualTo(reproductive), prototype + ": " + category);
                Assert.That(Categories(), Does.Not.Contain("Prostate"), prototype);
                var count = body!.ContainedEntities.Count;
                profiles.ApplyProfileTo(mob, female.WithAge(35));
                Assert.That(body.ContainedEntities.Count, Is.EqualTo(count), prototype + ": repeated profile duplicated anatomy");
                if (intestines)
                {
                    var intestine = body.ContainedEntities.Single(entity => SEntMan.TryGetComponent<OrganComponent>(entity, out var organ)
                        && organ.Category?.Id == "Intestines");
                    containers.Remove(intestine, body);
                    profiles.ApplyProfileTo(mob, female.WithAge(36));
                    Assert.That(Categories(), Does.Not.Contain("Intestines"), prototype + ": profile restored a removed organ");
                    SEntMan.DeleteEntity(intestine);
                }
                SEntMan.DeleteEntity(mob);
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
