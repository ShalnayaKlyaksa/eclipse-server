using System.Linq;
using Content.Server.CartridgeLoader;
using Content.Server.Research.Components;
using Content.Shared.GameTicking;
using Content.Shared.PDA;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;
using Robust.Shared.Prototypes;

namespace Content.Server.PDA;

/// <summary>Round-local personal accounts share the existing station employee IDs and mailbox identity.</summary>
[RegisterComponent]
public sealed partial class PdaPersonalServicesComponent : Component
{
    public readonly Dictionary<string, PdaPersonalAccount> Accounts = new(StringComparer.OrdinalIgnoreCase);
    public long NextTransaction;
    public long NextChatMessage;
    public readonly Dictionary<Guid, PdaGroupConversation> Groups = new();
}

public sealed class PdaPersonalAccount
{
    public int Credits;
    public int ReservedCredits;
    public readonly HashSet<string> Contacts = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> Unread = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<PdaChatLine> Messages = new();
    public readonly Dictionary<string, long> ReadChats = new(StringComparer.OrdinalIgnoreCase);
    public readonly Queue<Guid> ChatRequests = new();
    public readonly List<PdaBankEntry> Transactions = new();
    public readonly Queue<Guid> Transfers = new();
    public TimeSpan NextMessage;
    public int Salary;
    public TimeSpan NextSalary;
}

[RegisterComponent]
public sealed partial class PdaEmployeeCardComponent : Component
{
    public EntityUid Domain;
    public string Username = "";
}

