using System.Linq;
using System.Numerics;
using System.Globalization;
using Content.Client._Eclipse.MedicalChemistry;
using Content.Client.Chemistry.UI;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.MedicalChemistry;
using Content.Server.Power.Components;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.MedicalChemistry;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Metabolism;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Chemistry;

public sealed class MedicalChemistryTests : GameTest
{
    private static readonly ProtoId<ReagentPrototype> AntibioticPrecursor = "ClinicalAntibioticPrecursor";
    private static readonly ProtoId<ReagentPrototype> Bicaridine = "Bicaridine";
    private static readonly ProtoId<ReagentPrototype> Carbon = "Carbon";
    private static readonly ProtoId<ReagentPrototype> Inaprovaline = "Inaprovaline";
    private static readonly ProtoId<ReagentPrototype> Iron = "Iron";

    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestPrototypes]
    private const string Prototypes = """
- type: entity
  id: MedicalChemistryTestPatient
  parent: MobHuman
  components:
  - type: Metabolizer
    stages: [Bloodstream]
    solutions:
      Bloodstream:
        solutionName: medicalTestBlood
        solutionOnBody: false
  - type: SolutionContainerManager
    solutions:
      medicalTestBlood:
        maxVol: 100
""";

    [Test]
    public async Task ReactorReservesFeedstockProducesGradedDrugsAndPurifiesWithoutDuplication()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var machine = SEntMan.SpawnEntity("ChemMaster", map.GridCoords);
            var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(machine);
            power.Powered = true;
            var synth = SEntMan.System<MedicalSynthesizerSystem>();
            var comp = SEntMan.GetComponent<MedicalSynthesizerComponent>(machine);
            Entity<MedicalSynthesizerComponent> ent = (machine, comp);
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(machine, SharedChemMaster.BufferSolutionName, out _, out var buffer), Is.True);
            var beaker = SEntMan.SpawnEntity("LargeBeaker", map.GridCoords);
            Assert.That(solutions.TryGetSolution(beaker, "beaker", out var beakerEnt, out var mixture), Is.True);
            mixture.AddReagent(Inaprovaline.Id, FixedPoint2.New(15));
            mixture.AddReagent(Carbon.Id, FixedPoint2.New(15));
            solutions.UpdateChemicals(beakerEnt!.Value);
            buffer.AddSolution(mixture.SplitSolution(mixture.Volume), SProtoMan);
            Assert.That(buffer.GetTotalPrototypeQuantity("BicaridinePrecursor"), Is.EqualTo(FixedPoint2.New(30)));
            Assert.That(buffer.GetTotalPrototypeQuantity("Bicaridine"), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(synth.TryStart(ent, "Bicaridine", (MedicalSynthesisProtocol) 255, 30), Is.False);
            Assert.That(synth.TryStart(ent, "Bicaridine", MedicalSynthesisProtocol.Precision, 61), Is.False);
            Assert.That(synth.TryStart(ent, "Bicaridine", MedicalSynthesisProtocol.Precision, 30), Is.True);
            Assert.That(buffer.Volume, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(synth.GetState(machine)!.Volume, Is.EqualTo(FixedPoint2.New(30)));
            Assert.That(synth.TryStart(ent, "Bicaridine", MedicalSynthesisProtocol.Rapid, 1), Is.False);
            power.Powered = false;
            synth.Advance(ent, 90);
            Assert.That(comp.Progress, Is.Zero, "A power outage must preserve the reserved charge without processing it.");
            power.Powered = true;
            synth.Cancel(ent);
            Assert.That(buffer.Volume, Is.EqualTo(FixedPoint2.New(30)));
            Assert.That(synth.GetState(machine)!.Volume, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(synth.TryStart(ent, "Bicaridine", MedicalSynthesisProtocol.Rapid, 30), Is.True);
            synth.Advance(ent, 90);
            Assert.That(buffer.GetTotalPrototypeQuantity("Bicaridine"), Is.EqualTo(FixedPoint2.New(30)));
            Assert.That(MedicalBatch.GetPurity(buffer, "Bicaridine"), Is.EqualTo(0.8f).Within(0.001));
            var oldActive = buffer.Volume.Float() * 0.8f;
            Assert.That(synth.TryStart(ent, "Bicaridine", MedicalSynthesisProtocol.Purify, 30), Is.True);
            synth.Advance(ent, 90);
            Assert.That(MedicalBatch.GetPurity(buffer, "Bicaridine"), Is.EqualTo(0.99f).Within(0.001));
            Assert.That(buffer.Volume.Float() * 0.99f, Is.LessThan(oldActive));
            Assert.That(buffer.Volume.Float(), Is.InRange(23f, 23.1f));
            Assert.That(synth.TryStart(ent, "Bicaridine", MedicalSynthesisProtocol.Purify, 1), Is.False);
            var dose = buffer.SplitSolution(FixedPoint2.New(5));
            Assert.That(MedicalBatch.GetPurity(dose, "Bicaridine"), Is.EqualTo(0.99f).Within(0.001));
        });
    }

    [Test]
    public async Task LaboratoryAssaysARealSampleAndUnlocksPatientSpecificClinicalProduction()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var slots = SEntMan.System<ItemSlotsSystem>();
            var system = SEntMan.System<MedicalSynthesizerSystem>();
            var analyzer = SEntMan.SpawnEntity("PharmaceuticalAnalyzer", map.GridCoords);
            var synthesizer = SEntMan.SpawnEntity("PharmaceuticalSynthesizer", map.GridCoords);
            var basic = SEntMan.SpawnEntity("ChemMaster", map.GridCoords);
            foreach (var machine in new[] { analyzer, synthesizer, basic })
                SEntMan.GetComponent<ApcPowerReceiverComponent>(machine).Powered = true;
            Entity<MedicalSynthesizerComponent> analyzerEnt = (analyzer, SEntMan.GetComponent<MedicalSynthesizerComponent>(analyzer));
            Entity<MedicalSynthesizerComponent> synthesizerEnt = (synthesizer, SEntMan.GetComponent<MedicalSynthesizerComponent>(synthesizer));
            Assert.That(system.GetState(basic)!.Analysis.Length, Is.EqualTo(3));
            Assert.That(system.GetState(synthesizer)!.Analysis.Length, Is.EqualTo(15));
            Assert.That(system.TryStart((basic, SEntMan.GetComponent<MedicalSynthesizerComponent>(basic)), "ClinicalAntibiotic", MedicalSynthesisProtocol.Rapid, 5), Is.False);
            Assert.That(system.TryStart(analyzerEnt, "Bicaridine", MedicalSynthesisProtocol.Rapid, 5), Is.False);

            var sample = SEntMan.SpawnEntity("ChemistryEmptyVial", map.GridCoords);
            Assert.That(solutions.TryGetSolution(sample, "drink", out var sampleEnt, out var liquid), Is.True);
            liquid.AddReagent(new ReagentId("Blood", [new DnaData { DNA = "laboratory-patient-A" }]), FixedPoint2.New(5));
            solutions.UpdateChemicals(sampleEnt!.Value);
            Assert.That(slots.TryInsert(analyzer, SharedChemMaster.InputSlotName, sample, null), Is.True);
            Assert.That(system.TryAnalyze(analyzerEnt), Is.True);
            Assert.That(liquid.Volume, Is.EqualTo(FixedPoint2.Zero));
            system.Advance(analyzerEnt, 10);
            Assert.That(solutions.TryGetSolution(analyzer, SharedChemMaster.BufferSolutionName, out _, out var assayed), Is.True);
            Assert.That(MedicalBatch.GetSampleDna(assayed, true), Is.EqualTo("laboratory-patient-A"));
            Assert.That(assayed.Volume, Is.EqualTo(FixedPoint2.New(5)));

            var reference = SEntMan.SpawnEntity("ChemistryEmptyVial", map.GridCoords);
            Assert.That(solutions.TryGetSolution(reference, "drink", out var referenceEnt, out var referenceSolution), Is.True);
            referenceSolution.AddSolution(assayed.SplitSolution(FixedPoint2.New(5)), SProtoMan);
            solutions.UpdateChemicals(referenceEnt!.Value);
            Assert.That(slots.TryInsert(synthesizer, MedicalSynthesisRules.SampleSlot, reference, null), Is.True);
            Assert.That(system.GetState(synthesizer)!.SampleProfile, Is.EqualTo("laboratory-patient-A"));
            Assert.That(solutions.TryGetSolution(synthesizer, SharedChemMaster.BufferSolutionName, out _, out var buffer), Is.True);
            buffer.AddReagent(AntibioticPrecursor.Id, FixedPoint2.New(20));
            Assert.That(system.TryStart(synthesizerEnt, "ClinicalAntibiotic", MedicalSynthesisProtocol.Precision, 20), Is.True);
            system.Advance(synthesizerEnt, 90);
            Assert.That(buffer.Volume, Is.EqualTo(FixedPoint2.New(18)));
            Assert.That(MedicalBatch.GetPurity(buffer, "ClinicalAntibiotic"), Is.EqualTo(0.99f).Within(0.001));
            Assert.That(system.TryStart(synthesizerEnt, "ClinicalAntibiotic", MedicalSynthesisProtocol.Personalize, 18), Is.True);
            system.Advance(synthesizerEnt, 90);
            Assert.That(buffer.Volume.Float(), Is.EqualTo(17.1f).Within(0.01));
            Assert.That(MedicalBatch.TryGetPatientProfile(buffer, "ClinicalAntibiotic", out var target), Is.True);
            Assert.That(target, Is.EqualTo("laboratory-patient-A"));
            Assert.That(MedicalBatch.GetActivity(buffer, "ClinicalAntibiotic", target),
                Is.GreaterThan(MedicalBatch.GetActivity(buffer, "ClinicalAntibiotic", "other-patient")));
        });
        await Pair.RunTicksSync(3); // Load the actual machine sprites and networked batch metadata on the client too.
    }

    [Test]
    public async Task DownstreamMedicalReactionsPreserveBoundImpurities()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var beaker = SEntMan.SpawnEntity("LargeBeaker", map.GridCoords);
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(beaker, "beaker", out var soln, out var solution), Is.True);
            solution.AddReagent(new ReagentId("Dexalin", [new MedicalBatchData { Purity = 80 }]), FixedPoint2.New(1));
            solution.AddReagent(Carbon.Id, FixedPoint2.New(1));
            solution.AddReagent(Iron.Id, FixedPoint2.New(1));
            solutions.UpdateChemicals(soln!.Value);
            Assert.That(solution.GetTotalPrototypeQuantity("DexalinPlus"), Is.EqualTo(FixedPoint2.New(3)));
            Assert.That(MedicalBatch.GetPurity(solution, "DexalinPlus"), Is.EqualTo(0.8f).Within(0.001));
        });
    }

    [TestCase(8, false)]
    [TestCase(24, true)]
    public async Task MetabolismUsesOneWeightedDoseAndRetainsOverdoseThresholds(int volume, bool overdose)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MedicalChemistryTestPatient", map.GridCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var part = health.BodyParts[BodyPartSlot.LeftForearm];
            part.SkinIntegrity = 40f;
            health.ToxinLoad = 0;
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(patient, "medicalTestBlood", out _, out var blood), Is.True);
            blood.AddReagent(new ReagentId("Bicaridine", [new MedicalBatchData { Purity = 50 }]), FixedPoint2.New(volume / 2));
            blood.AddReagent(Bicaridine.Id, FixedPoint2.New(volume / 2));
            var initial = health.BodyParts.Values.Sum(bodyPart => bodyPart.SkinIntegrity + bodyPart.MuscleIntegrity);
            var metabolism = SEntMan.GetComponent<MetabolizerComponent>(patient);
            typeof(MetabolizerComponent).GetField(nameof(MetabolizerComponent.NextUpdate))!.SetValue(metabolism, TimeSpan.Zero);
            SEntMan.System<MetabolizerSystem>().Update(0);
            var healed = health.BodyParts.Values.Sum(bodyPart => bodyPart.SkinIntegrity + bodyPart.MuscleIntegrity) - initial;
            Assert.That(healed, Is.EqualTo(1.125f).Within(0.04f), "Different grades must produce one weighted dose, not stacked healing.");
            Assert.That(health.ToxinLoad, overdose ? Is.GreaterThan(1.7f) : Is.InRange(0.35f, 0.4f));
            var rate = SProtoMan.Index(Bicaridine).Metabolisms!.Metabolisms["Bloodstream"].MetabolismRate;
            Assert.That(blood.GetTotalPrototypeQuantity("Bicaridine"), Is.EqualTo(FixedPoint2.New(volume) - rate));
            Assert.That(MedicalBatch.GetPurity(blood, "Bicaridine"), Is.EqualTo(0.75f).Within(0.002));
        });
    }

    [TestCase("ru-RU", false, false)]
    [TestCase("en-US", false, false)]
    [TestCase("ru-RU", true, false)]
    [TestCase("ru-RU", true, true)]
    public async Task MedicalControlsFitChemMasterTab(string culture, bool advanced, bool analyzer)
    {
        await Client.WaitAssertion(() =>
        {
            var localization = Client.ResolveDependency<ILocalizationManager>();
            var original = localization.DefaultCulture;
            localization.SetCulture(CultureInfo.GetCultureInfo(culture));
            using var control = new MedicalSynthesizerControl();
            control.Apply(new MedicalSynthesizerState(true, "Bicaridine", MedicalSynthesisProtocol.Precision,
                MedicalReactorHeat.Automatic, 325.15f, 0.5f, 0, FixedPoint2.New(30), "medical-chemistry-status-running",
                [new MedicalSynthesisAnalysis("Bicaridine", FixedPoint2.New(30), FixedPoint2.New(20), 0.8f)],
                advanced, analyzer, analyzer, advanced ? "patient-A" : null,
                analyzer ? [new ReagentQuantity("Blood", FixedPoint2.New(5), [new DnaData { DNA = "patient-A" }])] : []));
            control.Measure(new Vector2(610, 590));
            control.Arrange(new Robust.Shared.Maths.UIBox2(Vector2.Zero, new Vector2(610, 590)));
            Assert.That(control.DesiredSize.X, Is.LessThanOrEqualTo(610));
            Assert.That(control.DesiredSize.Y, Is.LessThanOrEqualTo(590));
            localization.SetCulture(original);
        });
    }

    [Test]
    public async Task ReactorTelemetryPreservesPackagingChoices()
    {
        await Client.WaitAssertion(() =>
        {
            using var window = new ChemMasterWindow();
            var output = new ContainerInfo("Pill canister", FixedPoint2.Zero, FixedPoint2.New(10)) { Entities = [] };
            ReagentQuantity[] reagents = [new ReagentQuantity("Bicaridine", FixedPoint2.New(30))];
            var medical = new MedicalSynthesizerState(true, "Bicaridine", MedicalSynthesisProtocol.Precision,
                MedicalReactorHeat.Automatic, 325, 0.1f, 0, FixedPoint2.New(30), "medical-chemistry-status-running", []);
            ChemMasterBoundUserInterfaceState State(MedicalSynthesizerState telemetry) =>
                new(ChemMasterMode.Transfer, ChemMasterSortingType.None, null, output, reagents, FixedPoint2.New(30),
                    0, 20, false, ChemMasterDrawSource.Internal, telemetry);
            window.UpdateState(State(medical));
            var tabs = window.FindControl<Robust.Client.UserInterface.Controls.TabContainer>("Tabs");
            tabs.CurrentTab = 1;
            window.PillDosage.Value = 3;
            window.PillNumber.Value = 2;
            window.UpdateState(State(medical with { Progress = 0.5f }));
            Assert.That(window.Medical.Visible, Is.False, "Telemetry must not draw the synthesis tab over the output tab.");
            Assert.That(window.PillDosage.Value, Is.EqualTo(3));
            Assert.That(window.PillNumber.Value, Is.EqualTo(2));
        });
    }
}
