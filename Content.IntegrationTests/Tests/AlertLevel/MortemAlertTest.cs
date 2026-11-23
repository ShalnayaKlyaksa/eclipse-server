#nullable enable
using System.Linq;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Fixtures;
using Content.Server.AlertLevel;
using Content.Server.Station.Systems;
using Content.Server._Eclipse.ProtoCore;
using Content.Server._Eclipse.ProtoCore.Components;
using Content.Shared.Station.Components;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests.AlertLevel;

public sealed class MortemAlertTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Dirty = true, Connected = true };

    [Test]
    public async Task AnnouncementPrecedesTheCodeAndMeltdownsCannotDowngradeIt()
    {
        var map = await Pair.CreateTestMap();
        EntityUid station = default;
        var preannouncement = "";
        var announcement = "";
        var death = "";
        var sender = "";
        var historyStart = 0;

        await Server.WaitAssertion(() =>
        {
            station = SEntMan.SpawnEntity(null, map.GridCoords);
            SEntMan.AddComponent<StationDataComponent>(station);
            Server.System<StationSystem>().AddGridToStation(station, map.Grid);
            var alert = SEntMan.AddComponent<AlertLevelComponent>(station);
            alert.AlertLevelPrototype = AlertLevelSystem.DefaultAlertLevelSet;
            alert.AlertLevels = Server.ProtoMan.Index<AlertLevelPrototype>(alert.AlertLevelPrototype);
            alert.CurrentLevel = "alpha";
            var viewer = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            Server.PlayerMan.SetAttachedEntity(ServerSession!, viewer);
            var loc = Server.ResolveDependency<ILocalizationManager>();
            death = loc.GetString("alert-level-mortem-death");
            sender = loc.GetString("alert-level-mortem-sender");
            preannouncement = loc.GetString("alert-level-mortem-preannouncement", ("death", death));
            announcement = loc.GetString("alert-level-announcement",
                ("name", loc.GetString("alert-level-mortem").ToLower()),
                ("announcement", loc.GetString("alert-level-mortem-announcement")));
        });
        await Pair.RunTicksSync(5);
        await Client.WaitAssertion(() =>
            historyStart = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>().History.Count);

        await Server.WaitAssertion(() =>
        {
            var system = Server.System<AlertLevelSystem>();
            system.SetLevel(station, "mortem", false, true);
            Assert.That(system.GetLevel(station), Is.EqualTo("alpha"), "Crew cannot select Mortem.");
            system.SetLevel(station, "mortem", false, true, true, true);
            system.SetLevel(station, "mortem", false, true, true, true);
            system.SetLevel(station, "alpha", false, true);
            system.SetLevel(station, "delta", false, true, true, true);
            system.SetLevel(station, "alpha", false, true, true);
            Assert.That(system.GetLevel(station), Is.EqualTo("mortem"));
        });
        await Pair.RunTicksSync(5);
        await Client.WaitAssertion(() =>
        {
            var messages = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>()
                .History.Skip(historyStart).Select(entry => entry.Msg)
                .Where(message => message.Message == preannouncement || message.Message == announcement).ToArray();
            Assert.That(messages.Select(message => message.Message), Is.EqualTo(new[] { preannouncement, announcement }),
                "A rejected or repeated change must not repeat the Zero Shift preannouncement.");
            Assert.That(messages[0].WrappedMessage, Does.Contain(sender));
            Assert.That(messages[0].WrappedMessage, Does.Contain($"[blood=\"{death}\"]"));
            Assert.That(messages[0].Message, Does.Not.Contain("[blood"), "Logs and speech retain the plain sentence.");
        });

        await Server.WaitAssertion(() =>
        {
            var alerts = Server.System<AlertLevelSystem>();
            var coreUid = SEntMan.SpawnEntity("ProtoCore", map.GridCoords);
            var core = SEntMan.GetComponent<ProtoCoreComponent>(coreUid);
            var system = Server.System<ProtoCoreSystem>();
            system.StartMeltdown((coreUid, core));
            Assert.That(alerts.GetLevel(station), Is.EqualTo("mortem"));
            alerts.SetLevel(station, "alpha", false, false, true, overrideMortem: true);
            system.StartMeltdown((coreUid, core), skipEmergencyShuttle: true);
            Assert.That(alerts.GetLevel(station), Is.EqualTo("mortem"), "A Zero Shift meltdown selects Mortem.");
            Assert.That(core.SkipEmergencyShuttle, Is.True);
        });
    }
}
