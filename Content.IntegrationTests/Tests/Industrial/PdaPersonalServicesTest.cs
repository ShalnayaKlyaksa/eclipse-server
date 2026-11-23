using System;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.PDA;
using Content.Server.Research.Components;
using Content.Server.Research.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Cargo.Components;
using Content.Shared.Interaction;
using Content.Shared.PDA;
using Content.Shared.Research.Components;
using Content.Shared.Stacks;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class PdaPersonalServicesTest : GameTest
{
    [Test]
    public async Task InitialCapitalSalaryAndTransfersAreAuthoritativeAndIdempotent()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ResearchSystem>();
            var bank = SEntMan.System<PdaPersonalServicesSystem>();
            var domain = SEntMan.SpawnEntity(null, map.GridCoords);
            var anna = research.IssueCrewAccount(domain, SEntMan.SpawnEntity(null, map.GridCoords), "Scientist", "Anna", "anna");
            var boris = research.IssueCrewAccount(domain, SEntMan.SpawnEntity(null, map.GridCoords), "StationEngineer", "Boris", "boris");
            bank.RegisterEmployee(domain, anna.Username);
            bank.RegisterEmployee(domain, anna.Username);
            Assert.That(bank.Balance(domain, anna.Username), Is.EqualTo(1000));
            Assert.That(bank.Balance(domain, boris.Username), Is.EqualTo(1000));
            var request = Guid.NewGuid();
            Assert.That(bank.TryTransfer(domain, anna.Username, boris.Username, 250, request, "Lunch"), Is.True);
            Assert.That(bank.TryTransfer(domain, anna.Username, boris.Username, 250, request, "Lunch"), Is.True);
            Assert.That(bank.Balance(domain, anna.Username), Is.EqualTo(750));
            Assert.That(bank.Balance(domain, boris.Username), Is.EqualTo(1250));
            Assert.That(bank.TryTransfer(domain, anna.Username, boris.Username, -1, Guid.NewGuid(), ""), Is.False);
            Assert.That(bank.TryTransfer(domain, anna.Username, boris.Username, 9999, Guid.NewGuid(), ""), Is.False);
            Assert.That(bank.TryTransfer(domain, anna.Username, anna.Username, 10, Guid.NewGuid(), ""), Is.False);
            Assert.That(bank.TryTransfer(domain, anna.Username, boris.Username, 10, Guid.Empty, ""), Is.False);
            var account = SEntMan.GetComponent<PdaPersonalServicesComponent>(domain).Accounts[anna.Username];
            account.NextSalary = SGameTiming.CurTime - TimeSpan.FromSeconds(1);
            bank.Update(0);
            Assert.That(bank.Balance(domain, anna.Username), Is.EqualTo(970));
            bank.Update(0);
            Assert.That(bank.Balance(domain, anna.Username), Is.EqualTo(970));
            Assert.That(account.Transactions.Count(entry => entry.Amount == 1000), Is.EqualTo(1));
            Assert.That(bank.AdjustCredits(domain, boris.Username, int.MaxValue - 1250, "Test ceiling"), Is.True);
            Assert.That(bank.TryTransfer(domain, anna.Username, boris.Username, 1, Guid.NewGuid(), ""), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task AtmUsesTheSameCashAsEverySupplyDepartmentAndCannotDuplicateDeposits()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ResearchSystem>();
            var bank = SEntMan.System<PdaPersonalServicesSystem>();
            var atm = SEntMan.System<PdaAtmSystem>();
            var domain = SEntMan.SpawnEntity(null, map.GridCoords);
            SEntMan.EnsureComponent<StationDataComponent>(domain);
            SEntMan.System<Content.Server.Station.Systems.StationSystem>().AddGridToStation(domain, map.Grid.Owner);
            var departmental = SEntMan.EnsureComponent<StationBankAccountComponent>(domain);
            var actor = SEntMan.SpawnEntity(null, map.GridCoords);
            var employee = research.IssueCrewAccount(domain, actor, "Scientist", "Anna", "anna");
            var machine = SEntMan.SpawnEntity("ImperialPersonalATM", map.GridCoords);
            Assert.That(atm.Withdraw(domain, employee.Username, machine, 150, out var cash), Is.True);
            Assert.That(bank.Balance(domain, employee.Username), Is.EqualTo(850));
            Assert.That(SEntMan.GetComponent<StackComponent>(cash!.Value).StackTypeId.Id, Is.EqualTo("Credit"));
            Assert.That(atm.Deposit(domain, employee.Username, cash.Value, 150), Is.True);
            Assert.That(bank.Balance(domain, employee.Username), Is.EqualTo(1000));
            Assert.That(atm.Deposit(domain, employee.Username, cash.Value, 150), Is.False);
            Assert.That(atm.Deposit(domain, "missing", cash.Value, 1), Is.False);
            Assert.That(atm.Withdraw(domain, employee.Username, machine, 500, out cash), Is.True);
            var console = SEntMan.SpawnEntity("ComputerCargoOrdersScience", map.GridCoords);
            var previous = departmental.Accounts["Science"];
            var interaction = new InteractUsingEvent(actor, cash!.Value, console, map.GridCoords);
            SEntMan.EventBus.RaiseLocalEvent(console, interaction);
            Assert.That(interaction.Handled, Is.True);
            Assert.That(departmental.Accounts["Science"], Is.EqualTo(previous + 500));
            Assert.That(bank.Balance(domain, employee.Username), Is.EqualTo(500));
            // The reverse path produces standard Credit cash again, which an ATM can deposit.
            SEntMan.EnsureComponent<AccessComponent>(actor).Tags.Add("Research");
            var withdraw = new CargoConsoleWithdrawFundsMessage(null, 100) { Actor = actor };
            SEntMan.EventBus.RaiseLocalEvent(console, withdraw);
            Assert.That(departmental.Accounts["Science"], Is.EqualTo(previous + 400));
            var query = SEntMan.EntityQueryEnumerator<CashComponent, StackComponent>();
            EntityUid? dispensed = null;
            while (query.MoveNext(out var uid, out _, out var stack)) if (stack.Count == 100) dispensed = uid;
            Assert.That(dispensed, Is.Not.Null);
            Assert.That(atm.Deposit(domain, employee.Username, dispensed!.Value, 100), Is.True);
            Assert.That(bank.Balance(domain, employee.Username), Is.EqualTo(600));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task MaxResolvesPublicIdsDeliversToTheCorrectAccountAndHonorsBlocking()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ResearchSystem>();
            var bank = SEntMan.System<PdaPersonalServicesSystem>();
            var domain = SEntMan.SpawnEntity(null, map.GridCoords);
            var anna = research.IssueCrewAccount(domain, SEntMan.SpawnEntity(null, map.GridCoords), "Scientist", "Anna", "anna");
            var boris = research.IssueCrewAccount(domain, SEntMan.SpawnEntity(null, map.GridCoords), "StationEngineer", "Boris", "boris");
            var a = SEntMan.SpawnEntity("PassengerPDA", map.GridCoords);
            var b = SEntMan.SpawnEntity("PassengerPDA", map.GridCoords);
            research.AssignMailbox(a, domain, anna.Username); research.AssignMailbox(b, domain, boris.Username);
            void Action(EntityUid pda, PdaServiceAction action, string peer, string text = "") =>
                SEntMan.EventBus.RaiseLocalEvent(pda, new PdaServicesActionMessage(action, peer, text) { UiKey = PdaUiKey.Key });
            Action(a, PdaServiceAction.AddContact, "BORIS");
            Action(a, PdaServiceAction.SendChat, "boris", "Meet me in the lab [color=red]now[/color]");
            var service = SEntMan.GetComponent<PdaPersonalServicesComponent>(domain);
            Assert.That(service.Accounts[boris.Username].Messages.Single().Text, Does.Contain("Meet me in the lab"));
            Assert.That(service.Accounts[boris.Username].Unread, Does.Contain(anna.Username));
            Assert.That(service.Accounts[anna.Username].Messages.Single().To, Is.EqualTo(boris.Username));
            Action(b, PdaServiceAction.SendChat, "anna", "Received. See you there.");
            Assert.That(service.Accounts[anna.Username].Messages.Count, Is.EqualTo(2));
            Action(b, PdaServiceAction.ToggleBlock, "anna");
            service.Accounts[anna.Username].NextMessage = TimeSpan.Zero;
            Action(a, PdaServiceAction.SendChat, "boris", "Blocked message");
            Assert.That(service.Accounts[boris.Username].Messages.Count, Is.EqualTo(2));
            Action(a, PdaServiceAction.SendChat, "missing", "Unknown user");
            Assert.That(service.Accounts[anna.Username].Messages.Count, Is.EqualTo(2));
            Assert.That(bank.Balance(domain, anna.Username), Is.EqualTo(1000));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task MailHasSentCopiesReadFlagsAndRecipientScopedDeletion()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ResearchSystem>();
            var domain = SEntMan.SpawnEntity(null, map.GridCoords);
            var anna = research.IssueCrewAccount(domain, SEntMan.SpawnEntity(null, map.GridCoords), "Scientist", "Anna", "anna");
            var boris = research.IssueCrewAccount(domain, SEntMan.SpawnEntity(null, map.GridCoords), "StationEngineer", "Boris", "boris");
            var a = SEntMan.SpawnEntity("PassengerPDA", map.GridCoords); var b = SEntMan.SpawnEntity("PassengerPDA", map.GridCoords);
            research.AssignMailbox(a, domain, anna.Username); research.AssignMailbox(b, domain, boris.Username);
            Assert.That(research.HandlePersonalMail(a, new ResearchMailActionMessage { Action = ResearchMailAction.Send, Recipient = "boris", Subject = "Meeting", Body = "The meeting starts at noon." }), Is.EqualTo("pda-mail-sent"));
            var recipient = research.GetResearchInbox(b);
            Assert.That(recipient.Recipient, Is.EqualTo("boris"));
            Assert.That(recipient.Messages.Single().SenderId, Is.EqualTo("anna"));
            Assert.That(research.GetResearchInbox(a).Sent.Single().RecipientId, Is.EqualTo("boris"));
            var id = recipient.Messages.Single().Id;
            Assert.That(research.HandlePersonalMail(a, new ResearchMailActionMessage { Action = ResearchMailAction.Delete, Id = id }), Is.EqualTo("pda-services-invalid"));
            research.HandlePersonalMail(b, new ResearchMailActionMessage { Action = ResearchMailAction.Read, Id = id });
            Assert.That(research.GetResearchInbox(b).Messages.Single().Read, Is.True);
            research.HandlePersonalMail(b, new ResearchMailActionMessage { Action = ResearchMailAction.Delete, Id = id });
            Assert.That(research.GetResearchInbox(b).Messages, Is.Empty);
            Assert.That(research.GetResearchInbox(a).Sent, Has.Length.EqualTo(1));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
