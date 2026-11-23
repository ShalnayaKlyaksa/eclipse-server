using System.Linq;
using Content.Shared._Eclipse.AdvancedHealth;
using NUnit.Framework;

namespace Content.Tests.Shared._Eclipse;

[TestFixture]
public sealed class AdvancedTraumaTest
{
    [Test]
    public void DestroyedTissuesStillAllowLifeSavingBleedingControl()
    {
        var part = new BodyPartState { Slot = BodyPartSlot.LeftForearm, IsDestroyed = true };
        part.Wounds.Add(new Wound { ExternalBleedingRate = 2f });
        Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.Bandage), Is.True);
        Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.Tourniquet), Is.True);
        Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.BoneFixation), Is.False);
        part.HasTourniquet = true;
        Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.TourniquetRemoval), Is.True);
        part.LocalAnesthesia = 10;
        AdvancedTraumaRecovery.Advance(part, 5, 1, 0);
        Assert.That(part.LocalAnesthesia, Is.EqualTo(5), "Disabling bone recovery must not make anesthesia permanent.");
    }

    [Test]
    public void SurgicalIndicationsRequirePreparationAnesthesiaAndSourceControl()
    {
        var part = new BodyPartState { Slot = BodyPartSlot.LeftForearm };
        part.Wounds.Add(new Wound { Type = WoundType.Fracture, Severity = WoundSeverity.Severe });
        part.Wounds.Add(new Wound { Type = WoundType.Cut, IsDirty = true, InternalBleedingRate = 2f });
        Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.Suture), Is.False);
        Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.SurgicalAccess), Is.False);
        part.IsSurgicalSitePrepared = true;
        part.LocalAnesthesia = 120;
        Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.SurgicalAccess), Is.False);
        part.Wounds[1].IsDirty = false;
        Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.SurgicalAccess), Is.True);
        part.IsSurgicalOpen = true;
        Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.InternalHemostasis), Is.True);
        Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.BoneFixation), Is.False);
        Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.SurgicalClosure), Is.False);
        part.Wounds[1].InternalBleedingRate = 0;
        Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.BoneFixation), Is.True);
        part.Wounds[0].IsBoneFixed = true;
        Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.BoneFixation), Is.False);
        Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.SurgicalClosure), Is.True);
        part.LocalAnesthesia = 0;
        Assert.That(AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.SurgicalClosure), Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void FractureUnionRequiresCirculationAndClosedAccess(bool fixedFracture)
    {
        var part = new BodyPartState
        {
            Slot = BodyPartSlot.LeftForearm, BoneIntegrity = 20, IsSplinted = !fixedFracture,
            IsSurgicalOpen = true, LocalAnesthesia = 120,
        };
        part.Wounds.Add(new Wound
        {
            Type = WoundType.Fracture, Severity = fixedFracture ? WoundSeverity.Severe : WoundSeverity.Moderate,
            IsBoneFixed = fixedFracture,
        });
        AdvancedTraumaRecovery.Advance(part, 30, 1, 300);
        Assert.That(part.LocalAnesthesia, Is.EqualTo(90));
        Assert.That(part.BoneIntegrity, Is.EqualTo(20));
        part.IsSurgicalOpen = false;
        part.HasTourniquet = true;
        AdvancedTraumaRecovery.Advance(part, 30, 1, 300);
        Assert.That(part.BoneIntegrity, Is.EqualTo(20));
        part.HasTourniquet = false;
        AdvancedTraumaRecovery.Advance(part, 30, 0.2f, 300);
        Assert.That(part.BoneIntegrity, Is.EqualTo(20));
        AdvancedTraumaRecovery.Advance(part, 150, 1, 300);
        Assert.That(part.BoneIntegrity, Is.EqualTo(70).Within(0.001));
        Assert.That(part.Wounds.Single().HealingProgress, Is.EqualTo(0.5f));
        AdvancedTraumaRecovery.Advance(part, 150, 1, 300);
        Assert.That(part.BoneIntegrity, Is.EqualTo(100));
        Assert.That(part.Wounds, Is.Empty);
        Assert.That(part.IsSplinted, Is.False);
    }

    [Test]
    public void SplintCannotUniteASevereUnfixedFractureAndFixationDoesNotRegrowTissue()
    {
        var part = new BodyPartState { Slot = BodyPartSlot.LeftThigh, IsSplinted = true, BoneIntegrity = 15 };
        part.Wounds.Add(new Wound { Type = WoundType.Fracture, Severity = WoundSeverity.Severe });
        AdvancedTraumaRecovery.Advance(part, 300, 1, 300);
        Assert.That(part.BoneIntegrity, Is.EqualTo(15));
        var unsupported = part.GetFunction();
        part.Wounds[0].IsBoneFixed = true;
        Assert.That(part.GetFunction(), Is.GreaterThan(unsupported));
        Assert.That(part.BoneIntegrity, Is.EqualTo(15), "Fixation supports function without instantly healing bone.");
        part.NerveIntegrity = 0;
        Assert.That(part.GetFunction(), Is.Zero, "Hardware cannot repair a severed nerve.");
        Assert.That(AdvancedTraumaRecovery.Advance(part, float.NaN, 1, 300), Is.False);
    }
}
