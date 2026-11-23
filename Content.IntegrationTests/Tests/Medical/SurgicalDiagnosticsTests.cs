using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Paper;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class SurgicalDiagnosticsTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestCase(false)]
    [TestCase(true)]
    public async Task XRayRequiresCompletionWithTheHeldScannerAndKeepsItsOriginalRegion(bool cancel)
    {
        var map = await Pair.CreateTestMap();
        EntityUid doctor = default, patient = default, scanner = default;
        await Server.WaitAssertion(() =>
        {
            doctor = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            scanner = SEntMan.SpawnEntity("AdvancedPortableXRay", map.MapCoords);
            SEntMan.GetComponent<AdvancedTreatmentComponent>(scanner).Delay = 0.1f;
            SEntMan.EnsureComponent<AimTargetComponent>(doctor).CurrentTarget = BodyPartTarget.LeftForearm;
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(doctor, scanner), Is.True);
            var interaction = new AfterInteractEvent(doctor, scanner, patient,
                SEntMan.GetComponent<TransformComponent>(patient).Coordinates, true);
            SEntMan.EventBus.RaiseLocalEvent(scanner, interaction);
            Assert.That(interaction.Handled, Is.True);
            Assert.That(SEntMan.HasComponent<MedicalDiagnosticHistoryComponent>(patient), Is.False);
            SEntMan.GetComponent<AimTargetComponent>(doctor).CurrentTarget = BodyPartTarget.RightForearm;
            if (cancel) Assert.That(SEntMan.System<SharedHandsSystem>().TryDrop(doctor, scanner), Is.True);
        });
        await Pair.RunTicksSync(25);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<MedicalDiagnosticHistoryComponent>(patient), Is.EqualTo(!cancel));
            if (!cancel)
                Assert.That(SEntMan.GetComponent<MedicalDiagnosticHistoryComponent>(patient).Reports.Single().Slot,
                    Is.EqualTo(BodyPartSlot.LeftForearm));
            Assert.That(SEntMan.Deleted(scanner), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task DiagnosticWindowsAndSurgicalControlsLoadOnTheClient()
    {
        await Client.WaitAssertion(() =>
        {
            foreach (var kind in Enum.GetValues<MedicalDiagnosticKind>())
            {
                using var window = new Content.Client._Eclipse.AdvancedHealth.MedicalDiagnosticReportWindow(
                    new MedicalDiagnosticReport { Kind = kind, PatientName = "Пациент", Readings = new() { ["heart-rate"] = 75 } });
                window.SetSize = new System.Numerics.Vector2(540, 480);
                window.Measure(new System.Numerics.Vector2(540, 480));
                window.Arrange(new Robust.Shared.Maths.UIBox2(0, 0, 540, 480));
                Assert.That(window.Size.X, Is.LessThanOrEqualTo(540));
            }
            foreach (var mode in new[] { AdvancedTreatmentType.SurgicalAccess, AdvancedTreatmentType.InternalHemostasis })
            {
                using var control = new Content.Client._Eclipse.AdvancedHealth.TreatmentMinigameControl(mode, 0, true, 0, 0, 0, 0);
                control.Measure(new System.Numerics.Vector2(640, 380));
                control.Arrange(new Robust.Shared.Maths.UIBox2(0, 0, 640, 380));
                Assert.That(control.State.Mode, Is.EqualTo(mode));
            }
        });
    }

    [Test]
    public async Task BloodAnalysisUsesThePhysicalSampleSnapshotAndRejectsAnExpiredTube()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            health.BloodGlucose = 12;
            health.OxygenCarryingCapacity = 0.5f;
            SEntMan.System<AdvancedDiseaseSystem>().ApplyCondition(patient, "ClinicalBacterialPneumonia", 0.8f);
            var tube = SEntMan.System<AdvancedBloodSystem>().Sample(patient)!.Value;
            health.BloodGlucose = 4;
            health.OxygenCarryingCapacity = 1;
            var diagnostics = SEntMan.System<MedicalDiagnosticsSystem>();
            Assert.That(diagnostics.AnalyzeSample(tube), Is.True);
            var report = SEntMan.GetComponent<MedicalDiagnosticHistoryComponent>(patient).Reports.Single();
            Assert.That(report.Readings["glucose"], Is.EqualTo(12), "Old blood cannot show live glucose.");
            Assert.That(report.Readings["hemoglobin"], Is.EqualTo(75), "Cellular markers must preserve the sampled oxygen-carrying state.");
            Assert.That(report.Readings.Count, Is.EqualTo(20));
            Assert.That(report.Readings["leukocytes"], Is.GreaterThan(11), "Inflammatory markers must react to the sampled disease state.");
            Assert.That(report.PatientIdentity, Is.EqualTo(health.MedicalIdentity));
            var paper = SEntMan.EntityQuery<PaperComponent>().Single();
            Assert.That(paper.Content, Does.Contain("12"));
            var sample = SEntMan.GetComponent<ClinicalBloodSampleComponent>(tube);
            sample.CollectedAt = Server.ResolveDependency<IGameTiming>().CurTime - TimeSpan.FromMinutes(31);
            Assert.That(diagnostics.AnalyzeSample(tube), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task XRayShowsOnlyTheExaminedRegionAndDoesNotDiscoverSoftTissueDiseases()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            health.BodyParts[BodyPartSlot.LeftForearm].Wounds.Add(new Wound { Type = WoundType.Fracture });
            health.BodyParts[BodyPartSlot.LeftForearm].ForeignBodyCount = 1;
            SEntMan.System<AdvancedDiseaseSystem>().ApplyCondition(patient, "ClinicalAppendicitis");
            var diagnostics = SEntMan.System<MedicalDiagnosticsSystem>();
            diagnostics.RecordExamination(patient, BodyPartSlot.RightForearm, AdvancedTreatmentType.XRay);
            diagnostics.RecordExamination(patient, BodyPartSlot.LeftForearm, AdvancedTreatmentType.XRay);
            var reports = SEntMan.GetComponent<MedicalDiagnosticHistoryComponent>(patient).Reports;
            Assert.That(reports.Single(r => r.Slot == BodyPartSlot.RightForearm).Findings, Does.Contain("diagnostic-xray-clear"));
            var left = reports.Single(r => r.Slot == BodyPartSlot.LeftForearm);
            Assert.That(left.Findings, Does.Contain("diagnostic-fracture"));
            Assert.That(left.Findings, Does.Contain("diagnostic-foreign-body"));
            Assert.That(SEntMan.GetComponent<AdvancedDiseaseComponent>(patient).Known, Is.Empty);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task EcgRecordsArrestAndDoesNotChangeAfterThePatientRecovers()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            health.IsHeartStopped = true;
            SEntMan.System<MedicalDiagnosticsSystem>().RecordExamination(patient, BodyPartSlot.Chest, AdvancedTreatmentType.Electrocardiogram);
            var report = SEntMan.GetComponent<MedicalDiagnosticHistoryComponent>(patient).Reports.Single();
            health.IsHeartStopped = false;
            health.HeartRate = 75;
            Assert.That(report.Readings["heart-rate"], Is.Zero);
            Assert.That(report.Findings, Does.Contain("diagnostic-cardiac-arrest"));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InstrumentReprocessingIsTimedAndAnInterruptedPackIsPreserved(bool cancel)
    {
        var map = await Pair.CreateTestMap();
        EntityUid doctor = default, scalpel = default, wrap = default;
        await Server.WaitAssertion(() =>
        {
            doctor = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            scalpel = SEntMan.SpawnEntity("AdvancedSterileScalpel", map.MapCoords);
            wrap = SEntMan.SpawnEntity("AdvancedInstrumentSterilizationKit", map.MapCoords);
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(doctor, scalpel), Is.True);
            Assert.That(hands.TryDrop(doctor, scalpel), Is.True);
            Assert.That(SEntMan.GetComponent<SurgicalInstrumentComponent>(scalpel).Sterile, Is.False);
            Assert.That(SEntMan.System<SurgicalEquipmentSystem>().CanOperate(doctor, scalpel, AdvancedTreatmentType.SurgicalAccess), Is.False);
            SEntMan.GetComponent<InstrumentSterilizationComponent>(wrap).Delay = 0.1f;
            Assert.That(hands.TryPickupAnyHand(doctor, wrap), Is.True);
            var interaction = new AfterInteractEvent(doctor, wrap, scalpel,
                SEntMan.GetComponent<TransformComponent>(scalpel).Coordinates, true);
            SEntMan.EventBus.RaiseLocalEvent(wrap, interaction);
            Assert.That(interaction.Handled, Is.True);
            Assert.That(SEntMan.GetComponent<SurgicalInstrumentComponent>(scalpel).Sterile, Is.False);
            if (cancel) Assert.That(hands.TryDrop(doctor, wrap), Is.True);
        });
        await Pair.RunTicksSync(25);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(wrap), Is.EqualTo(!cancel));
            Assert.That(SEntMan.GetComponent<SurgicalInstrumentComponent>(scalpel).Sterile, Is.EqualTo(!cancel));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task CleanroomProtectsAnOpenIncisionWithoutCreatingAnInstantInfection()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            bool Expose(int grade)
            {
                var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
                var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
                SEntMan.GetComponent<RoomSanitationComponent>(patient).Grade = grade;
                var part = health.BodyParts[BodyPartSlot.LeftForearm];
                part.IsSurgicalOpen = true;
                var wound = new Wound { Type = WoundType.Cut, IsSurgicalIncision = true };
                part.Wounds.Add(wound);
                SEntMan.System<AdvancedDiseaseSystem>().Advance((patient, health), 121);
                Assert.That(SEntMan.TryGetComponent<AdvancedDiseaseComponent>(patient, out var diseases)
                    && diseases.Active.ContainsKey("WoundFever"), Is.False);
                return wound.IsDirty;
            }
            Assert.That(Expose(0), Is.True);
            Assert.That(Expose(1), Is.False);
            Assert.That(Expose(2), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
