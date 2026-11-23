using Content.Shared._Eclipse.MedicalChemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Tests.Shared.Chemistry;

[TestFixture]
public sealed class MedicalSynthesisTests : ContentUnitTest
{
    [OneTimeSetUp]
    public void Setup() => IoCManager.Resolve<IPrototypeManager>().Initialize();

    [Test]
    public void BlendingAndSplittingConserveGradeAndVolume()
    {
        var solution = new Solution();
        solution.AddReagent(new ReagentId("Bicaridine", [new MedicalBatchData { Purity = 50 }]), FixedPoint2.New(10));
        solution.AddReagent("Bicaridine", FixedPoint2.New(10));
        solution.AddReagent("Water", FixedPoint2.New(10));
        Assert.That(MedicalBatch.GetPurity(solution, "Bicaridine"), Is.EqualTo(0.75f));
        var dose = MedicalBatch.Take(solution, "Bicaridine", FixedPoint2.New(4));
        Assert.That(dose.Volume, Is.EqualTo(FixedPoint2.New(4)));
        Assert.That(MedicalBatch.GetPurity(dose, "Bicaridine"), Is.EqualTo(0.75f));
        Assert.That(MedicalBatch.GetPurity(solution, "Bicaridine"), Is.EqualTo(0.75f));
        Assert.That(solution.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(10)));
        var cloned = dose.Clone();
        Assert.That(MedicalBatch.GetPurity(cloned, "Bicaridine"), Is.EqualTo(0.75f));
        Assert.That(solution.Volume + dose.Volume, Is.EqualTo(FixedPoint2.New(30)));
    }

    [TestCase(0.35f)]
    [TestCase(0.8f)]
    [TestCase(0.99f)]
    public void PurificationNeverCreatesActiveIngredient(float initialPurity)
    {
        var clean = MedicalSynthesisRules.Result(MedicalSynthesisProtocol.Purify, 0, initialPurity);
        var overheated = MedicalSynthesisRules.Result(MedicalSynthesisProtocol.Purify, 1, initialPurity);
        Assert.That(clean.Purity, Is.EqualTo(99));
        Assert.That(clean.Yield * clean.Purity / 100f, Is.LessThanOrEqualTo(initialPurity));
        Assert.That(overheated.Yield * overheated.Purity / 100f, Is.LessThanOrEqualTo(initialPurity));
        Assert.That(clean.Yield, Is.InRange(0f, 1f));
    }

    [Test]
    public void ProtocolsOfferDifferentSpeedYieldAndPurity()
    {
        var rapid = MedicalSynthesisRules.Result(MedicalSynthesisProtocol.Rapid, 0);
        var precise = MedicalSynthesisRules.Result(MedicalSynthesisProtocol.Precision, 0);
        var dirty = MedicalSynthesisRules.Result(MedicalSynthesisProtocol.Precision, 0.5f);
        Assert.That(rapid.Yield, Is.GreaterThan(precise.Yield));
        Assert.That(rapid.Purity, Is.LessThan(precise.Purity));
        Assert.That(MedicalSynthesisRules.Duration(MedicalSynthesisProtocol.Rapid),
            Is.LessThan(MedicalSynthesisRules.Duration(MedicalSynthesisProtocol.Precision)));
        Assert.That(dirty.Purity, Is.LessThan(precise.Purity));
        Assert.That(MedicalSynthesisRules.Result(MedicalSynthesisProtocol.Precision, 0, 0.5f).Purity,
            Is.LessThanOrEqualTo(50), "Reprocessing a contaminated precursor must not erase its impurities.");
    }

    [Test]
    public void AssaysRequireOneRealIdentityAndPatientAffinitySurvivesTransfers()
    {
        var sample = new Solution();
        sample.AddReagent(new ReagentId("Blood", [new DnaData { DNA = "patient-A" }]), FixedPoint2.New(5));
        Assert.That(MedicalBatch.GetSampleDna(sample, true), Is.Null);
        Assert.That(MedicalBatch.Assay(sample), Is.EqualTo("patient-A"));
        Assert.That(MedicalBatch.GetSampleDna(sample, true), Is.EqualTo("patient-A"));
        var reference = sample.SplitSolution(FixedPoint2.New(2));
        Assert.That(MedicalBatch.GetSampleDna(reference, true), Is.EqualTo("patient-A"));
        sample.AddReagent(new ReagentId("Blood", [new DnaData { DNA = "patient-B" }]), FixedPoint2.New(1));
        Assert.That(MedicalBatch.GetSampleDna(sample, true), Is.Null, "A mixed sample must not fabricate an individual identity.");

        var drug = new Solution();
        drug.AddReagent(new ReagentId("Bicaridine", [new MedicalBatchData { Purity = 80, PatientDna = "patient-A" }]), FixedPoint2.New(10));
        Assert.That(MedicalBatch.GetActivity(drug, "Bicaridine", "patient-A"), Is.EqualTo(9.2f).Within(0.001));
        Assert.That(MedicalBatch.GetActivity(drug, "Bicaridine", "patient-B"), Is.EqualTo(5.2f).Within(0.001));
        Assert.That(MedicalBatch.GetMismatch(drug, "Bicaridine", "patient-B"), Is.EqualTo(1));
        Assert.That(MedicalBatch.GetActivity(drug.Clone(), "Bicaridine", "patient-A"), Is.EqualTo(9.2f).Within(0.001));
        drug.AddReagent("Bicaridine", FixedPoint2.New(1));
        Assert.That(MedicalBatch.TryGetPatientProfile(drug, "Bicaridine", out _), Is.False);
    }

    [Test]
    public void ColdStopsReactionAndOverheatingAccumulatesPersistentStress()
    {
        var recipe = new MedicalSynthesisPrototype { MinTemperature = 315, MaxTemperature = 335 };
        var cold = MedicalSynthesisRules.Step(recipe, MedicalSynthesisProtocol.Precision, MedicalReactorHeat.Cool, 293, 0, 0, 1);
        Assert.That(cold.Progress, Is.Zero);
        var hot = MedicalSynthesisRules.Step(recipe, MedicalSynthesisProtocol.Precision, MedicalReactorHeat.Boost, 345, 0, 0, 1);
        Assert.That(hot.Stress, Is.GreaterThan(0));
        var recovered = MedicalSynthesisRules.Step(recipe, MedicalSynthesisProtocol.Precision, MedicalReactorHeat.Automatic,
            325, hot.Progress, hot.Stress, 1);
        Assert.That(recovered.Stress, Is.EqualTo(hot.Stress));
        Assert.That(recovered.Progress - hot.Progress, Is.GreaterThan(hot.Progress));
        Assert.That(recovered.Temperature, Is.EqualTo(325));
    }
}
