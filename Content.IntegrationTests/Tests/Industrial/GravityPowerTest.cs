using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Power;
using Content.Server.Materials;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Power.Generator;
using Content.Server.Singularity.Components;
using Content.Server.Singularity.EntitySystems;
using Content.Shared._Eclipse.Power;
using Content.Shared.Power.Generator;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class GravityPowerTest : GameTest
{
    [Test]
    public async Task DestroyingActiveContainmentLeavesAResidualWellWithNoElectricalOutput()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var hole = SEntMan.SpawnEntity("IndustrialMicroBlackHoleGenerator", map.GridCoords);
            SEntMan.GetComponent<GravityPowerSourceComponent>(hole).ResidualActivity = .9f;
            SEntMan.DeleteEntity(hole);
            var query = SEntMan.EntityQueryEnumerator<ResidualMicroBlackHoleComponent>();
            Assert.That(query.MoveNext(out var residual, out var state), Is.True);
            Assert.That(state!.Activity, Is.EqualTo(.9f));
            Assert.That(SEntMan.HasComponent<PowerSupplierComponent>(residual), Is.False);
            Assert.That(SEntMan.HasComponent<FuelGeneratorComponent>(residual), Is.False);
            var gravity = Server.System<GravityPowerSystem>();
            gravity.Update(.5f);
            Assert.That(SEntMan.GetComponent<GravityWellComponent>(residual).BaseRadialAcceleration, Is.GreaterThan(25));
            gravity.Update(10);
            Assert.That(state.Activity, Is.Zero);
            Assert.That(SEntMan.GetComponent<GravityWellComponent>(residual).MaxRange, Is.LessThan(0));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task KineticGeneratorsShareOneFuelPaymentAndCannotGenerateAfterItIsSpent()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            for (var x = -4; x <= 4; x++)
            for (var y = -4; y <= 4; y++)
            {
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                SEntMan.SpawnEntity("CableHV", new EntityCoordinates(map.Grid.Owner, x + .5f, y + .5f));
            }
            var hole = SEntMan.SpawnEntity("IndustrialMicroBlackHoleGenerator", map.GridCoords);
            var transform = Server.System<SharedTransformSystem>();
            transform.AnchorEntity(hole);
            var plant = SEntMan.GetComponent<PowerPlantComponent>(hole);
            plant.FieldCount = 4;
            plant.Warmup = 1;
            var generators = Server.System<GeneratorSystem>();
            Server.System<MaterialStorageSystem>().TryChangeMaterialAmount(hole, "Steel", 1000);
            var collectors = new List<EntityUid>();
            foreach (var offset in new[] { new Vector2i(-3, 0), new Vector2i(3, 0), new Vector2i(0, -3), new Vector2i(0, 3) })
            {
                var collector = SEntMan.SpawnEntity("GravitationalKineticGenerator", new EntityCoordinates(map.Grid.Owner, .5f + offset.X, .5f + offset.Y));
                transform.AnchorEntity(collector);
                SEntMan.GetComponent<GravitationalKineticGeneratorComponent>(collector).RotorSpeed = 1;
                collectors.Add(collector);
            }
            Server.System<NodeGroupSystem>().ForceUpdate();
            var before = generators.GetFuel(hole);
            generators.SetFuelGeneratorOn(hole, true);
            generators.Update(1);
            var gravity = Server.System<GravityPowerSystem>();
            gravity.Update(1);
            var source = SEntMan.GetComponent<GravityPowerSourceComponent>(hole);
            Assert.That(source.HarvestedPower, Is.EqualTo(1200000).Within(1));
            Assert.That(source.Collectors, Is.EqualTo(4));
            Assert.That(generators.GetFuel(hole), Is.LessThan(before));
            Assert.That(SEntMan.GetComponent<PowerSupplierComponent>(hole).MaxSupply, Is.Zero);
            foreach (var collector in collectors)
                Assert.That(SEntMan.GetComponent<PowerSupplierComponent>(collector).MaxSupply, Is.EqualTo(300000).Within(1));
            gravity.Update(1);
            Assert.That(source.HarvestedPower, Is.Zero, "The same fuel payment cannot be harvested twice.");
            foreach (var collector in collectors)
                Assert.That(SEntMan.GetComponent<PowerSupplierComponent>(collector).Enabled, Is.False);
            generators.Update(1);
            transform.Unanchor(collectors[0]);
            gravity.Update(1);
            Assert.That(SEntMan.GetComponent<PowerSupplierComponent>(collectors[0]).MaxSupply, Is.Zero);
            Assert.That(source.HarvestedPower, Is.LessThanOrEqualTo(source.AvailablePower));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task FieldFailureLeavesARealGravityWellAfterElectricalOutputStops()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var hole = SEntMan.SpawnEntity("IndustrialMicroBlackHoleGenerator", map.GridCoords);
            var transform = Server.System<SharedTransformSystem>();
            transform.AnchorEntity(hole);
            var source = SEntMan.GetComponent<GravityPowerSourceComponent>(hole);
            source.ResidualActivity = 1;
            var plant = SEntMan.GetComponent<PowerPlantComponent>(hole);
            plant.Tripped = true;
            plant.Condition = PowerPlantCondition.MissingField;
            var loose = SEntMan.SpawnEntity("SheetSteel", new EntityCoordinates(map.Grid.Owner, 2.5f, .5f));
            var swallowed = SEntMan.SpawnEntity("SheetSteel", new EntityCoordinates(map.Grid.Owner, .6f, .5f));
            var gravity = Server.System<GravityPowerSystem>();
            gravity.Update(.5f);
            Assert.That(SEntMan.IsQueuedForDeletion(swallowed), Is.True, "Loose items crossing the horizon must not accumulate forever.");
            var well = SEntMan.GetComponent<GravityWellComponent>(hole);
            Assert.That(source.AvailablePower, Is.Zero);
            Assert.That(source.ResidualActivity, Is.GreaterThan(.9f));
            Assert.That(well.BaseRadialAcceleration, Is.GreaterThan(30));
            Assert.That(SEntMan.GetComponent<MicroBlackHoleVisualComponent>(hole).Instability, Is.EqualTo(1));
            Server.System<GravityWellSystem>().GravPulse(hole, well.MaxRange, well.MinRange,
                well.BaseRadialAcceleration * .5f, well.BaseTangentialAcceleration * .5f);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(loose).LinearVelocity.X, Is.LessThan(0));
            for (var i = 0; i < 20; i++) gravity.Update(.5f);
            Assert.That(source.ResidualActivity, Is.Zero);
            Assert.That(well.MaxRange, Is.LessThan(0));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
