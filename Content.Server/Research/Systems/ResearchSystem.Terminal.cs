using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Content.Server.Power.EntitySystems;
using Content.Server.Research.Components;
using Content.Shared.Access;
using Content.Shared.Power;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    private static readonly ProtoId<AccessLevelPrototype> ScienceAccess = "Research";
    private static readonly ProtoId<AccessLevelPrototype> DirectorAccess = "ResearchDirector";
    private static readonly ProtoId<AccessLevelPrototype> CaptainAccess = "Captain";
    private int _nextTerminalAddress = 1;

    private void InitializeTerminal()
    {
        InitializeAccountProvisioning();
        SubscribeLocalEvent<ResearchConsoleComponent, PowerChangedEvent>(OnTerminalPowerChanged);
        SubscribeLocalEvent<ResearchConsoleComponent, ResearchTerminalAuthMessage>(OnTerminalAuth);
        SubscribeLocalEvent<ResearchConsoleComponent, ResearchTerminalCommandMessage>(OnTerminalCommand);
    }

    private void OnTerminalPowerChanged(EntityUid uid, ResearchConsoleComponent console, ref PowerChangedEvent args)
    {
        if (!args.Powered) EndTerminalSession(uid, console);
    }

    public bool CanUseTerminal(EntityUid uid)
    {
        if (!TryComp<ResearchConsoleComponent>(uid, out var console) || !this.IsPowered(uid, EntityManager) ||
            !TryGetClientServer(uid, out var server, out _)) return false;
        if (console.AccountServer != server || console.Username == null ||
            !TryComp<ResearchAccountsComponent>(AccountDomain(server.Value), out var accounts) || !accounts.Accounts.ContainsKey(console.Username))
        {
            console.Username = null;
            console.AccountServer = null;
            console.Analysis = null;
            console.PendingResearch.Clear();
            return false;
        }
        return true;
    }

    public bool CanResearchAtTerminal(EntityUid uid)
        => CanUseTerminal(uid) && TerminalAccounts(Comp<ResearchConsoleComponent>(uid).AccountServer!.Value)
            .Accounts[Comp<ResearchConsoleComponent>(uid).Username!].CanResearch;

    private ResearchTerminalState GetTerminalState(EntityUid uid, ResearchConsoleComponent console)
    {
        if (console.Address == 0) console.Address = _nextTerminalAddress++;
        var authenticated = CanUseTerminal(uid);
        var administrator = authenticated && TryComp<ResearchAccountsComponent>(AccountDomain(console.AccountServer!.Value), out var accounts) &&
                            accounts.Accounts[console.Username!].Administrator;
        TryGetClientServer(uid, out _, out var server);
        return new ResearchTerminalState
        {
            Username = authenticated ? console.Username : null,
            Administrator = administrator,
            CanResearch = authenticated && TerminalAccounts(console.AccountServer!.Value).Accounts[console.Username!].CanResearch,
            ServerName = server?.ServerName ?? Loc.GetString("research-terminal-no-network"),
            Address = console.Address,
            Lines = console.TerminalLines.ToArray(),
            QueuedTechnologies = console.PendingResearch.ToArray(),
        };
    }

    private void TerminalPrint(ResearchConsoleComponent console, string message)
    {
        console.TerminalLines.Add(message);
        if (console.TerminalLines.Count > 80) console.TerminalLines.RemoveRange(0, console.TerminalLines.Count - 80);
    }

    private void OnTerminalAuth(EntityUid uid, ResearchConsoleComponent console, ResearchTerminalAuthMessage args)
    {
        AuthenticateTerminal(uid, args.Actor, args.Username, args.Password, args.Register);
        UpdateConsoleInterface(uid, console);
    }

    /// <summary>Game-world credentials, shared by the station's research servers for this round.</summary>
    public bool AuthenticateTerminal(EntityUid uid, EntityUid actor, string username, string password, bool register)
    {
        if (!TryComp<ResearchConsoleComponent>(uid, out var console) || !this.IsPowered(uid, EntityManager) ||
            !TryGetClientServer(uid, out var server, out _)) return false;
        if (_timing.CurTime < console.NextAuthentication) return false;
        console.NextAuthentication = _timing.CurTime + TimeSpan.FromSeconds(2);
        username = (username ?? "").Trim().ToLowerInvariant();
        if (username.Length is < 3 or > 24 || username.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-' and not '.') ||
            password == null || password.Length is < 4 or > 32 || password.Any(c => c < '!' || c > '~'))
        {
            TerminalPrint(console, Loc.GetString("research-terminal-credentials-format"));
            return false;
        }
        var accounts = TerminalAccounts(server.Value);
        if (register)
        {
            TerminalPrint(console, Loc.GetString("research-terminal-accounts-issued"));
            return false;
        }
        if (!accounts.Accounts.TryGetValue(username, out var account) ||
            !CryptographicOperations.FixedTimeEquals(PasswordHash(password, account.Salt), account.Hash))
        {
            TerminalPrint(console, Loc.GetString("research-terminal-login-failed"));
            return false;
        }
        console.Username = username;
        console.AccountServer = server;
        console.Analysis = null;
        TerminalPrint(console, Loc.GetString("research-terminal-welcome", ("user", username)));
        return true;
    }

    private static byte[] PasswordHash(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 10000, HashAlgorithmName.SHA256, 32);

    private void OnTerminalCommand(EntityUid uid, ResearchConsoleComponent console, ResearchTerminalCommandMessage args)
    {
        ExecuteTerminalCommand(uid, args.Actor, args.Command);
        UpdateConsoleInterface(uid, console);
    }

    public void ExecuteTerminalCommand(EntityUid uid, EntityUid actor, string command)
    {
        if (!TryComp<ResearchConsoleComponent>(uid, out var console) || !CanUseTerminal(uid) ||
            command == null || command.Length > 512 || _timing.CurTime < console.NextCommand) return;
        console.NextCommand = _timing.CurTime + TimeSpan.FromSeconds(.2);
        // A fixed in-game command vocabulary; never passed to a host shell or script engine.
        foreach (var statement in command.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8))
        {
            if (!CanUseTerminal(uid)) break;
            var split = statement.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (split.Length == 0) continue;
            var verb = split[0].ToLowerInvariant();
            var argument = split.Length > 1 ? split[1].Trim('"') : "";
            // Passwords belong in the masked login form, never in shared console scrollback.
            if (verb is "login" or "password" or "passwd")
            {
                TerminalPrint(console, Loc.GetString("research-terminal-use-login-form"));
                continue;
            }
            TerminalPrint(console, $"{console.Username}@lab-{console.Address}> {statement}");
            if (verb == "exit")
            {
                EndTerminalSession(uid, console);
                break;
            }
            if (ExecuteExtendedTerminalCommand(uid, console, actor, verb, argument)) continue;
            switch (verb)
            {
                case "clear": console.TerminalLines.Clear(); break;
                case "whoami":
                case "status": PrintTerminalStatus(uid, console); break;
                case "techs": PrintTechnologies(uid, console, argument); break;
                case "plan": PrintResearchPlan(uid, console, argument); break;
                case "research":
                    if (FindTerminalTechnology(argument) is { } technology)
                        TerminalPrint(console, Loc.GetString(TryConsoleResearch(uid, actor, technology.ID) ? "research-terminal-researched" : "research-terminal-unavailable", ("tech", Loc.GetString(technology.Name))));
                    else TerminalPrint(console, Loc.GetString("research-terminal-tech-not-found"));
                    break;
                case "batch":
                    foreach (var request in argument.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(8))
                    {
                        var batchTechnology = FindTerminalTechnology(request);
                        if (batchTechnology == null || !TryConsoleResearch(uid, actor, batchTechnology.ID))
                        {
                            TerminalPrint(console, Loc.GetString("research-terminal-batch-stopped", ("tech", request)));
                            break;
                        }
                        TerminalPrint(console, Loc.GetString("research-terminal-researched", ("tech", Loc.GetString(batchTechnology.Name))));
                    }
                    break;
                case "terminals":
                case "sessions": PrintTerminals(uid, console); break;
                case "remote": RemoteTerminal(uid, console, argument); break;
                case "revoke": RevokeAccount(uid, console, argument); break;
                case "analyze": StartTerminalAnalysis(uid, console, argument); break;
                case "trace":
                case "tune":
                case "verify": AdvanceTerminalAnalysis(uid, console, verb, argument); break;
                default: TerminalPrint(console, Loc.GetString("research-terminal-unknown-command")); break;
            }
        }
    }

    private void EndTerminalSession(EntityUid uid, ResearchConsoleComponent console)
    {
        console.Username = null;
        console.AccountServer = null;
        console.Analysis = null;
        console.TerminalLines.Clear();
        console.PendingResearch.Clear();
        console.QueueActor = null;
        _uiSystem.CloseUi(uid, ResearchConsoleUiKey.Key);
    }

    private void PrintTerminalStatus(EntityUid uid, ResearchConsoleComponent console)
    {
        TryGetClientServer(uid, out _, out var server);
        var account = TerminalAccounts(console.AccountServer!.Value).Accounts[console.Username!];
        TerminalPrint(console, Loc.GetString("research-terminal-status", ("user", console.Username!),
            ("role", Loc.GetString(account.Administrator ? "research-terminal-role-director" : account.CanResearch ? "research-terminal-role-scientist" : "research-terminal-role-observer")),
            ("address", console.Address), ("server", server!.ServerName), ("points", server.Points)));
    }

    private TechnologyPrototype? FindTerminalTechnology(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return PrototypeManager.EnumeratePrototypes<TechnologyPrototype>().FirstOrDefault(p => !p.Hidden &&
            (p.ID.Equals(text, StringComparison.OrdinalIgnoreCase) || TechnologyAlias(p).Equals(text, StringComparison.OrdinalIgnoreCase) ||
             Loc.GetString(p.Name).Equals(text, StringComparison.OrdinalIgnoreCase)));
    }

    private static string TechnologyAlias(TechnologyPrototype prototype)
        => prototype.ID.StartsWith("Industrial", StringComparison.Ordinal) ? prototype.ID[10..].ToLowerInvariant() : prototype.ID;

    private void PrintTechnologies(EntityUid uid, ResearchConsoleComponent console, string filter)
    {
        SyncClientWithServer(uid);
        var database = Comp<TechnologyDatabaseComponent>(uid);
        foreach (var technology in PrototypeManager.EnumeratePrototypes<TechnologyPrototype>()
                     .Where(p => !p.Hidden && (string.IsNullOrEmpty(filter) || p.ID.Contains(filter, StringComparison.OrdinalIgnoreCase) || Loc.GetString(p.Name).Contains(filter, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(p => p.Cost).Take(16))
            TerminalPrint(console, $"{TechnologyAlias(technology)} / {Loc.GetString(technology.Name)} / {technology.Cost} / " +
                Loc.GetString(database.UnlockedTechnologies.Contains(technology.ID) ? "research-terminal-done" : IsTechnologyAvailable(database, technology) ? "research-terminal-ready" : "research-terminal-locked"));
    }

    private void PrintResearchPlan(EntityUid uid, ResearchConsoleComponent console, string argument)
    {
        var goal = FindTerminalTechnology(argument);
        if (goal == null) { TerminalPrint(console, Loc.GetString("research-terminal-tech-not-found")); return; }
        var route = BuildResearchPlan(uid, goal);
        foreach (var technology in route) TerminalPrint(console, $"-> {TechnologyAlias(technology)} / {Loc.GetString(technology.Name)} / {technology.Cost}");
        TerminalPrint(console, Loc.GetString("research-terminal-plan-total", ("count", route.Count), ("points", route.Sum(p => p.Cost))));
    }

    private List<TechnologyPrototype> BuildResearchPlan(EntityUid uid, TechnologyPrototype goal)
    {
        SyncClientWithServer(uid);
        var database = Comp<TechnologyDatabaseComponent>(uid);
        var visited = new HashSet<string>();
        var route = new List<TechnologyPrototype>();
        void Visit(TechnologyPrototype technology)
        {
            if (!visited.Add(technology.ID) || database.UnlockedTechnologies.Contains(technology.ID)) return;
            foreach (var prerequisite in technology.TechnologyPrerequisites) Visit(PrototypeManager.Index(prerequisite));
            route.Add(technology);
        }
        Visit(goal);
        return route;
    }

    private List<(EntityUid Uid, ResearchConsoleComponent Console)> NetworkTerminals(EntityUid uid)
    {
        var result = new List<(EntityUid, ResearchConsoleComponent)>();
        if (!TryGetClientServer(uid, out var server, out _)) return result;
        var query = EntityQueryEnumerator<ResearchConsoleComponent, ResearchClientComponent>();
        while (query.MoveNext(out var terminal, out var console, out var client))
        {
            if (client.Server != server) continue;
            if (console.Address == 0) console.Address = _nextTerminalAddress++;
            result.Add((terminal, console));
        }
        return result;
    }

    private void PrintTerminals(EntityUid uid, ResearchConsoleComponent console)
    {
        foreach (var (terminal, session) in NetworkTerminals(uid).OrderBy(t => t.Console.Address))
            TerminalPrint(console, $"lab-{session.Address} / {Name(terminal)} / " +
                (CanUseTerminal(terminal) ? session.Username : Loc.GetString("research-terminal-locked")));
    }

    private void RemoteTerminal(EntityUid uid, ResearchConsoleComponent console, string argument)
    {
        var parts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0].Replace("lab-", "", StringComparison.OrdinalIgnoreCase), out var address))
        { TerminalPrint(console, Loc.GetString("research-terminal-remote-usage")); return; }
        foreach (var (terminal, target) in NetworkTerminals(uid))
        {
            if (target.Address != address) continue;
            if (parts[1] == "status") { TerminalPrint(console, $"lab-{address}: {Name(terminal)} / {target.Username ?? "—"}"); return; }
            var administrator = TerminalAccounts(console.AccountServer!.Value).Accounts[console.Username!].Administrator;
            if (parts[1] == "exit" && (administrator || target.Username == console.Username))
            {
                EndTerminalSession(terminal, target);
                UpdateConsoleInterface(terminal, target);
                TerminalPrint(console, Loc.GetString("research-terminal-remote-closed", ("address", address)));
                return;
            }
            TerminalPrint(console, Loc.GetString("research-terminal-operation-denied"));
            return;
        }
        TerminalPrint(console, Loc.GetString("research-terminal-remote-not-found"));
    }

    private void RefreshAccountSessions(EntityUid server, string username, bool close)
    {
        var domain = AccountDomain(server);
        var sessions = new List<(EntityUid Uid, ResearchConsoleComponent Console)>();
        var query = EntityQueryEnumerator<ResearchConsoleComponent>();
        while (query.MoveNext(out var terminal, out var session))
            if (session.AccountServer is { } connected && !Deleted(connected) && AccountDomain(connected) == domain &&
                string.Equals(session.Username, username, StringComparison.OrdinalIgnoreCase)) sessions.Add((terminal, session));
        foreach (var (terminal, session) in sessions)
        {
            if (close) EndTerminalSession(terminal, session);
            UpdateConsoleInterface(terminal, session);
        }
    }

    private void RevokeAccount(EntityUid uid, ResearchConsoleComponent console, string username)
    {
        var accounts = TerminalAccounts(console.AccountServer!.Value);
        if (!accounts.Accounts[console.Username!].Administrator || !accounts.Accounts.Remove(username))
        { TerminalPrint(console, Loc.GetString("research-terminal-operation-denied")); return; }
        RefreshAccountSessions(console.AccountServer!.Value, username, close: true);
        TerminalPrint(console, Loc.GetString("research-terminal-account-revoked", ("user", username)));
    }
}
