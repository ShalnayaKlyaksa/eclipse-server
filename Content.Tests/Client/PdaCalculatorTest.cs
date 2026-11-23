using Content.Shared.PDA;
using NUnit.Framework;

namespace Content.Tests.Client;

[TestFixture]
public sealed class PdaCalculatorTest
{
    [TestCase("2+3*4", "14")]
    [TestCase("(2+3)*4", "20")]
    [TestCase("1,5+2.25", "3.75")]
    [TestCase("200+10%", "220")]
    [TestCase("200-10%", "180")]
    [TestCase("200*10%", "20")]
    [TestCase("50+10%+10%", "60.5")]
    [TestCase("-5*-2", "10")]
    [TestCase("0.1+0.2", "0.3")]
    [TestCase("0.0000000000000000000000000001", "0.0000000000000000000000000001")]
    public void DecimalExpressionsRespectPrecedenceAndPercentages(string expression, string expected)
    {
        Assert.That(PdaCalculator.TryEvaluate(expression, out var result), Is.True);
        Assert.That(result, Is.EqualTo(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture)));
    }
    [TestCase("1/0")]
    [TestCase("2+")]
    [TestCase("(2+3")]
    [TestCase("2..5")]
    [TestCase("79228162514264337593543950335+1")]
    [TestCase("hello")]
    public void InvalidAndOverflowingExpressionsAreRejected(string expression) => Assert.That(PdaCalculator.TryEvaluate(expression, out _), Is.False);
}
