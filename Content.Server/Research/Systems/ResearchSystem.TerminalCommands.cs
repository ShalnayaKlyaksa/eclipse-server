using System.Linq;
using System.Security.Cryptography;
using Content.Server.Research.Components;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    private bool ExecuteExtendedTerminalCommand(EntityUid uid, ResearchConsoleComponent console, EntityUid actor, string verb, string argument)
    {
        switch (verb)
        {
            case "help": PrintTerminalHelp(console, argument); return true;
            case "find":
            case "search": PrintTechnologies(uid, console, argument); return true;
            case "info": PrintTechnologyInformation(uid, console, argument); return true;
            case "tree": PrintResearchPlan(uid, console, argument); return true;
            case "servers":
                foreach (var (server, component) in GetServers(uid).OrderBy(x => x.Comp.Id))
                    TerminalPrint(console, $"rd-{component.Id} / {component.ServerName} / {component.Points} " + Loc.GetString("research-terminal-points-unit"));
                return true;
            case "connect":
                if (int.TryParse(argument.Replace("rd-", "", StringComparison.OrdinalIgnoreCase), out var id) &&
                    TryGetServerById(uid, id, out var serverUid, out _))
                {
                    OnClientSelected(uid, Comp<ResearchClientComponent>(uid), new ResearchClientServerSelectedMessage(id));
                    TerminalPrint(console, Loc.GetString("research-terminal-connected", ("id", id)));
                }
                else TerminalPrint(console, Loc.GetString("research-terminal-server-not-found"));
                return true;
            case "users": PrintAccountDirectory(console, argument); return true;
            case "role": ChangeAccountRole(uid, console, argument); return true;
            case "reset": ResetAccountPassword(uid, console, argument); return true;
            case "mail": HandleTerminalMail(console, argument); return true;
            case "queue":
            case "jobs": HandleResearchQueue(uid, console, actor, argument); return true;
            case "cancel": console.PendingResearch.Clear(); TerminalPrint(console, Loc.GetString("research-terminal-queue-cancelled")); return true;
            case "history":
                foreach (var line in console.TerminalLines.Where(l => l.Contains("@lab-", StringComparison.Ordinal)).TakeLast(12).ToArray()) TerminalPrint(console, line);
                return true;
            case "logout": EndTerminalSession(uid, console); return true;
            default: return false;
        }
    }

    private void PrintTerminalHelp(ResearchConsoleComponent console, string topic)
    {
        var key = topic.ToLowerInvariant() switch
        {
            "research" or "science" or "plan" or "batch" or "queue" => "research-terminal-help-science",
            "analysis" or "analyze" or "trace" or "tune" or "verify" => "research-terminal-help-analysis",
            "network" or "servers" or "connect" or "remote" or "terminals" => "research-terminal-help-network",
            "accounts" or "users" or "reset" or "role" or "revoke" or "mail" => "research-terminal-help-accounts",
            _ => "research-terminal-help-overview",
        };
        TerminalPrint(console, Loc.GetString(key));
    }

    private void PrintTechnologyInformation(EntityUid uid, ResearchConsoleComponent console, string argument)
    {
        var technology = FindTerminalTechnology(argument);
        if (technology == null) { TerminalPrint(console, Loc.GetString("research-terminal-tech-not-found")); return; }
        TerminalPrint(console, Loc.GetString("research-terminal-info-title", ("name", Loc.GetString(technology.Name)), ("alias", TechnologyAlias(technology)), ("points", technology.Cost)));
        foreach (var prerequisite in technology.TechnologyPrerequisites)
            TerminalPrint(console, "  -> " + Loc.GetString(PrototypeManager.Index(prerequisite).Name));
        foreach (var unlock in technology.GenericUnlocks) TerminalPrint(console, Loc.GetString(unlock.UnlockDescription));
        PrintResearchPlan(uid, console, technology.ID);
    }

    private void PrintAccountDirectory(ResearchConsoleComponent console, string filter)
    {
        var accounts = TerminalAccounts(console.AccountServer!.Value);
        foreach (var credential in accounts.Credentials.Values.Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || c.Username.Contains(filter, StringComparison.OrdinalIgnoreCase)).Take(32))
            TerminalPrint(console, $"{credential.Username} / {credential.Name} / {credential.Job}" +
                (accounts.Accounts.ContainsKey(credential.Username) ? "" : " / " + Loc.GetString("research-terminal-account-disabled")));
    }

    private void ChangeAccountRole(EntityUid uid, ResearchConsoleComponent console, string argument)
    {
        var parts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var accounts = TerminalAccounts(console.AccountServer!.Value);
        if (!accounts.Accounts[console.Username!].Administrator || parts.Length != 2 ||
            !accounts.Accounts.TryGetValue(parts[0], out var account) || parts[1] is not ("observer" or "researcher" or "admin"))
        { TerminalPrint(console, Loc.GetString("research-terminal-role-usage")); return; }
        accounts.Accounts[parts[0]] = account with { Administrator = parts[1] == "admin", CanResearch = parts[1] != "observer" };
        if (accounts.Credentials.TryGetValue(parts[0], out var credential))
            accounts.Credentials[parts[0]] = credential with { Administrator = parts[1] == "admin", CanResearch = parts[1] != "observer" };
        RefreshAccountSessions(console.AccountServer!.Value, parts[0], close: false);
        TerminalPrint(console, Loc.GetString("research-terminal-role-updated", ("user", parts[0]), ("role", parts[1])));
    }

    private void ResetAccountPassword(EntityUid uid, ResearchConsoleComponent console, string username)
    {
        var server = console.AccountServer!.Value;
        var accounts = TerminalAccounts(server);
        var requester = console.Username!;
        if (!accounts.Accounts[requester].Administrator || !accounts.Credentials.TryGetValue(username, out var credential))
        { TerminalPrint(console, Loc.GetString("research-terminal-operation-denied")); return; }
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        var password = new string(Enumerable.Range(0, 10).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        var salt = RandomNumberGenerator.GetBytes(16);
        var previous = accounts.Accounts.GetValueOrDefault(username);
        accounts.Accounts[username] = new ResearchAccount(salt, PasswordHash(password, salt), previous?.Administrator ?? credential.Administrator, previous?.CanResearch ?? credential.CanResearch);
        accounts.Credentials[username] = credential with { Password = password };
        SendResearchMail(AccountDomain(server), requester, Loc.GetString("research-mail-reset-subject"),
            Loc.GetString("research-mail-reset-body", ("name", credential.Name), ("login", credential.Username), ("password", password)));
        RefreshAccountSessions(server, username, close: true);
        TerminalPrint(console, Loc.GetString("research-terminal-password-reset", ("user", username)));
    }

    private void HandleTerminalMail(ResearchConsoleComponent console, string argument)
    {
        var domain = AccountDomain(console.AccountServer!.Value);
        var accounts = Comp<ResearchAccountsComponent>(domain);
        var inbox = accounts.Mailboxes[console.Username!];
        var parts = argument.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts[0] == "inbox")
        {
            if (inbox.Count == 0) TerminalPrint(console, Loc.GetString("research-mail-empty"));
            for (var i = 0; i < inbox.Count; i++) TerminalPrint(console, $"{i + 1}. {inbox[i].Sender} / {inbox[i].Subject}");
        }
        else if (parts.Length >= 2 && parts[0] == "read" && int.TryParse(parts[1], out var number) && number > 0 && number <= inbox.Count)
        {
            var message = inbox[number - 1];
            TerminalPrint(console, $"{message.Subject}\n{message.Sender}\n\n{message.Body}");
        }
        else if (parts.Length == 3 && parts[0] == "send" && SendResearchMail(domain, parts[1], Loc.GetString("research-mail-player-subject"), parts[2], console.Username))
            TerminalPrint(console, Loc.GetString("research-terminal-mail-sent", ("user", parts[1])));
        else TerminalPrint(console, Loc.GetString("research-terminal-mail-usage"));
    }

    private void HandleResearchQueue(EntityUid uid, ResearchConsoleComponent console, EntityUid actor, string argument)
    {
        if (!CanResearchAtTerminal(uid)) { TerminalPrint(console, Loc.GetString("research-terminal-operation-denied")); return; }
        var parts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts[0] == "list")
        {
            if (console.PendingResearch.Count == 0) TerminalPrint(console, Loc.GetString("research-terminal-queue-empty"));
            foreach (var pending in console.PendingResearch) TerminalPrint(console, "-> " + Loc.GetString(PrototypeManager.Index<TechnologyPrototype>(pending).Name));
            return;
        }
        if (parts[0] == "cancel") { console.PendingResearch.Clear(); TerminalPrint(console, Loc.GetString("research-terminal-queue-cancelled")); return; }
        var goal = parts.Length == 2 ? FindTerminalTechnology(parts[1]) : null;
        if (goal == null || parts[0] is not ("add" or "plan")) { TerminalPrint(console, Loc.GetString("research-terminal-queue-usage")); return; }
        var route = parts[0] == "plan" ? BuildResearchPlan(uid, goal) : new List<TechnologyPrototype> { goal };
        foreach (var technology in route)
            if (console.PendingResearch.Count < 32 && !console.PendingResearch.Contains(technology.ID)) console.PendingResearch.Add(technology.ID);
        console.QueueActor = actor;
        TerminalPrint(console, Loc.GetString("research-terminal-queue-added", ("count", console.PendingResearch.Count)));
        TryAdvanceResearchQueue(uid, console);
    }

    private void TryAdvanceResearchQueue(EntityUid uid, ResearchConsoleComponent console)
    {
        if (console.QueueProcessing || console.PendingResearch.Count == 0 || !CanResearchAtTerminal(uid)) return;
        console.QueueProcessing = true;
        try
        {
            SyncClientWithServer(uid);
            for (var step = 0; step < 32 && console.PendingResearch.Count > 0; step++)
            {
                var next = console.PendingResearch[0];
                if (!PrototypeManager.TryIndex<TechnologyPrototype>(next, out var technology) || HasTechnology(uid, next)) { console.PendingResearch.RemoveAt(0); continue; }
                if (!CanServerUnlockTechnology(uid, technology)) break;
                console.PendingResearch.RemoveAt(0);
                var actor = console.QueueActor is { } owner && !Deleted(owner) ? owner : uid;
                if (!TryConsoleResearch(uid, actor, next)) { console.PendingResearch.Insert(0, next); break; }
                TerminalPrint(console, Loc.GetString("research-terminal-researched", ("tech", Loc.GetString(technology.Name))));
            }
        }
        finally { console.QueueProcessing = false; }
    }
}
