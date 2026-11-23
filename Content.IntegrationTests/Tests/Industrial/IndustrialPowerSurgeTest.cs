using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Industrial;
using Content.Server.Lightning;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Atmos.Components;
using Content.Shared.Interaction;
using Content.Shared.Emp;
using Content.Shared.NodeContainer;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class IndustrialPowerSurgeTest : GameTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task NativeLightningAndEmpEventsReachThePowerNetwork(bool emp)
    {
        var map = await Pair.CreateTestMap();
        EntityUid cable = default;
        await Server.WaitAssertion(() =>
        {
            cable = SEntMan.SpawnEntity("CableHV", map.GridCoords);
        });
        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() =>
        {
            if (emp)
            {
                var pulse = new EmpPulseEvent(50000, false, false, TimeSpan.FromSeconds(5), null);
                SEntMan.EventBus.RaiseLocalEvent(cable, ref pulse);
                Assert.That(pulse.Affected);
            }
            else
            {
                var lightning = new HitByLightningEvent(cable, cable);
                SEntMan.EventBus.RaiseLocalEvent(cable, ref lightning);
            }
            Server.System<IndustrialPowerSurgeSystem>().Update(.5f);
            Assert.That(SEntMan.GetComponent<PowerSurgeCableComponent>(cable).Stress, Is.GreaterThan(0));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task NormalTeslaCollectionDoesNotSendASurgeIntoItsOutputCable()
    {
        var map = await Pair.CreateTestMap();
        EntityUid cable = default, coil = default;
        await Server.WaitAssertion(() =>
        {
            cable = SEntMan.SpawnEntity("CableHV", map.GridCoords);
            coil = SEntMan.SpawnEntity("TeslaCoil", map.GridCoords);
        });
        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() =>
        {
            var lightning = new HitByLightningEvent(coil, coil);
            SEntMan.EventBus.RaiseLocalEvent(coil, ref lightning);
            Server.System<IndustrialPowerSurgeSystem>().Update(.5f);
            Assert.That(SEntMan.GetComponent<PowerSurgeCableComponent>(cable).Stress, Is.Zero);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExternalSurgesHeatOnlyConnectedWiringAndAConsumableProtectorAbsorbsThem(bool protectedLine)
    {
        var map = await Pair.CreateTestMap();
        EntityUid first = default, second = default, isolated = default, protector = default;
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            foreach (var x in new[] { 0, 1, 5 })
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            first = SEntMan.SpawnEntity("CableHV", map.GridCoords);
            second = SEntMan.SpawnEntity("CableHV", new EntityCoordinates(map.Grid, 1.5f, .5f));
            isolated = SEntMan.SpawnEntity("CableHV", new EntityCoordinates(map.Grid, 5.5f, .5f));
            if (protectedLine)
            {
                protector = SEntMan.SpawnEntity("IndustrialSurgeProtector", map.GridCoords);
                Assert.That(Server.System<SharedTransformSystem>().AnchorEntity(protector));
            }
        });
        await Pair.RunTicksSync(3);
        await Server.WaitAssertion(() =>
        {
            var system = Server.System<IndustrialPowerSurgeSystem>();
            var firstGroup = SEntMan.GetComponent<NodeContainerComponent>(first).Nodes["power"].NodeGroup;
            Assert.That(firstGroup, Is.Not.Null);
            Assert.That(SEntMan.GetComponent<NodeContainerComponent>(second).Nodes["power"].NodeGroup, Is.SameAs(firstGroup));
            Assert.That(system.Inject(first, 2, 5, 100000));
            Assert.That(system.Inject(second, 2, 5, 100000), Is.False, "One EMP hitting both cables must not duplicate the pulse.");
            for (var i = 0; i < 10; i++)
                system.Update(.5f);
            var a = SEntMan.GetComponent<PowerSurgeCableComponent>(first);
            var b = SEntMan.GetComponent<PowerSurgeCableComponent>(second);
            var burning = new[] { first, second }.Count(uid => SEntMan.GetComponent<FlammableComponent>(uid).OnFire);
            if (protectedLine)
            {
                Assert.That(a.Stress + b.Stress, Is.Zero);
                Assert.That(burning, Is.Zero);
                Assert.That(SEntMan.GetComponent<IndustrialSurgeProtectorComponent>(protector).RemainingCapacity, Is.EqualTo(100000));
            }
            else
            {
                Assert.That(a.Stress + b.Stress, Is.GreaterThan(0));
                Assert.That(burning, Is.EqualTo(1), "A single fault must not ignite every cable simultaneously.");
            }
            Assert.That(SEntMan.GetComponent<PowerSurgeCableComponent>(isolated).Stress, Is.Zero);
            Assert.That(SEntMan.GetComponent<FlammableComponent>(isolated).OnFire, Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task ReplacingTheCartridgeConsumesTheItemAndRestoresOnlyTheRatedCapacity()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var protector = SEntMan.SpawnEntity("IndustrialSurgeProtector", map.GridCoords);
            var cartridge = SEntMan.SpawnEntity("IndustrialSurgeFuse", map.GridCoords);
            var component = SEntMan.GetComponent<IndustrialSurgeProtectorComponent>(protector);
            component.RemainingCapacity = 0;
            var use = new InteractUsingEvent(protector, cartridge, protector, map.GridCoords);
            SEntMan.EventBus.RaiseLocalEvent(protector, use);
            Assert.That(use.Handled);
            Assert.That(component.RemainingCapacity, Is.EqualTo(component.Capacity));
            Assert.That(SEntMan.Deleted(cartridge));
            Assert.That(Server.System<IndustrialPowerSurgeSystem>().Inject(protector, float.NaN, 5, 1000), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
