using Content.Shared._Eclipse.AdvancedHealth;
using NUnit.Framework;

namespace Content.Tests.Shared._Eclipse;

[TestFixture]
public sealed class ClinicalLaboratoryTest
{
    [Test]
    public void HypoperfusionRenalFailureAndInflammationHaveDistinctLaboratoryConsequences()
    {
        var health = new AdvancedHealthComponent { Perfusion = 1 };
        var healthy = ClinicalLaboratoryModel.Calculate(health, 1, 1, 0, 0);
        var renal = ClinicalLaboratoryModel.Calculate(health, 0.25f, 1, 0, 0);
        var inflammatory = ClinicalLaboratoryModel.Calculate(health, 1, 1, 0, 0.8f);
        Assert.That(renal["creatinine"], Is.GreaterThan(healthy["creatinine"]));
        Assert.That(renal["potassium"], Is.GreaterThan(healthy["potassium"]));
        Assert.That(renal["crp"], Is.EqualTo(healthy["crp"]));
        Assert.That(inflammatory["crp"], Is.GreaterThan(healthy["crp"]));
        Assert.That(inflammatory["leukocytes"], Is.GreaterThan(healthy["leukocytes"]));
        Assert.That(inflammatory["creatinine"], Is.EqualTo(healthy["creatinine"]));
        health.Perfusion = 0.2f;
        Assert.That(ClinicalLaboratoryModel.Calculate(health, 1, 1, 0, 0)["lactate"], Is.GreaterThan(healthy["lactate"]));
    }

    [Test]
    public void BloodDilutionAndLiverDamageAffectTheirOwnProfiles()
    {
        var health = new AdvancedHealthComponent { OxygenCarryingCapacity = 1 };
        var healthy = ClinicalLaboratoryModel.Calculate(health, 1, 1, 0, 0);
        health.OxygenCarryingCapacity = 0.5f;
        var diluted = ClinicalLaboratoryModel.Calculate(health, 1, 1, 0, 0);
        Assert.That(diluted["hemoglobin"], Is.EqualTo(healthy["hemoglobin"] * 0.5f));
        Assert.That(diluted["hematocrit"], Is.EqualTo(healthy["hematocrit"] * 0.5f));
        Assert.That(diluted["erythrocytes"], Is.EqualTo(healthy["erythrocytes"] * 0.5f));
        var hepatic = ClinicalLaboratoryModel.Calculate(health, 1, 0.2f, 0, 0);
        Assert.That(hepatic["alt"], Is.GreaterThan(diluted["alt"]));
        Assert.That(hepatic["bilirubin"], Is.GreaterThan(diluted["bilirubin"]));
        Assert.That(hepatic["protein"], Is.LessThan(diluted["protein"]));
        Assert.That(hepatic["creatinine"], Is.EqualTo(diluted["creatinine"]));
    }
}