public sealed partial class PdaPersonalServicesSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly CartridgeLoaderSystem _cartridges = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    private readonly Dictionary<EntityUid, string> _peers = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<PdaComponent, PdaServicesRequestMessage>(OnRequest);
        SubscribeLocalEvent<PdaComponent, PdaServicesActionMessage>(OnAction);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { _peers.Clear(); _selectedChats.Clear(); });
        InitializeMessenger();
    }

    private void OnRequest(EntityUid uid, PdaComponent pda, PdaServicesRequestMessage message)
    {
        if (!PdaUiKey.Key.Equals(message.UiKey) || message.Peer == null || message.Peer.Length > 32) return;
        if (TryIdentity(uid, out var mailbox, out var registry))
        {
            _peers[uid] = ResolveId(registry, message.Peer) ?? "";
            Account(mailbox.Domain, mailbox.Username).Unread.Remove(_peers[uid]);
        }
        SendState(uid);
    }

    private void OnAction(EntityUid uid, PdaComponent pda, PdaServicesActionMessage message)
    {
        if (!PdaUiKey.Key.Equals(message.UiKey)) return;
        if (!TryIdentity(uid, out var mailbox, out var registry)) { SendState(uid, "pda-services-no-account"); return; }
        if (message.Peer == null || message.Text == null || message.Peer.Length > 32 || message.Text.Length > 512)
        { SendState(uid, "pda-services-invalid"); return; }
        var peer = ResolveId(registry, message.Peer) ?? "";
        var text = message.Text.Trim();
        if (peer == mailbox.Username || !registry.Credentials.ContainsKey(peer))
        { SendState(uid, "pda-services-recipient-not-found"); return; }
        var account = Account(mailbox.Domain, mailbox.Username);
        var recipient = Account(mailbox.Domain, peer);
        string? feedback = null;
        _peers[uid] = peer;
        switch (message.Action)
        {
            case PdaServiceAction.AddContact:
                if (account.Contacts.Count >= 32 && !account.Contacts.Contains(peer)) feedback = "pda-services-contact-limit";
                else account.Contacts.Add(peer);
                break;
            case PdaServiceAction.RemoveContact:
                account.Contacts.Remove(peer);
                account.Messages.RemoveAll(line => line.From == peer || line.To == peer);
                account.Unread.Remove(peer);
                _peers[uid] = "";
                break;
            case PdaServiceAction.ToggleBlock:
                if (!account.Blocked.Remove(peer)) account.Blocked.Add(peer);
                break;
            case PdaServiceAction.SendChat:
                if (text.Length == 0 || (!account.Contacts.Contains(peer) && !account.Messages.Any(line => line.From == peer))) feedback = "pda-services-add-contact-first";
                else if (account.Blocked.Contains(peer) || recipient.Blocked.Contains(mailbox.Username)) feedback = "pda-services-message-unavailable";
                else if (_timing.CurTime < account.NextMessage) feedback = "pda-services-slow-down";
                else
                {
                    account.NextMessage = _timing.CurTime + TimeSpan.FromSeconds(1);
                    account.Unread.Remove(peer);
                    var line = new PdaChatLine(mailbox.Username, peer, text, (int) _timing.CurTime.TotalSeconds, ++Comp<PdaPersonalServicesComponent>(mailbox.Domain).NextChatMessage);
                    Append(account.Messages, line, 100);
                    Append(recipient.Messages, line, 100);
                    recipient.Unread.Add(mailbox.Username);
                    feedback = "pda-services-message-sent";
                    Notify(mailbox.Domain, peer, "pda-os-app-max", "pda-services-new-message");
                }
                break;
            case PdaServiceAction.Transfer:
                if (!TryComp<PdaEmployeeCardComponent>(pda.ContainedId, out var card) || card.Domain != mailbox.Domain || card.Username != mailbox.Username)
                    feedback = "pda-bank-no-card";
                else feedback = TryTransfer(mailbox.Domain, mailbox.Username, peer, message.Amount, message.RequestId, text)
                        ? "pda-services-transferred" : "pda-services-transfer-failed";
                break;
            default:
                feedback = "pda-services-invalid";
                break;
        }
        SendState(uid, feedback);
        Push(mailbox.Domain, peer);
    }

    private bool TryIdentity(EntityUid pda, out ResearchPdaMailboxComponent mailbox, out ResearchAccountsComponent registry)
    {
        mailbox = default!;
        registry = default!;
        if (TryComp<PdaComponent>(pda, out var device) && TryComp<PdaEmployeeCardComponent>(device.ContainedId, out var card) &&
            TryComp<ResearchAccountsComponent>(card.Domain, out var cardRegistry) && cardRegistry.Credentials.ContainsKey(card.Username))
        {
            mailbox = new ResearchPdaMailboxComponent { Domain = card.Domain, Username = card.Username };
            registry = cardRegistry;
            return true;
        }
        if (!TryComp<ResearchPdaMailboxComponent>(pda, out var foundMailbox) ||
            !TryComp<ResearchAccountsComponent>(foundMailbox.Domain, out var foundRegistry) ||
            !foundRegistry.Credentials.ContainsKey(foundMailbox.Username))
            return false;
        mailbox = foundMailbox;
        registry = foundRegistry;
        return true;
    }

    private PdaPersonalAccount Account(EntityUid domain, string id)
    {
        var service = EnsureComp<PdaPersonalServicesComponent>(domain);
        if (!service.Accounts.TryGetValue(id, out var account))
        {
            var economy = _prototypes.Index<PersonalEconomyPrototype>("StationPersonalEconomy");
            var job = Comp<ResearchAccountsComponent>(domain).Credentials[id].JobId;
            service.Accounts[id] = account = new()
            {
                Credits = economy.InitialCredits,
                Salary = economy.Salaries.GetValueOrDefault(job, economy.DefaultSalary),
                NextSalary = _timing.CurTime + economy.SalaryInterval,
            };
            Append(account.Transactions, new PdaBankEntry(++service.NextTransaction, "", economy.InitialCredits, Loc.GetString("pda-bank-initial"), (int) _timing.CurTime.TotalSeconds), 40);
        }
        return account;
    }

    public static string? ResolveId(ResearchAccountsComponent registry, string id) => registry.PublicIds.GetValueOrDefault(id.Trim().ToLowerInvariant());
    public void RegisterEmployee(EntityUid domain, string id) => Account(domain, id);

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<PdaPersonalServicesComponent>();
        var economy = _prototypes.Index<PersonalEconomyPrototype>("StationPersonalEconomy");
        if (economy.SalaryInterval <= TimeSpan.Zero) return;
        while (query.MoveNext(out var domain, out var service))
        {
            foreach (var (id, account) in service.Accounts)
            {
                if (_timing.CurTime < account.NextSalary) continue;
                var periods = 1 + (_timing.CurTime - account.NextSalary).Ticks / economy.SalaryInterval.Ticks;
                var amount = (int) Math.Min((long) account.Salary * periods, int.MaxValue - (long) account.Credits - account.ReservedCredits);
                account.NextSalary += TimeSpan.FromTicks(economy.SalaryInterval.Ticks * periods);
                if (amount > 0) AdjustCredits(domain, id, amount, Loc.GetString("pda-bank-salary"));
            }
        }
    }

    public int Balance(EntityUid domain, string id) => Account(domain, id).Credits;
    public bool HasAccount(EntityUid domain, string id) => TryComp<ResearchAccountsComponent>(domain, out var registry) && registry.Credentials.ContainsKey(id);

    /// <summary>Server/admin funding hook. No client message can issue credits.</summary>
    public bool AdjustCredits(EntityUid domain, string id, int amount, string note)
    {
        if (!TryComp<ResearchAccountsComponent>(domain, out var registry) || !registry.Credentials.ContainsKey(id)) return false;
        var account = Account(domain, id);
        var updated = (long) account.Credits + amount;
        if (updated < 0 || updated + account.ReservedCredits > int.MaxValue || amount == 0) return false;
        account.Credits = (int) updated;
        var service = Comp<PdaPersonalServicesComponent>(domain);
        Append(account.Transactions, new PdaBankEntry(++service.NextTransaction, "", amount, note[..Math.Min(80, note.Length)], (int) _timing.CurTime.TotalSeconds), 40);
        Push(domain, id);
        return true;
    }

    public bool TryTransfer(EntityUid domain, string sender, string recipient, int amount, Guid requestId, string note)
    {
        if (requestId == Guid.Empty || amount <= 0 || amount > 1000000 || sender.Equals(recipient, StringComparison.OrdinalIgnoreCase) || note.Length > 80 ||
            !TryComp<ResearchAccountsComponent>(domain, out var registry) || !registry.Credentials.ContainsKey(sender) || !registry.Credentials.ContainsKey(recipient)) return false;
        var from = Account(domain, sender);
        var to = Account(domain, recipient);
        if (from.Transfers.Contains(requestId)) return true;
        if (from.Credits < amount || (long) to.Credits + to.ReservedCredits + amount > int.MaxValue) return false;
        from.Credits -= amount;
        to.Credits += amount;
        from.Transfers.Enqueue(requestId);
        if (from.Transfers.Count > 64) from.Transfers.Dequeue();
        var id = ++Comp<PdaPersonalServicesComponent>(domain).NextTransaction;
        var seconds = (int) _timing.CurTime.TotalSeconds;
        Append(from.Transactions, new PdaBankEntry(id, recipient, -amount, note, seconds), 40);
        Append(to.Transactions, new PdaBankEntry(id, sender, amount, note, seconds), 40);
        Push(domain, sender);
        Push(domain, recipient);
        Notify(domain, recipient, "pda-os-app-bank", "pda-services-transfer-received");
        return true;
    }

    private void SendState(EntityUid pda, string? feedback = null)
    {
        var state = new PdaServicesStateMessage { Feedback = feedback };
        if (TryIdentity(pda, out var mailbox, out var registry))
        {
            var own = Account(mailbox.Domain, mailbox.Username);
            var peer = _peers.GetValueOrDefault(pda, "");
            state.Available = true;
            state.Id = registry.Credentials[mailbox.Username].PublicId;
            state.Name = registry.Credentials[mailbox.Username].Name;
            state.Balance = own.Credits;
            state.Peer = registry.Credentials.GetValueOrDefault(peer)?.PublicId ?? "";
            state.PeerName = registry.Credentials.GetValueOrDefault(peer)?.Name ?? "";
            state.Contacts = own.Contacts.Union(own.Messages.Select(line => line.From == mailbox.Username ? line.To : line.From)).Union(own.Unread).Union(own.Blocked)
                .Where(registry.Credentials.ContainsKey).OrderByDescending(own.Unread.Contains).ThenBy(id => registry.Credentials[id].Name).Take(64)
                .Select(id => new PdaContact(registry.Credentials[id].PublicId, registry.Credentials[id].Name, own.Unread.Contains(id), own.Blocked.Contains(id))).ToArray();
            state.Messages = own.Messages.Where(line => line.From == peer || line.To == peer).TakeLast(40)
                .Select(line => line with { From = registry.Credentials.GetValueOrDefault(line.From)?.PublicId ?? line.From, To = registry.Credentials.GetValueOrDefault(line.To)?.PublicId ?? line.To }).ToArray();
            state.Transactions = own.Transactions.AsEnumerable().Reverse().Select(entry => entry with { Counterparty = registry.Credentials.GetValueOrDefault(entry.Counterparty)?.PublicId ?? entry.Counterparty }).ToArray();
            state.Salary = own.Salary;
            state.SecondsToSalary = (int) Math.Max(0, (own.NextSalary - _timing.CurTime).TotalSeconds);
        }
        _ui.ServerSendUiMessage(pda, PdaUiKey.Key, state);
        SendMessengerState(pda);
    }

    private void Push(EntityUid domain, string id)
    {
        var query = EntityQueryEnumerator<PdaComponent>();
        while (query.MoveNext(out var pda, out _))
            if (TryIdentity(pda, out var mailbox, out _) && mailbox.Domain == domain && mailbox.Username.Equals(id, StringComparison.OrdinalIgnoreCase)) SendState(pda);
    }

    private void Notify(EntityUid domain, string id, string title, string text)
    {
        var query = EntityQueryEnumerator<PdaComponent>();
        while (query.MoveNext(out var pda, out _))
            if (TryIdentity(pda, out var mailbox, out _) && mailbox.Domain == domain && mailbox.Username.Equals(id, StringComparison.OrdinalIgnoreCase))
                _cartridges.SendNotification(pda, Loc.GetString(title), Loc.GetString(text));
    }

    private static void Append<T>(List<T> list, T entry, int limit)
    {
        list.Add(entry);
        if (list.Count > limit) list.RemoveAt(0);
    }
}
