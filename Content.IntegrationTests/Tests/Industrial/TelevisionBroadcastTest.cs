using System;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.WallDisplays;
using Content.Server.Station.Systems;
using Content.Server.SurveillanceCamera;
using Content.Server.Power.Components;
using Content.Shared._Eclipse.WallDisplays;
using Content.Shared.Station.Components;
using Content.Shared.Power;
using Content.Shared.Speech.Components;
using Content.Shared.SurveillanceCamera;
using Content.Shared.SurveillanceCamera.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Server.GameObjects;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class TelevisionBroadcastTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task TelevisionCanBeOpenedAcrossTheRoomButNotThroughWallsOrOutsideViewingRange()
    {
        var map = await Pair.CreateTestMap();
        EntityUid tv = default;
        EntityUid viewer = default;
        try
        {
            await Server.WaitAssertion(() =>
            {
                tv = SEntMan.SpawnEntity("EclipseWallTelevision", map.GridCoords);
                SEntMan.GetComponent<TelevisionComponent>(tv).Powered = true;
                var power = SEntMan.GetComponent<ApcPowerReceiverComponent>(tv); power.NeedsPower = false; power.Powered = true;
                viewer = SEntMan.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, new Vector2(8, 0)));
                Server.PlayerMan.SetAttachedEntity(ServerSession!, viewer);
                var system = SEntMan.System<TelevisionSystem>();
                Assert.That(system.TryWatch(tv, viewer), Is.True, "Watching a screen does not require standing within arm's reach.");
                Assert.That(SEntMan.System<UserInterfaceSystem>().IsUiOpen(tv, TelevisionUiKey.Key, viewer), Is.True);
                SEntMan.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid.Owner, new Vector2(4, 0)));
                Assert.That(system.TryWatch(tv, viewer), Is.False, "A wall blocks viewing.");
            });
            await Pair.RunTicksSync(65);
            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.System<UserInterfaceSystem>().IsUiOpen(tv, TelevisionUiKey.Key, viewer), Is.False, "Losing sight of the screen closes an existing viewing window.");
                SEntMan.System<SharedTransformSystem>().SetCoordinates(viewer, new EntityCoordinates(map.Grid.Owner, new Vector2(0, 13)));
                Assert.That(SEntMan.System<TelevisionSystem>().TryWatch(tv, viewer), Is.False);
            });
        }
        catch (Exception error) { TestContext.Progress.WriteLine(error); throw; }
    }

    [Test]
    public async Task PublicBroadcastsRegisterViewersAndSpeakersAndReleaseThemWhenStopped()
    {
        var map = await Pair.CreateTestMap();
        try
        {
            await Server.WaitAssertion(() =>
            {
                var station = SEntMan.SpawnEntity(null, map.GridCoords);
                SEntMan.EnsureComponent<StationDataComponent>(station);
                SEntMan.System<StationSystem>().AddGridToStation(station, map.Grid.Owner);
                var viewer = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
                Server.PlayerMan.SetAttachedEntity(ServerSession!, viewer);
                var system = SEntMan.System<TelevisionSystem>();
                var camera = SEntMan.SpawnEntity("EclipseReporterCamera", map.GridCoords);
                var broadcast = SEntMan.GetComponent<PublicBroadcastCameraComponent>(camera);
                var sensor = SEntMan.GetComponent<SurveillanceCameraComponent>(camera);
                var tv = SEntMan.SpawnEntity("EclipseWallTelevision", map.GridCoords);
                var receiver = SEntMan.GetComponent<TelevisionComponent>(tv);
                var secondTv = SEntMan.SpawnEntity("EclipseWallTelevision", map.GridCoords);
                var secondReceiver = SEntMan.GetComponent<TelevisionComponent>(secondTv);
                receiver.Powered = secondReceiver.Powered = true;

                Assert.That(system.TryTune(tv, receiver, TelevisionChannel.Broadcast, camera), Is.False, "Inactive camera cannot be selected.");
                Assert.That(system.SetOnAir(camera, broadcast, true, viewer), Is.True);
                Assert.That(SEntMan.HasComponent<ActiveListenerComponent>(camera), Is.True);
                Assert.That(system.TryTune(tv, receiver, TelevisionChannel.Broadcast, camera), Is.True);
                Assert.That(system.TryTune(secondTv, secondReceiver, TelevisionChannel.Broadcast, camera), Is.True);
                Assert.That(sensor.ActivePvsViewers, Does.Contain(viewer));
                Assert.That(sensor.ActiveMonitors, Does.Contain(tv).And.Contain(secondTv));

                Assert.That(system.TryTune(tv, receiver, TelevisionChannel.Flappy, null), Is.True);
                Assert.That(sensor.ActiveMonitors, Does.Not.Contain(tv));
                Assert.That(sensor.ActivePvsViewers, Does.Contain(viewer), "Another TV still uses this feed.");
                var powerLoss = new PowerChangedEvent(false, 0);
                SEntMan.EventBus.RaiseLocalEvent(secondTv, ref powerLoss);
                Assert.That(sensor.ActivePvsViewers, Is.Empty);
                Assert.That(sensor.ActiveMonitors, Is.Empty);
                Assert.That(system.TryTune(secondTv, secondReceiver, TelevisionChannel.Blocks, null), Is.False);

                receiver.Powered = true;
                Assert.That(system.TryTune(tv, receiver, TelevisionChannel.Broadcast, camera), Is.True);
                Assert.That(system.SetOnAir(camera, broadcast, false), Is.True);
                Assert.That(receiver.Camera, Is.Null);
                Assert.That(sensor.ActivePvsViewers, Is.Empty);
                Assert.That(sensor.ActiveMonitors, Is.Empty);
                Assert.That(SEntMan.GetComponent<SurveillanceCameraMicrophoneComponent>(camera).Enabled, Is.False);

                Assert.That(system.SetOnAir(camera, broadcast, true, viewer), Is.True);
                Assert.That(system.TryTune(tv, receiver, TelevisionChannel.Broadcast, camera), Is.True);
                var transforms = SEntMan.System<SharedTransformSystem>();
                transforms.SetCoordinates(viewer, new EntityCoordinates(map.Grid.Owner, new Vector2(30, 30)));
                system.RefreshAudience();
                Assert.That(sensor.ActivePvsViewers, Is.Empty, "Distant players do not receive the feed.");
                transforms.SetCoordinates(viewer, map.GridCoords);
                system.RefreshAudience();
                Assert.That(sensor.ActivePvsViewers, Does.Contain(viewer));
                SEntMan.System<SurveillanceCameraSystem>().SetActive(camera, false);
                system.RefreshAudience();
                Assert.That(broadcast.OnAir, Is.False, "External deactivation also stops the public broadcast.");
                Assert.That(receiver.Camera, Is.Null);
                Assert.That(sensor.ActivePvsViewers, Is.Empty);
                Assert.That(sensor.ActiveMonitors, Is.Empty);
                Assert.That(system.SetOnAir(camera, broadcast, true, viewer), Is.True);
                Assert.That(system.TryTune(tv, receiver, TelevisionChannel.Broadcast, camera), Is.True);
                SEntMan.DeleteEntity(tv);
                system.RefreshAudience();
                Assert.That(sensor.ActivePvsViewers, Is.Empty);
                Assert.That(sensor.ActiveMonitors, Is.Empty);
                SEntMan.DeleteEntity(camera);
            });
        }
        catch (Exception error) { TestContext.Progress.WriteLine(error); throw; }
    }

    [Test]
    public async Task PrivateForeignAndStowedCamerasCannotBeBroadcast()
    {
        var first = await Pair.CreateTestMap();
        var second = await Pair.CreateTestMap();
        try
        {
            await Server.WaitAssertion(() =>
            {
                foreach (var map in new[] { first, second })
                {
                    var station = SEntMan.SpawnEntity(null, map.GridCoords);
                    SEntMan.EnsureComponent<StationDataComponent>(station);
                    SEntMan.System<StationSystem>().AddGridToStation(station, map.Grid.Owner);
                }
                var system = SEntMan.System<TelevisionSystem>();
                var tv = SEntMan.SpawnEntity("EclipseWallTelevision", first.GridCoords);
                var receiver = SEntMan.GetComponent<TelevisionComponent>(tv);
                receiver.Powered = true;
                var privateCamera = SEntMan.SpawnEntity(null, first.GridCoords);
                SEntMan.EnsureComponent<SurveillanceCameraComponent>(privateCamera);
                SEntMan.System<SurveillanceCameraSystem>().SetActive(privateCamera, true);
                Assert.That(system.TryTune(tv, receiver, TelevisionChannel.Broadcast, privateCamera), Is.False);
                var foreign = SEntMan.SpawnEntity("EclipseReporterCamera", second.GridCoords);
                Assert.That(system.SetOnAir(foreign, SEntMan.GetComponent<PublicBroadcastCameraComponent>(foreign), true), Is.True);
                Assert.That(system.TryTune(tv, receiver, TelevisionChannel.Broadcast, foreign), Is.False);

                var camera = SEntMan.SpawnEntity("EclipseReporterCamera", first.GridCoords);
                Assert.That(system.SetOnAir(camera, SEntMan.GetComponent<PublicBroadcastCameraComponent>(camera), true), Is.True);
                var bag = SEntMan.SpawnEntity(null, first.GridCoords);
                var containers = SEntMan.System<SharedContainerSystem>();
                var storage = containers.EnsureContainer<Container>(bag, "test-storage");
                Assert.That(containers.Insert(camera, storage), Is.True);
                Assert.That(system.TryTune(tv, receiver, TelevisionChannel.Broadcast, camera), Is.False);
                containers.Remove(camera, storage);
                Assert.That(system.TryTune(tv, receiver, TelevisionChannel.Broadcast, camera), Is.True);
                SEntMan.DeleteEntity(camera);
                Assert.That(receiver.Camera, Is.Null);
            });
        }
        catch (Exception error) { TestContext.Progress.WriteLine(error); throw; }
    }
}
