using System;
using Content.Shared._Eclipse.AdvancedHealth;
using NUnit.Framework;

namespace Content.Tests.Shared._Eclipse;

#pragma warning disable RA0039 // These calculation tests use synthetic prototype data.
[TestFixture]
public sealed class ClinicalMedicationTest
{
    [Test]
    public void OpioidAntagonistLeavesOtherSedativesAndTheirRespiratoryRisk()
    {
        var opioid = new ClinicalMedicationPrototype { Class = MedicationClass.Opioid, Analgesia = 0.7f, Sedation = 0.2f, RespiratoryDepression = 0.3f };
        var sedative = new ClinicalMedicationPrototype { Class = MedicationClass.Sedative, Sedation = 0.3f, RespiratoryDepression = 0.15f };
        var naloxone = new ClinicalMedicationPrototype { Class = MedicationClass.Naloxone };
        var combined = ClinicalMedicationMath.Calculate(new[] { (opioid, 3f), (sedative, 3f) });
        var rescued = ClinicalMedicationMath.Calculate(new[] { (opioid, 3f), (sedative, 3f), (naloxone, 3f) });
        Assert.That(combined.RespiratoryDepression, Is.GreaterThan(0.45f), "The combination must carry an added respiratory risk.");
        Assert.That(rescued.RespiratoryDepression, Is.EqualTo(0.15f).Within(0.001));
        Assert.That(rescued.Sedation, Is.EqualTo(0.3f).Within(0.001));
        Assert.That(rescued.Analgesia, Is.Zero);
    }

    [Test]
    public void ExcessDrugAddsToxicityWithoutUnboundedTherapeuticActivity()
    {
        var drug = new ClinicalMedicationPrototype { Class = MedicationClass.Antibiotic, EffectiveQuantity = 2, OverdoseQuantity = 10 };
        var normal = ClinicalMedicationMath.Calculate(new[] { (drug, 3f) });
        var excess = ClinicalMedicationMath.Calculate(new[] { (drug, 30f), (drug, float.NaN), (drug, float.PositiveInfinity) });
        Assert.That(excess.Activity[MedicationClass.Antibiotic], Is.EqualTo(normal.Activity[MedicationClass.Antibiotic]));
        Assert.That(normal.Toxicity, Is.Zero);
        Assert.That(excess.Toxicity, Is.GreaterThan(0));
        Assert.That(float.IsFinite(excess.Toxicity), Is.True);
    }

    [Test]
    public void GlucoseAndInsulinHaveOppositeMetabolicEffects()
    {
        var insulin = new ClinicalMedicationPrototype { Class = MedicationClass.Insulin, GlucosePerSecond = -0.09f };
        var glucose = new ClinicalMedicationPrototype { Class = MedicationClass.Glucose, GlucosePerSecond = 0.15f };
        Assert.That(ClinicalMedicationMath.Calculate(new[] { (insulin, 3f) }).Glucose, Is.LessThan(0));
        Assert.That(ClinicalMedicationMath.Calculate(new[] { (glucose, 3f) }).Glucose, Is.GreaterThan(0));
    }

    [Test]
    public void WeakOrMismatchedPreparationsStillOverdoseByTheirActualVolume()
    {
        var drug = new ClinicalMedicationPrototype { Class = MedicationClass.Opioid, EffectiveQuantity = 2,
            OverdoseQuantity = 10, Analgesia = 0.5f, RespiratoryDepression = 0.2f };
        var weak = ClinicalMedicationMath.Calculate(new[] { (drug, 30f, 1f) });
        var strong = ClinicalMedicationMath.Calculate(new[] { (drug, 30f, 30f) });
        Assert.That(weak.Analgesia, Is.LessThan(strong.Analgesia));
        Assert.That(weak.Toxicity, Is.EqualTo(strong.Toxicity).And.GreaterThan(0));
        Assert.That(weak.RespiratoryDepression, Is.GreaterThan(0.5f));
    }
}
#pragma warning restore RA0039
