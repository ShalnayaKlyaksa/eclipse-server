using System;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.CartridgeLoader.Cartridges;
using Content.Server.PDA;
using Content.Server.Research.Systems;
using Content.Shared.CartridgeLoader.Cartridges;
using Content.Shared.PDA;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class PdaMessengerTest : GameTest
{
    private async Task Check(Action action)
    { try { await Server.WaitAssertion(action); } catch (Exception error) { TestContext.Progress.WriteLine(error); throw; } }

    private (EntityUid Domain, EntityUid[] Devices, string[] Users) Employees(Robust.Shared.Map.EntityCoordinates coords)
    {
        var domain = SEntMan.SpawnEntity(null, coords);
        var research = SEntMan.System<ResearchSystem>();
        var names = new[] { "anna", "boris", "clara", "eve" };
        var devices = new EntityUid[4]; var users = new string[4];
        for (var i = 0; i < names.Length; i++)
        {
            var employee = research.IssueCrewAccount(domain, SEntMan.SpawnEntity(null, coords), "Scientist", names[i], names[i]);
            devices[i] = SEntMan.SpawnEntity("PassengerPDA", coords); users[i] = employee.Username;
            research.AssignMailbox(devices[i], domain, employee.Username);
        }
        return (domain, devices, users);
    }

    [Test]
    public async Task DirectChatsHaveReadReceiptsRepliesEditsDeletionAndIdempotentSending()
    {
        var map = await Pair.CreateTestMap();
        await Check(() =>
        {
            var (domain, devices, users) = Employees(map.GridCoords);
            var system = SEntMan.System<PdaPersonalServicesSystem>();
            Assert.That(system.PerformMessengerAction(devices[0], new(PdaMessengerAction.OpenDirect, text: "BORIS")), Is.EqualTo("pda-orbit-done"));
            var send = new PdaMessengerActionMessage(PdaMessengerAction.Send, "d:boris", "Первое сообщение", request: Guid.NewGuid());
            Assert.That(system.PerformMessengerAction(devices[0], send), Is.EqualTo("pda-services-message-sent"));
            Assert.That(system.PerformMessengerAction(devices[0], send), Is.EqualTo("pda-orbit-done"));
            var unread = system.MessengerState(devices[1]);
            Assert.That(unread.Chats.Single().Unread, Is.EqualTo(1));
            var incoming = system.MessengerState(devices[1], "d:anna", true);
            Assert.That(incoming.Messages.Length, Is.EqualTo(1));
            var first = incoming.Messages.Single().Id;
            Assert.That(system.MessengerState(devices[0], "d:boris").Messages.Single().Read, Is.True);
            Assert.That(system.PerformMessengerAction(devices[1], new(PdaMessengerAction.Send, "d:anna", "Ответ", message: first)), Is.EqualTo("pda-services-message-sent"));
            Assert.That(system.MessengerState(devices[0]).Messages.Last().ReplyTo, Is.EqualTo(first));
            Assert.That(system.PerformMessengerAction(devices[1], new(PdaMessengerAction.Edit, "d:anna", "Подмена", message: first)), Is.EqualTo("pda-orbit-owner-only"));
            Assert.That(system.PerformMessengerAction(devices[0], new(PdaMessengerAction.Edit, "d:boris", "Исправленный текст", message: first)), Is.EqualTo("pda-orbit-done"));
            Assert.That(system.MessengerState(devices[1]).Messages.First().Text, Is.EqualTo("Исправленный текст"));
            Assert.That(system.MessengerState(devices[1]).Messages.First().Edited, Is.True);
            Assert.That(system.PerformMessengerAction(devices[0], new(PdaMessengerAction.Delete, "d:boris", message: first)), Is.EqualTo("pda-orbit-done"));
            Assert.That(system.MessengerState(devices[1]).Messages.Length, Is.EqualTo(1));
            Assert.That(system.PerformMessengerAction(devices[1], new(PdaMessengerAction.ToggleBlock, "d:anna")), Is.EqualTo("pda-orbit-done"));
            SEntMan.GetComponent<PdaPersonalServicesComponent>(domain).Accounts[users[0]].NextMessage = TimeSpan.Zero;
            Assert.That(system.PerformMessengerAction(devices[0], new(PdaMessengerAction.Send, "d:boris", "Не доставлять")), Is.EqualTo("pda-services-message-unavailable"));
            Assert.That(system.MessengerState(devices[1]).Messages.Length, Is.EqualTo(1));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task GroupsEnforceMembershipOwnershipAndTransferOwnershipOnLeaving()
    {
        var map = await Pair.CreateTestMap();
        await Check(() =>
        {
            var (domain, devices, _) = Employees(map.GridCoords);
            var system = SEntMan.System<PdaPersonalServicesSystem>();
            Assert.That(system.PerformMessengerAction(devices[0], new(PdaMessengerAction.CreateGroup, text: "Научный отдел", members: ["boris", "clara"])), Is.EqualTo("pda-orbit-done"));
            var key = system.MessengerState(devices[0]).Selected;
            Assert.That(system.MessengerState(devices[1], key).Members.Length, Is.EqualTo(3));
            Assert.That(system.MessengerState(devices[3], key).Selected, Is.Empty);
            Assert.That(system.MessengerState(devices[3], key).Members, Is.Empty);
            Assert.That(system.PerformMessengerAction(devices[1], new(PdaMessengerAction.Send, key, "Образцы доставлены")), Is.EqualTo("pda-services-message-sent"));
            Assert.That(system.MessengerState(devices[3], key).Messages, Is.Empty, "A guessed group ID must not expose messages.");
            Assert.That(system.PerformMessengerAction(devices[3], new(PdaMessengerAction.Send, key, "Вторжение")), Is.EqualTo("pda-orbit-unavailable"));
            Assert.That(system.PerformMessengerAction(devices[1], new(PdaMessengerAction.AddMembers, key, members: ["eve"])), Is.EqualTo("pda-orbit-owner-only"));
            Assert.That(system.PerformMessengerAction(devices[0], new(PdaMessengerAction.AddMembers, key, members: ["eve"])), Is.EqualTo("pda-orbit-done"));
            Assert.That(system.MessengerState(devices[3], key).Messages.Length, Is.EqualTo(1));
            Assert.That(system.PerformMessengerAction(devices[0], new(PdaMessengerAction.RemoveMember, key, "eve")), Is.EqualTo("pda-orbit-done"));
            Assert.That(system.MessengerState(devices[3], key).Chats, Is.Empty);
            Assert.That(system.PerformMessengerAction(devices[0], new(PdaMessengerAction.RenameGroup, key, "Лаборатория")), Is.EqualTo("pda-orbit-done"));
            Assert.That(system.MessengerState(devices[1], key).Title, Is.EqualTo("Лаборатория"));
            Assert.That(system.PerformMessengerAction(devices[1], new(PdaMessengerAction.LeaveGroup, key)), Is.EqualTo("pda-orbit-done"));
            Assert.That(system.PerformMessengerAction(devices[0], new(PdaMessengerAction.LeaveGroup, key)), Is.EqualTo("pda-orbit-done"));
            Assert.That(system.MessengerState(devices[2], key).Owner, Is.True);
            Assert.That(system.PerformMessengerAction(devices[2], new(PdaMessengerAction.LeaveGroup, key)), Is.EqualTo("pda-orbit-done"));
            Assert.That(SEntMan.GetComponent<PdaPersonalServicesComponent>(domain).Groups, Is.Empty);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task InvalidInvitationsAndMessageRepliesAreAtomicAndDoNotLeakGroups()
    {
        var map = await Pair.CreateTestMap();
        await Check(() =>
        {
            var (domain, devices, _) = Employees(map.GridCoords);
            var system = SEntMan.System<PdaPersonalServicesSystem>();
            Assert.That(system.PerformMessengerAction(devices[0], new(PdaMessengerAction.CreateGroup, text: "Invalid", members: ["boris", "missing"])), Is.EqualTo("pda-services-recipient-not-found"));
            Assert.That(SEntMan.GetComponent<PdaPersonalServicesComponent>(domain).Groups, Is.Empty);
            Assert.That(system.PerformMessengerAction(devices[0], new(PdaMessengerAction.CreateGroup, text: "Lab", members: ["boris"])), Is.EqualTo("pda-orbit-done"));
            var key = system.MessengerState(devices[0]).Selected;
            Assert.That(system.PerformMessengerAction(devices[0], new(PdaMessengerAction.AddMembers, key, members: ["clara", "missing"])), Is.EqualTo("pda-services-recipient-not-found"));
            Assert.That(system.MessengerState(devices[0]).Members.Length, Is.EqualTo(2));
            Assert.That(system.PerformMessengerAction(devices[0], new(PdaMessengerAction.Send, key, "Invalid quote", message: long.MaxValue)), Is.EqualTo("pda-orbit-message-missing"));
            Assert.That(system.PerformMessengerAction(devices[0], new(PdaMessengerAction.Send, key, new string('x', 1001))), Is.EqualTo("pda-services-invalid"));
            Assert.That(system.MessengerState(devices[0]).Messages, Is.Empty);
            Assert.That(system.MessengerState(devices[2], key).Members, Is.Empty);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task NotesHaveStableIdsAndRejectStaleEditsWithoutLosingContent()
    {
        await Check(() =>
        {
            var system = SEntMan.System<NotekeeperCartridgeSystem>();
            var notes = new NotekeeperCartridgeComponent();
            Assert.That(system.Apply(notes, new(NotekeeperUiAction.Add, "First draft", "Lab notes"), out var first), Is.EqualTo("pda-notes-saved"));
            Assert.That(system.Apply(notes, new(NotekeeperUiAction.Add, "First draft", "Other note"), out var other), Is.EqualTo("pda-notes-saved"));
            Assert.That(first, Is.Not.EqualTo(other));
            Assert.That(system.Apply(notes, new(NotekeeperUiAction.Edit, "Edited body", "Lab notes", first, 0), out _), Is.EqualTo("pda-notes-saved"));
            Assert.That(system.Apply(notes, new(NotekeeperUiAction.Edit, "Stale overwrite", "Bad", first, 0), out _), Is.EqualTo("pda-notes-conflict"));
            Assert.That(notes.Entries.Single(entry => entry.Id == first).Body, Is.EqualTo("Edited body"));
            Assert.That(system.Apply(notes, new(NotekeeperUiAction.Remove, "", id: other), out _), Is.EqualTo("pda-notes-saved"));
            Assert.That(notes.Entries.Single().Id, Is.EqualTo(first));
            Assert.That(system.Apply(notes, new(NotekeeperUiAction.Add, new string('x', 4001), "Huge"), out _), Is.EqualTo("pda-services-invalid"));
        });
    }
}
