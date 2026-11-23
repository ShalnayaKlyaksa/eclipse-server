using Content.Shared._Eclipse.Beekeeping;
using NUnit.Framework;

namespace Content.Tests.Shared._Eclipse.Beekeeping;

[TestFixture]
public sealed class BeeGeneticsTest
{
    [Test]
    public void SpecializedFramesTradeResourcesAndNeverBoostLateMutations()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ApiaryFrameRules.MutationMultiplier("Selection", 2), Is.EqualTo(1.5f));
            Assert.That(ApiaryFrameRules.CombBonus("Selection"), Is.EqualTo(-1));
            Assert.That(ApiaryFrameRules.DroneBonus("Nursing"), Is.EqualTo(2));
            Assert.That(ApiaryFrameRules.FoodMultiplier("Nursing"), Is.GreaterThan(1));
            Assert.That(ApiaryFrameRules.WaterMultiplier("Insulated"), Is.LessThan(1));
            Assert.That(ApiaryFrameRules.TemperatureTolerance("Insulated"), Is.EqualTo(8));
        });
        foreach (var kind in new[] { "Production", "Selection", "Nursing", "Insulated" })
            for (var tier = 4; tier <= 8; tier++)
                Assert.That(ApiaryFrameRules.MutationMultiplier(kind, tier), Is.EqualTo(1), $"{kind}, tier {tier}");
    }

    [Test]
    public void OffspringInheritsAllelesAndDoesNotAddParallelLineageAges()
    {
        var first = new BeeGenome { Dominant = "Meadow", Recessive = "Forest", Generation = 4, LineageSeconds = 1000, Productivity = 5 };
        var second = new BeeGenome { Dominant = "Hardy", Recessive = "Common", Generation = 2, LineageSeconds = 100, Productivity = 1 };
        var low = BeeGenetics.Cross(first, second, 120, () => 0);
        var high = BeeGenetics.Cross(first, second, 120, () => 0.999);
        Assert.That(low.Dominant, Is.EqualTo("Meadow"));
        Assert.That(low.Recessive, Is.EqualTo("Hardy"));
        Assert.That(high.Dominant, Is.EqualTo("Forest"));
        Assert.That(high.Recessive, Is.EqualTo("Common"));
        Assert.That(low.Generation, Is.EqualTo(3));
        Assert.That(low.LineageSeconds, Is.EqualTo(220));
        Assert.That(low.Productivity, Is.EqualTo(5));
        Assert.That(high.Productivity, Is.EqualTo(1));
        Assert.That(first.LineageSeconds, Is.EqualTo(1000), "Parents must remain immutable during inheritance.");
    }

    [Test]
    public void RareProductsRequireBothTimeAndGeneration()
    {
        var species = new BeeSpeciesPrototype { RareProduct = "IndustrialAdvancedProcessor", RareGeneration = 8, RareLineageSeconds = 2700 };
        var genome = new BeeGenome { Generation = 8, LineageSeconds = 2699 };
        Assert.That(BeeGenetics.RareEligible(genome, species), Is.False);
        genome.LineageSeconds = 2700;
        Assert.That(BeeGenetics.RareEligible(genome, species), Is.True);
        genome.Generation = 7;
        Assert.That(BeeGenetics.RareEligible(genome, species), Is.False);
    }

    [Test]
    public void MutationRequiresTheCorrectParentsMineralsAndWorkedTime()
    {
        var mutation = new BeeMutationPrototype
        { First = "Resonant", Second = "Imperial", Result = "Quantum", Generation = 8, LineageSeconds = 2700, NeedsMinerals = true };
        var first = new BeeGenome { Dominant = "Resonant", Generation = 7, LineageSeconds = 2500 };
        var second = new BeeGenome { Dominant = "Imperial", Generation = 7, LineageSeconds = 2500 };
        Assert.That(BeeGenetics.Eligible(first, second, mutation, 200, true), Is.True);
        Assert.That(BeeGenetics.Eligible(second, first, mutation, 200, true), Is.True);
        Assert.That(BeeGenetics.Eligible(first, second, mutation, 199, true), Is.False);
        Assert.That(BeeGenetics.Eligible(first, second, mutation, 200, false), Is.False);
        second.Dominant = "Meadow";
        Assert.That(BeeGenetics.Eligible(first, second, mutation, 200, true), Is.False);
    }
}
