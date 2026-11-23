using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Eclipse.Industrial;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class IndustrialPortInteractionTest : GameTest
{
    [Test]
    public async Task PortBadgesMatchPipeFacesOnRotatedMachinesAndGrids()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var transform = Server.System<SharedTransformSystem>();
            var maps = Server.System<SharedMapSystem>();
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(0, 1), map.Tile.Tile);
            var machine = SEntMan.SpawnEntity("BasicIndustrialCrusher", map.GridCoords);
            var pipe = SEntMan.SpawnEntity("LpItemPipe", new EntityCoordinates(map.Grid, 0.5f, 1.5f));
            var xform = SEntMan.GetComponent<TransformComponent>(machine);
            foreach (var gridAngle in new[] { 0, 90, 180 })
            {
                transform.SetLocalRotation(map.Grid.Owner, Angle.FromDegrees(gridAngle));
                foreach (var machineAngle in new[] { 0, 90, 180, 270 })
                {
                    transform.SetLocalRotation(machine, Angle.FromDegrees(machineAngle));
                    Assert.That(IndustrialProcessorConnectionHelper.IsPipeOnProcessorFace(machine, pipe,
                        Direction.North, SEntMan, maps), Is.True);
                    foreach (var (direction, bounds) in IndustrialPortClickZones.GetFaceZones(machine, SEntMan))
                    {
                        var center = IndustrialPortClickZones.GetFaceZoneCenter(direction);
                        Assert.That(bounds.Contains(center), Is.True);
                        var click = new EntityCoordinates(map.Grid, xform.LocalPosition + center);
                        Assert.That(IndustrialPortClickZones.TryGetClickedFace(machine, click, SEntMan, transform,
                            out var clicked), Is.True);
                        Assert.That(clicked, Is.EqualTo(direction),
                            $"Grid {gridAngle}°, sprite {machineAngle}°: badge must match the adjacent pipe face.");
                    }
                    Assert.That(IndustrialPortClickZones.TryGetClickedFace(machine, xform.Coordinates,
                        SEntMan, transform, out _), Is.False, "The unmarked centre must not cycle the north port.");
                }
            }
            foreach (var point in new[] { Vector2.Zero, new Vector2(0.45f, 0.45f), new Vector2(0, 0.6f) })
                Assert.That(IndustrialPortClickZones.TryGetLocalFace(point, out _), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
