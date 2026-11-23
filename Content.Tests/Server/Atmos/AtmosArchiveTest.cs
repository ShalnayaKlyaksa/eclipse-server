using System;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using NUnit.Framework;

namespace Content.Tests.Server.Atmos;

[TestFixture]
// These unit tests deliberately replace a tile's live mixture to check snapshot independence.
#pragma warning disable RA0002
public sealed class AtmosArchiveTest
{
    [Test]
    public void ArchiveReusesIndependentSnapshot()
    {
        var air = new GasMixture(2500) { Temperature = 300 };
        air.SetMoles(Gas.Oxygen, 10);
        var tile = new TileAtmosphere { Air = air };

        AtmosphereSystem.Archive(tile, 1);
        var archived = tile.AirArchived;
        Assert.That(archived, Is.Not.SameAs(air));
        Assert.That(archived, Is.EqualTo(new GasMixture(air)));

        air.SetMoles(Gas.Oxygen, 20);
        air.SetMoles(Gas.Nitrogen, 30);
        air.Temperature = 500;
        air.Volume = 1000;
        Assert.That(archived.GetMoles(Gas.Oxygen), Is.EqualTo(10));

        AtmosphereSystem.Archive(tile, 2);
        Assert.That(tile.AirArchived, Is.SameAs(archived));
        Assert.That(archived, Is.EqualTo(new GasMixture(air)));
        Assert.That(tile.ArchivedCycle, Is.EqualTo(2));

        // Replacing the live mixture must overwrite old gases in the reused snapshot too.
        tile.Air = new GasMixture(500) { Temperature = 200 };
        tile.Air.SetMoles(Gas.Plasma, 5);
        AtmosphereSystem.Archive(tile, 3);
        Assert.That(tile.AirArchived, Is.SameAs(archived));
        Assert.That(archived, Is.EqualTo(new GasMixture(tile.Air)));

        tile.Air = null;
        AtmosphereSystem.Archive(tile, 4);
        Assert.That(tile.ArchivedCycle, Is.EqualTo(4));
    }

    [Test]
    public void WarmArchiveDoesNotAllocate()
    {
        var tile = new TileAtmosphere { Air = new GasMixture(2500) { Temperature = 300 } };
        AtmosphereSystem.Archive(tile, 1);

        var baselineBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++)
        {
            tile.AirArchived = new GasMixture(tile.Air);
        }
        var baselineAllocated = GC.GetAllocatedBytesForCurrentThread() - baselineBefore;

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 2; i < 10002; i++)
        {
            AtmosphereSystem.Archive(tile, i);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.Zero, "Archiving active tiles should reuse their allocated mixture storage.");
        TestContext.Out.WriteLine($"Snapshot allocations for 10000 calls: before={baselineAllocated} bytes; after={allocated} bytes.");
    }
}
#pragma warning restore RA0002
