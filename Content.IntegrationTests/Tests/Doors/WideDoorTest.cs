using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Doors.Systems;
using Content.Shared.Doors.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests.Doors;

public sealed class WideDoorTest : GameTest
{
    [TestCase("EclipseBlastDoor2x1", 2)]
    [TestCase("EclipseBlastDoor3x1", 3)]
    [TestCase("EclipseWoodDoor2x1", 2)]
    [TestCase("EclipseWoodDoor3x1", 3)]
    public async Task BlocksWholeOpening(string prototype, int width)
    {
        var map = await Pair.CreateTestMap();
        var maps = SEntMan.System<SharedMapSystem>();
        var atmosphere = SEntMan.System<AtmosphereSystem>();
        var doors = SEntMan.System<DoorSystem>();
        var physics = SEntMan.System<SharedPhysicsSystem>();
        var transforms = SEntMan.System<SharedTransformSystem>();
        var anchor = map.Tile.GridIndices;
        var east = anchor + new Vector2i(1, 0);
        var west = anchor + new Vector2i(-1, 0);
        var north = anchor + new Vector2i(0, 1);
        var south = anchor + new Vector2i(0, -1);
        var door = EntityUid.Invalid;
        DoorComponent doorComponent = null;

        await Server.WaitAssertion(() =>
        {
            foreach (var tile in new[] { east, west, north, south })
                maps.SetTile(map.Grid, tile, map.Tile.Tile);

            door = SEntMan.SpawnAtPosition(prototype, maps.GridTileToLocal(map.Grid, map.Grid.Comp, anchor));
            doorComponent = SEntMan.GetComponent<DoorComponent>(door);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(physics.GetWorldAABB(door).Width, Is.EqualTo(width).Within(0.001f));
                Assert.That(atmosphere.IsTileAirBlocked(map.Grid, anchor), Is.True);
                Assert.That(atmosphere.IsTileAirBlocked(map.Grid, east), Is.True);
                Assert.That(atmosphere.IsTileAirBlocked(map.Grid, west), Is.EqualTo(width == 3));
                Assert.That(atmosphere.IsTileAirBlocked(map.Grid, north), Is.False);
                Assert.That(atmosphere.IsTileAirBlocked(map.Grid, south), Is.False);
            }

            Assert.That(doors.TryOpen(door), Is.True);
        });

        await PoolManager.WaitUntil(Server, () => doorComponent.State == DoorState.Open);
        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SEntMan.GetComponent<PhysicsComponent>(door).CanCollide, Is.False);
                Assert.That(atmosphere.IsTileAirBlocked(map.Grid, anchor), Is.False);
                Assert.That(atmosphere.IsTileAirBlocked(map.Grid, east), Is.False);
                Assert.That(atmosphere.IsTileAirBlocked(map.Grid, west), Is.False);
            }

            transforms.SetLocalRotation(door, Angle.FromDegrees(90));
            Assert.That(doors.TryClose(door), Is.True);
        });

        await PoolManager.WaitUntil(Server, () => doorComponent.State == DoorState.Closed);
        await Server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(physics.GetWorldAABB(door).Height, Is.EqualTo(width).Within(0.001f));
                Assert.That(atmosphere.IsTileAirBlocked(map.Grid, anchor), Is.True);
                Assert.That(atmosphere.IsTileAirBlocked(map.Grid, north), Is.True);
                Assert.That(atmosphere.IsTileAirBlocked(map.Grid, south), Is.EqualTo(width == 3));
                Assert.That(atmosphere.IsTileAirBlocked(map.Grid, east), Is.False);
                Assert.That(atmosphere.IsTileAirBlocked(map.Grid, west), Is.False);
            }

            SEntMan.DeleteEntity(door);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(atmosphere.IsTileAirBlocked(map.Grid, north), Is.False);
                Assert.That(atmosphere.IsTileAirBlocked(map.Grid, south), Is.False);
            }
        });
    }
}
