using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._Eclipse.Cooking;
using NUnit.Framework;

namespace Content.Tests.Shared._Eclipse.Cooking;

[TestFixture]
public sealed class CulinaryMathTest
{
    [Test]
    public void EveryMethodIsIdempotentAndPreservesThePropertyBudget()
    {
        var original = new CulinaryData
        {
            Traits = Enum.GetValues<CulinaryProperty>().ToDictionary(property => property, _ => 0.3f),
        };
        var total = original.Traits.Values.Sum();
        var processed = original;
        foreach (var method in Enum.GetValues<CulinaryMethod>())
        {
            processed = CulinaryMath.Process(processed, method);
            Assert.That(processed.Traits.Values.Sum(), Is.LessThanOrEqualTo(total + 0.00001f));
            Assert.That(processed.Traits.Values.All(value => value >= 0 && float.IsFinite(value)));
            Assert.That(CulinaryMath.Process(processed, method), Is.EqualTo(processed));
        }
        Assert.That(original.Methods, Is.Zero, "Processing must not mutate data shared by other portions.");
    }

    [Test]
    public void OppositeEffectsConsumeEachOtherWithoutCreatingAnotherEffect()
    {
        var original = new CulinaryData
        {
            Traits = new()
            {
                [CulinaryProperty.Warmth] = 0.4f, [CulinaryProperty.Cooling] = 0.3f,
                [CulinaryProperty.Stimulation] = 0.2f, [CulinaryProperty.Calm] = 0.2f,
            },
        };
        var resolved = CulinaryMath.Resolve(original);
        Assert.That(resolved.Get(CulinaryProperty.Warmth), Is.EqualTo(0.1f).Within(0.00001));
        Assert.That(resolved.Get(CulinaryProperty.Cooling), Is.Zero);
        Assert.That(resolved.Get(CulinaryProperty.Stimulation), Is.Zero);
        Assert.That(resolved.Get(CulinaryProperty.Calm), Is.Zero);
        Assert.That(original.Get(CulinaryProperty.Cooling), Is.EqualTo(0.3f));
    }

    [Test]
    public void OrderChangesTheResult()
    {
        var input = new CulinaryData
        {
            Traits = new() { [CulinaryProperty.Rapid] = 0.4f, [CulinaryProperty.Satiety] = 0.4f },
        };
        var grindThenBake = CulinaryMath.Process(CulinaryMath.Process(input, CulinaryMethod.Grind), CulinaryMethod.Bake);
        var bakeThenGrind = CulinaryMath.Process(CulinaryMath.Process(input, CulinaryMethod.Bake), CulinaryMethod.Grind);
        Assert.That(grindThenBake.Get(CulinaryProperty.Rapid), Is.Not.EqualTo(bakeThenGrind.Get(CulinaryProperty.Rapid)));
    }

    [Test]
    public void SoakingTradesBenefitsForReducedSideEffects()
    {
        var data = new CulinaryData
        {
            Traits = new() { [CulinaryProperty.Warmth] = 0.4f, [CulinaryProperty.Thirst] = 0.3f },
        };
        var soaked = CulinaryMath.Process(data, CulinaryMethod.Soak);
        Assert.That(soaked.Get(CulinaryProperty.Warmth), Is.EqualTo(0.28f).Within(0.00001));
        Assert.That(soaked.Get(CulinaryProperty.Thirst), Is.EqualTo(0.15f).Within(0.00001));
    }

    [Test]
    public void StabilizerPreservesHeatSensitiveProperties()
    {
        var input = new CulinaryData { Traits = new() { [CulinaryProperty.Healing] = 0.3f } };
        var stable = (CulinaryData) input.Clone();
        stable.Traits[CulinaryProperty.Stability] = 0.4f;
        Assert.That(CulinaryMath.Process(stable, CulinaryMethod.Bake).Get(CulinaryProperty.Healing),
            Is.GreaterThan(CulinaryMath.Process(input, CulinaryMethod.Bake).Get(CulinaryProperty.Healing)));
    }

    [Test]
    public void DataEqualityIsIndependentOfDictionaryOrderAndCloneIsIndependent()
    {
        var a = new CulinaryData { Traits = new() { [CulinaryProperty.Fat] = 0.2f, [CulinaryProperty.Acid] = 0.1f } };
        var b = new CulinaryData { Traits = new() { [CulinaryProperty.Acid] = 0.1f, [CulinaryProperty.Fat] = 0.2f } };
        Assert.That(a, Is.EqualTo(b));
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        var clone = (CulinaryData) a.Clone();
        clone.Traits[CulinaryProperty.Fat] = 10;
        Assert.That(a.Get(CulinaryProperty.Fat), Is.EqualTo(0.2f));
    }
}
