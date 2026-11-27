#nullable enable annotations

using System;
using Content.IntegrationTests.Fixtures;
using Content.Server.Power.Components;
using Content.Server.Research.Components;
using Content.Server.Research.Systems;
using Content.Shared.Access.Components;
using Content.Shared.Inventory;
using Content.Shared.Paper;
using Content.Shared.Station.Components;
using System.Linq;
using Content.Shared.Power;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using System.Collections.Generic;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class ResearchTerminalSessionTest : GameTest
{
    private EntityUid Terminal(EntityCoordinates coordinates, EntityUid server)
    {
        var uid = SEntMan.SpawnEntity("ComputerResearchAndDevelopment", coordinates);
        SEntMan.GetComponent<ApcPowerReceiverComponent>(uid).Powered = true;
        SEntMan.GetComponent<ResearchClientComponent>(uid).Server = server;
        return uid;
    }

    private EntityUid Actor(EntityCoordinates coordinates, string? access = null)
    {
        var uid = SEntMan.SpawnEntity(null, coordinates);
        if (access != null) SEntMan.EnsureComponent<AccessComponent>(uid).Tags.Add(new(access));
        return uid;
    }

    private void Run(ResearchSystem research, EntityUid terminal, EntityUid actor, string command)
    {
        SEntMan.GetComponent<ResearchConsoleComponent>(terminal).NextCommand = TimeSpan.Zero;
        research.ExecuteTerminalCommand(terminal, actor, command);
    }

    [Test]
    public async Task CredentialsAreNetworkScopedAndClosingTheUiDoesNotTerminateThePhysicalSession()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ResearchSystem>();
            var server = SEntMan.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
            var otherServer = SEntMan.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
            var first = Terminal(map.GridCoords, server);
            var second = Terminal(map.GridCoords, server);
            var isolated = Terminal(map.GridCoords, otherServer);
            var scientist = Actor(map.GridCoords, "Research");
            var outsider = Actor(map.GridCoords);
            Assert.That(research.AuthenticateTerminal(first, outsider, "alice", "round-password", true), Is.False);
            SEntMan.GetComponent<ResearchConsoleComponent>(first).NextAuthentication = TimeSpan.Zero;
            var alice = research.IssueCrewAccount(server, scientist, "Scientist", "Alice");
            Assert.That(research.AuthenticateTerminal(first, scientist, alice.Username, alice.Password, false), Is.True);
            Assert.That(SEntMan.GetComponent<ResearchAccountsComponent>(server).Accounts[alice.Username].Administrator, Is.False);
            SEntMan.System<UserInterfaceSystem>().CloseUi(first, ResearchConsoleUiKey.Key);
            Assert.That(research.CanUseTerminal(first), Is.True, "Closing the physical screen must leave its account session open.");
            Assert.That(research.AuthenticateTerminal(second, outsider, alice.Username, alice.Password, false), Is.True,
                "Stolen game-world credentials must work without also requiring the scientist's ID.");
            Assert.That(research.AuthenticateTerminal(isolated, outsider, alice.Username, alice.Password, false), Is.False,
                "Another network must not accept this server's accounts.");
            Run(research, first, scientist, "exit");
            Assert.That(research.CanUseTerminal(first), Is.False);
            Assert.That(research.CanUseTerminal(second), Is.True, "Exit affects one physical terminal, not every session of the account.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task DirectorCanRevokeCredentialsAndRemotelyCloseSessionsButScientistsCannot()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ResearchSystem>();
            var server = SEntMan.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
            var directorTerminal = Terminal(map.GridCoords, server);
            var scientistTerminal = Terminal(map.GridCoords, server);
            var stolenTerminal = Terminal(map.GridCoords, server);
            var director = Actor(map.GridCoords, "ResearchDirector");
            var scientist = Actor(map.GridCoords, "Research");
            var outsider = Actor(map.GridCoords);
            var directorAccount = research.IssueCrewAccount(server, director, "ResearchDirector", "Director");
            var scientistAccount = research.IssueCrewAccount(server, scientist, "Scientist", "Scientist");
            Assert.That(research.AuthenticateTerminal(directorTerminal, director, directorAccount.Username, directorAccount.Password, false), Is.True);
            Assert.That(research.AuthenticateTerminal(scientistTerminal, scientist, scientistAccount.Username, scientistAccount.Password, false), Is.True);
            Assert.That(research.AuthenticateTerminal(stolenTerminal, outsider, scientistAccount.Username, scientistAccount.Password, false), Is.True);
            Run(research, directorTerminal, director, "terminals");
            var directorAddress = SEntMan.GetComponent<ResearchConsoleComponent>(directorTerminal).Address;
            var stolenAddress = SEntMan.GetComponent<ResearchConsoleComponent>(stolenTerminal).Address;
            Run(research, scientistTerminal, scientist, $"remote lab-{directorAddress} exit");
            Assert.That(research.CanUseTerminal(directorTerminal), Is.True);
            Run(research, directorTerminal, director, $"remote lab-{stolenAddress} exit");
            Assert.That(research.CanUseTerminal(stolenTerminal), Is.False);
            Assert.That(research.CanUseTerminal(scientistTerminal), Is.True);
            Run(research, directorTerminal, director, "revoke " + scientistAccount.Username);
            Assert.That(research.CanUseTerminal(scientistTerminal), Is.False);
            Assert.That(SEntMan.GetComponent<ResearchAccountsComponent>(server).Accounts.ContainsKey(scientistAccount.Username), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task AnalysisChecksEveryStageAwardsOncePerNetworkAndNeverBypassesResearchRequirements()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ResearchSystem>();
            var server = SEntMan.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
            var terminal = Terminal(map.GridCoords, server);
            var scientist = Actor(map.GridCoords, "Research");
            var analyst = research.IssueCrewAccount(server, scientist, "Scientist", "Analyst");
            Assert.That(research.AuthenticateTerminal(terminal, scientist, analyst.Username, analyst.Password, false), Is.True);
            research.ModifyServerPoints(server, 10000 - SEntMan.GetComponent<ResearchServerComponent>(server).Points);
            var console = SEntMan.GetComponent<ResearchConsoleComponent>(terminal);
            Assert.That(research.TryConsoleResearch(terminal, scientist, "IndustrialPerfectEquipment"), Is.False);
            Run(research, terminal, scientist, "analyze electronics");
            Assert.That(console.Analysis, Is.Not.Null);
            Run(research, terminal, scientist, "trace INVALID");
            Assert.That(console.Analysis, Is.Null);
            Assert.That(SEntMan.GetComponent<ResearchServerComponent>(server).Points, Is.EqualTo(10000));
            console.NextAnalysis = TimeSpan.Zero;
            Run(research, terminal, scientist, "analyze electronics");
            var analysis = console.Analysis!;
            Run(research, terminal, scientist, "trace " + (char) ('A' + analysis.Node));
            Run(research, terminal, scientist, "tune " + analysis.Offset);
            Run(research, terminal, scientist, "verify " + analysis.Checksum.ToString("X2"));
            Assert.That(console.Analysis, Is.Null);
            Assert.That(SEntMan.GetComponent<ResearchServerComponent>(server).Points, Is.EqualTo(10150));
            Assert.That(research.HasTechnology(server, "IndustrialElectronics"), Is.False, "Analysis awards points, never technology or prerequisite bypasses.");
            Run(research, terminal, scientist, "analyze electronics");
            Assert.That(console.Analysis, Is.Null);
            Assert.That(SEntMan.GetComponent<ResearchServerComponent>(server).Points, Is.EqualTo(10150));
            Assert.That(research.TryConsoleResearch(terminal, scientist, "IndustrialElectronics"), Is.True);
            Assert.That(SEntMan.GetComponent<ResearchServerComponent>(server).Points, Is.EqualTo(8650));
            var power = new PowerChangedEvent(false, 0);
            SEntMan.EventBus.RaiseLocalEvent(terminal, ref power);
            Assert.That(research.CanUseTerminal(terminal), Is.False, "Power loss clears the account, even after power is restored.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
    [Test]
    public async Task QueuedResearchWaitsForPointsAndSurvivesClosingTheScreen()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ResearchSystem>();
            var server = SEntMan.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
            var terminal = Terminal(map.GridCoords, server);
            research.RegisterClient(terminal, server);
            var actor = Actor(map.GridCoords, "Research");
            var account = research.IssueCrewAccount(server, actor, "Scientist", "Queue operator");
            Assert.That(research.AuthenticateTerminal(terminal, actor, account.Username, account.Password, false), Is.True);
            research.ModifyServerPoints(server, -SEntMan.GetComponent<ResearchServerComponent>(server).Points);
            Run(research, terminal, actor, "queue plan automation");
            var console = SEntMan.GetComponent<ResearchConsoleComponent>(terminal);
            Assert.That(console.PendingResearch.Count, Is.EqualTo(2));
            SEntMan.System<UserInterfaceSystem>().CloseUi(terminal, ResearchConsoleUiKey.Key);
            research.ModifyServerPoints(server, 1500);
            Assert.That(research.HasTechnology(server, "IndustrialElectronics"), Is.True);
            Assert.That(console.PendingResearch, Is.EqualTo(new[] { "IndustrialAutomation" }));
            research.ModifyServerPoints(server, 2500);
            Assert.That(research.HasTechnology(server, "IndustrialAutomation"), Is.True);
            Assert.That(console.PendingResearch, Is.Empty);
            Run(research, terminal, actor, "queue add processchemistry");
            Assert.That(console.PendingResearch.Count, Is.EqualTo(1));
            Run(research, terminal, actor, "exit");
            research.ModifyServerPoints(server, 3000);
            Assert.That(research.HasTechnology(server, "IndustrialProcessChemistry"), Is.False);
            Assert.That(console.PendingResearch, Is.Empty);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task ResearchProgressionHasNoCyclesOrOverlappingNodesAndEveryBranchReachesProduction()
    {
        await Server.WaitAssertion(() =>
        {
            var technologies = SProtoMan.EnumeratePrototypes<TechnologyPrototype>().ToDictionary(t => t.ID);
            var visible = technologies.Values.Where(t => !t.Hidden).ToArray();
            Assert.That(visible.Select(t => t.Position).Distinct().Count(), Is.EqualTo(visible.Length), "Research nodes must not occupy the same map position.");
            var visited = new HashSet<string>();
            var visiting = new HashSet<string>();
            void Visit(TechnologyPrototype technology)
            {
                if (visited.Contains(technology.ID)) return;
                Assert.That(visiting.Add(technology.ID), Is.True, "Cyclic prerequisite at " + technology.ID);
                foreach (var prerequisite in technology.TechnologyPrerequisites)
                {
                    Assert.That(technologies.ContainsKey(prerequisite.Id), Is.True, prerequisite.Id);
                    Visit(technologies[prerequisite.Id]);
                }
                visiting.Remove(technology.ID);
                visited.Add(technology.ID);
            }
            bool ReachesProduction(TechnologyPrototype technology)
                => technology.ID.StartsWith("Industrial", StringComparison.Ordinal) ||
                   technology.TechnologyPrerequisites.Any(p => ReachesProduction(technologies[p.Id]));
            foreach (var technology in visible) Visit(technology);
            foreach (var technology in visible)
                Assert.That(ReachesProduction(technology), Is.True, technology.ID + " must follow a production milestone.");
            foreach (var technology in visible.Where(t => t.ID.StartsWith("Industrial", StringComparison.Ordinal)))
                foreach (var prerequisite in technology.TechnologyPrerequisites)
                    Assert.That(technology.Position.X, Is.GreaterThan(technologies[prerequisite.Id].Position.X),
                        technology.ID + " must follow its prerequisite from left to right.");
            Assert.That(technologies["Powercells"].Hidden, Is.True);
            Assert.That(technologies["SalvageEquipment"].Hidden, Is.True);
        });
    }

    [Test]
    public async Task PasswordResetAndRevocationCloseSessionsAcrossEveryServerOfTheStation()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ResearchSystem>();
            var station = SEntMan.SpawnEntity(null, map.GridCoords);
            SEntMan.EnsureComponent<StationMemberComponent>(map.Grid.Owner).Station = station;
            var firstServer = SEntMan.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
            var secondServer = SEntMan.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
            var first = Terminal(map.GridCoords, firstServer);
            var second = Terminal(map.GridCoords, secondServer);
            var director = Actor(map.GridCoords, "ResearchDirector");
            var scientist = Actor(map.GridCoords, "Research");
            var head = research.IssueCrewAccount(station, director, "ResearchDirector", "Head");
            var employee = research.IssueCrewAccount(station, scientist, "Scientist", "Scientist");
            Assert.That(research.AuthenticateTerminal(first, director, head.Username, head.Password, false), Is.True);
            Assert.That(research.AuthenticateTerminal(second, scientist, employee.Username, employee.Password, false), Is.True);
            Run(research, second, scientist, "analyze electronics");
            var secondConsole = SEntMan.GetComponent<ResearchConsoleComponent>(second);
            Assert.That(secondConsole.Analysis, Is.Not.Null);
            var trace = "trace " + (char) ('A' + secondConsole.Analysis!.Node);
            Run(research, first, director, "role " + employee.Username + " observer");
            Assert.That(research.CanResearchAtTerminal(second), Is.False);
            Run(research, second, scientist, trace);
            Assert.That(secondConsole.Analysis, Is.Null, "A demoted observer cannot finish a pending scientific analysis.");
            Run(research, first, director, "role " + employee.Username + " researcher");
            Assert.That(research.CanResearchAtTerminal(second), Is.True);
            Run(research, first, director, "reset " + employee.Username);
            Assert.That(SEntMan.GetComponent<ResearchConsoleComponent>(second).Username, Is.Null);
            var registry = SEntMan.GetComponent<ResearchAccountsComponent>(station);
            var replacement = registry.Credentials[employee.Username].Password;
            Assert.That(replacement, Is.Not.EqualTo(employee.Password));
            SEntMan.GetComponent<ResearchConsoleComponent>(second).NextAuthentication = TimeSpan.Zero;
            Assert.That(research.AuthenticateTerminal(second, scientist, employee.Username, replacement, false), Is.True);
            Run(research, first, director, "revoke " + employee.Username);
            Assert.That(SEntMan.GetComponent<ResearchConsoleComponent>(second).Username, Is.Null);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task AutomaticCredentialsReachCommandPdasAndTheDirectorsPaperRegister()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var research = SEntMan.System<ResearchSystem>();
            var domain = SEntMan.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
            var director = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var captain = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var personnel = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var directorPda = SEntMan.SpawnEntity("RnDPDA", map.GridCoords);
            var captainPda = SEntMan.SpawnEntity("CaptainPDA", map.GridCoords);
            var personnelPda = SEntMan.SpawnEntity("HoPPDA", map.GridCoords);
            var inventory = SEntMan.System<InventorySystem>();
            Assert.That(inventory.TryEquip(director, directorPda, "id", force: true), Is.True);
            Assert.That(inventory.TryEquip(captain, captainPda, "id", force: true), Is.True);
            Assert.That(inventory.TryEquip(personnel, personnelPda, "id", force: true), Is.True);
            var third = research.ProvisionCrewMember(domain, personnel, "HeadOfPersonnel", "Personnel");
            var first = research.ProvisionCrewMember(domain, director, "ResearchDirector", "Director");
            var second = research.ProvisionCrewMember(domain, captain, "Captain", "Captain");
            var assistant = Actor(map.GridCoords);
            var observer = research.IssueCrewAccount(domain, assistant, "Passenger", "Assistant");
            research.Update(1.1f);
            Assert.That(research.GetResearchInbox(personnelPda).Messages.Any(m => m.Body.Contains(third.Password)), Is.True);
            Assert.That(research.GetResearchInbox(captainPda).Messages.Any(m => m.Body.Contains(second.Password)), Is.True);
            Assert.That(research.GetResearchInbox(directorPda).Messages.Any(m => m.Body.Contains(first.Password)), Is.True);
            var papers = SEntMan.EntityQueryEnumerator<PaperComponent>();
            var found = false;
            while (papers.MoveNext(out _, out var paper))
                found |= paper.Content.Contains(first.Username) && paper.Content.Contains(second.Username) && paper.Content.Contains(observer.Username);
            Assert.That(found, Is.True, "The director must receive the complete initial employee credential register.");
            var terminal = Terminal(map.GridCoords, domain);
            Assert.That(research.AuthenticateTerminal(terminal, assistant, observer.Username, observer.Password, false), Is.True);
            Assert.That(research.CanUseTerminal(terminal), Is.True);
            Assert.That(research.TryConsoleResearch(terminal, assistant, "IndustrialElectronics"), Is.False, "Ordinary employee accounts cannot purchase research.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

}
