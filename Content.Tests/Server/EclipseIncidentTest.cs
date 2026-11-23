using System;
using System.Linq;
using Content.Server._Eclipse.StationEvents;
using NUnit.Framework;

namespace Content.Tests.Server;

public sealed class EclipseIncidentTest
{
    [Test]
    public void LivingHullUsesOnlyTheFirstFiveHazardLevels()
    {
        var levels = Enumerable.Range(0, 100).Select(EclipseIncidentSystem.SelectHazardLevel).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(Enum.GetValues<BiologicalHazardLevel>(), Has.Length.EqualTo(7));
            Assert.That(levels.Count(level => level == BiologicalHazardLevel.LocalGrowth), Is.EqualTo(35));
            Assert.That(levels.Count(level => level == BiologicalHazardLevel.InvasiveGrowth), Is.EqualTo(30));
            Assert.That(levels.Count(level => level == BiologicalHazardLevel.HostileEcosystem), Is.EqualTo(22));
            Assert.That(levels.Count(level => level == BiologicalHazardLevel.StationOvergrowth), Is.EqualTo(11));
            Assert.That(levels.Count(level => level == BiologicalHazardLevel.Blob), Is.EqualTo(2));
            Assert.That(levels, Does.Not.Contain(BiologicalHazardLevel.Planetary));
            Assert.That(levels, Does.Not.Contain(BiologicalHazardLevel.Uncontained));
            Assert.Throws<ArgumentOutOfRangeException>(() => EclipseIncidentSystem.SelectHazardLevel(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => EclipseIncidentSystem.SelectHazardLevel(100));
        });
    }
}
