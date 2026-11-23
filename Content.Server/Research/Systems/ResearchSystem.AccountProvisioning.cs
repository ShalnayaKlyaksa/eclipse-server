using System.Linq;
using System.Security.Cryptography;
using Content.Server.CartridgeLoader;
using Content.Server.Preferences.Managers;
using Content.Server.Research.Components;
using Content.Server.Station.Systems;
using Content.Shared.CartridgeLoader;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Paper;
using Content.Shared.PDA;
using Content.Shared.Research.Components;
using Content.Shared.Roles;
using Robust.Shared.Containers;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    [Dependency] private readonly StationSystem _accountStations = default!;
    [Dependency] private readonly InventorySystem _accountInventory = default!;
    [Dependency] private readonly SharedHandsSystem _accountHands = default!;
    [Dependency] private readonly PaperSystem _accountPaper = default!;
    [Dependency] private readonly CartridgeLoaderSystem _accountCartridges = default!;
    [Dependency] private readonly SharedContainerSystem _accountContainers = default!;
    [Dependency] private readonly IServerPreferencesManager _accountPreferences = default!;
    private readonly List<(EntityUid Employee, EntityUid Domain, string Username, string Job, int Attempts)> _accountDelivery = new();
    private readonly Dictionary<EntityUid, (EntityUid Domain, EntityUid Paper)> _credentialReports = new();
    private float _accountDeliveryElapsed;
    [Dependency] private readonly Content.Server.Eclipse.Economy.EclipseCurrencyManager _personalIdentity = default!;

    private void InitializeAccountProvisioning()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnAccountCrewSpawn);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _accountDelivery.Clear();
            _credentialReports.Clear();
            _nextTerminalAddress = 1;
        });
    }
    private EntityUid AccountDomain(EntityUid server) => _accountStations.GetOwningStation(server) ?? server;
    private ResearchAccountsComponent TerminalAccounts(EntityUid server) => EnsureComp<ResearchAccountsComponent>(AccountDomain(server));

    private void OnAccountCrewSpawn(PlayerSpawnCompleteEvent args)
    {
        if (string.IsNullOrEmpty(args.JobId)) return;
        var registry = EnsureComp<ResearchAccountsComponent>(args.Station);
        var user = args.Player.UserId;
        int? slot = null;
        if (_accountPreferences.GetPreferencesOrNull(user) is { } preferences)
        {
            foreach (var (index, character) in preferences.Characters)
            {
                if (!ReferenceEquals(character, args.Profile))
                    continue;
                slot = index;
                break;
            }
        }

        var publicId = slot is { } indexOfCharacter ? _personalIdentity.GetPdaPublicId(user, indexOfCharacter) : string.Empty;
        var accountKey = (user, slot, publicId);
        ResearchIssuedCredential credential;
        if (registry.PlayerAccounts.TryGetValue(accountKey, out var previous) && registry.Credentials.TryGetValue(previous, out var issued))
        {
            credential = issued;
            var owner = EnsureComp<ResearchCrewAccountComponent>(args.Mob);
            owner.Domain = args.Station; owner.Username = issued.Username;
            _accountDelivery.Add((args.Mob, args.Station, issued.Username, args.JobId, 0));
        }
        else
        {
            credential = ProvisionCrewMember(args.Station, args.Mob, args.JobId, args.Profile.Name, publicId);
            registry.PlayerAccounts[accountKey] = credential.Username;
        }
        EntityManager.System<Content.Server.PDA.PdaPersonalServicesSystem>().RegisterEmployee(args.Station, credential.Username);
        EntityManager.System<Content.Server.PDA.StationCommerceSystem>().ProvisionMerchant(args.Station, credential.Username, args.Mob, args.JobId);
        if (args.LateJoin)
        {
            var accounts = Comp<ResearchAccountsComponent>(args.Station);
            foreach (var director in accounts.DirectorAccounts)
                if (accounts.Accounts.TryGetValue(director, out var account) && account.Administrator)
                    SendResearchMail(args.Station, director, Loc.GetString("research-mail-new-employee-subject"),
                        $"{credential.Name} / {credential.Job}\n{credential.Username} : {credential.Password}");
        }
    }

    public ResearchIssuedCredential ProvisionCrewMember(EntityUid domain, EntityUid employee, string job, string? fullName = null, string? publicId = null)
    {
        var credential = IssueCrewAccount(domain, employee, job, fullName, publicId);
        if (!_accountDelivery.Any(d => d.Employee == employee && d.Domain == domain))
            _accountDelivery.Add((employee, domain, credential.Username, job, 0));
        return credential;
    }

    /// <summary>Provision one station employee, independently of which scientific servers are currently online.</summary>
    public ResearchIssuedCredential IssueCrewAccount(EntityUid domain, EntityUid employee, string job, string? fullName = null, string? publicId = null)
    {
        if (TryComp<ResearchCrewAccountComponent>(employee, out var existing) && existing.Domain == domain &&
            TryComp<ResearchAccountsComponent>(domain, out var oldRegistry) && oldRegistry.Credentials.TryGetValue(existing.Username, out var old)) return old;
        var registry = EnsureComp<ResearchAccountsComponent>(domain);
        var username = $"crew{++registry.NextEmployeeNumber:000}";
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        var password = new string(Enumerable.Range(0, 10).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        var access = _accessReader.FindAccessTags(employee);
        var administrator = job is "ResearchDirector" or "HeadOfPersonnel" or "Captain" || access.Contains(DirectorAccess) || access.Contains(CaptainAccess);
        var scientist = administrator || access.Contains(ScienceAccess);
        var salt = RandomNumberGenerator.GetBytes(16);
        var jobName = PrototypeManager.TryIndex<JobPrototype>(job, out var role) ? Loc.GetString(role.Name) : job;
        if (!PdaPublicId.Valid(publicId) || registry.PublicIds.ContainsKey(publicId!))
        {
            publicId = username;
            while (registry.PublicIds.ContainsKey(publicId) || _personalIdentity.IsPdaPublicIdReserved(publicId))
                publicId += "-";
        }
        var credential = new ResearchIssuedCredential(username, password, fullName ?? Name(employee), jobName, administrator, scientist, publicId!, job);
        registry.Accounts.Add(username, new ResearchAccount(salt, PasswordHash(password, salt), administrator, scientist));
        registry.Credentials.Add(username, credential);
        registry.PublicIds.Add(publicId!, username);
        if (job == "ResearchDirector") registry.DirectorAccounts.Add(username);
        registry.Mailboxes.Add(username, new());
        var owner = EnsureComp<ResearchCrewAccountComponent>(employee);
        owner.Domain = domain;
        owner.Username = username;
        return credential;
    }

    private void UpdateAccountDelivery(float frameTime)
    {
        _accountDeliveryElapsed += frameTime;
        if (_accountDeliveryElapsed < 1) return;
        _accountDeliveryElapsed = 0;
        for (var i = _accountDelivery.Count - 1; i >= 0; i--)
        {
            var delivery = _accountDelivery[i];
            if (Deleted(delivery.Employee) || Deleted(delivery.Domain)) { _accountDelivery.RemoveAt(i); continue; }
            if (delivery.Job == "ResearchDirector" && !_credentialReports.ContainsKey(delivery.Employee))
            {
                var paper = Spawn("ResearchCredentialsReport", Transform(delivery.Employee).Coordinates);
                _credentialReports[delivery.Employee] = (delivery.Domain, paper);
                UpdateCredentialReport(delivery.Domain, paper);
                _accountHands.TryPickupAnyHand(delivery.Employee, paper);
            }
            if (!_accountInventory.TryGetSlotEntity(delivery.Employee, "id", out var pda) || !HasComp<PdaComponent>(pda))
            {
                if (delivery.Attempts >= 30) _accountDelivery.RemoveAt(i);
                else _accountDelivery[i] = (delivery.Employee, delivery.Domain, delivery.Username, delivery.Job, delivery.Attempts + 1);
                continue;
            }
            AssignMailbox(pda.Value, delivery.Domain, delivery.Username);
            if (delivery.Job is "ResearchDirector" or "HeadOfPersonnel" or "Captain")
            {
                var credential = Comp<ResearchAccountsComponent>(delivery.Domain).Credentials[delivery.Username];
                SendResearchMail(delivery.Domain, delivery.Username, Loc.GetString("research-mail-credentials-subject"),
                    Loc.GetString("research-mail-credentials-body", ("name", credential.Name), ("login", credential.Username), ("password", credential.Password)));
            }
            _accountDelivery.RemoveAt(i);
        }
    }

    public void AssignMailbox(EntityUid pda, EntityUid domain, string username)
    {
        var mailbox = EnsureComp<ResearchPdaMailboxComponent>(pda);
        mailbox.Domain = domain;
        mailbox.Username = username;
        if (TryComp<PdaComponent>(pda, out var device) && device.ContainedId is { } id)
        {
            var card = EnsureComp<Content.Server.PDA.PdaEmployeeCardComponent>(id);
            card.Domain = domain; card.Username = username;
        }
        if (_accountContainers.TryGetContainer(pda, SharedCartridgeLoaderSystem.InstalledContainerId, out var programs) &&
            !programs.ContainedEntities.Any(p => HasComp<ResearchMailCartridgeComponent>(p)))
            _accountCartridges.InstallProgram(pda, "ResearchMailCartridge", deinstallable: false);
    }

    private void UpdateCredentialReport(EntityUid domain, EntityUid paper)
    {
        if (Deleted(paper) || !TryComp<ResearchAccountsComponent>(domain, out var accounts)) return;
        var text = Loc.GetString("research-credentials-report-header") + "\n\n" + string.Join("\n\n", accounts.Credentials.Values.Select(c =>
            $"{c.Name} / {c.Job}\n{c.Username} : {c.Password}"));
        _accountPaper.SetContent(paper, text);
    }

    public bool SendResearchMail(EntityUid domain, string recipient, string subject, string body, string? sender = null, string senderId = "", string recipientId = "")
    {
        if (!TryComp<ResearchAccountsComponent>(domain, out var accounts) || !accounts.Mailboxes.TryGetValue(recipient, out var inbox)) return false;
        inbox.Add(new ResearchMailMessage(sender ?? Loc.GetString("research-mail-accounts-service"), subject, body) { SenderId = senderId, RecipientId = recipientId });
        if (inbox.Count > 32) inbox.RemoveAt(0);
        var mailboxes = EntityQueryEnumerator<ResearchPdaMailboxComponent>();
        while (mailboxes.MoveNext(out var pda, out var mailbox))
            if (mailbox.Domain == domain && mailbox.Username.Equals(recipient, StringComparison.OrdinalIgnoreCase))
            {
                _accountCartridges.SendNotification(pda, Loc.GetString("research-mail-program"), Loc.GetString("research-mail-new-message"));
                if (TryComp<CartridgeLoaderComponent>(pda, out var loader) && HasComp<ResearchMailCartridgeComponent>(loader.ActiveProgram))
                    _accountCartridges.UpdateCartridgeUiState(pda, GetResearchInbox(pda));
            }
        return true;
    }

    public ResearchMailUiState GetResearchInbox(EntityUid pda)
    {
        if (TryComp<ResearchPdaMailboxComponent>(pda, out var mailbox) && TryComp<ResearchAccountsComponent>(mailbox.Domain, out var accounts) &&
            accounts.Mailboxes.TryGetValue(mailbox.Username, out var inbox)) return new ResearchMailUiState(accounts.Credentials.GetValueOrDefault(mailbox.Username)?.PublicId ?? mailbox.Username, inbox.ToArray())
            { Sent = accounts.SentMail.GetValueOrDefault(mailbox.Username)?.ToArray() ?? [] };
        return new ResearchMailUiState("", []);
    }
}
