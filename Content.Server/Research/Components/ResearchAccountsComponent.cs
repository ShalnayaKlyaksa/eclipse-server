namespace Content.Server.Research.Components;

/// <summary>Round-local accounts and mail for a station; an independent server owns its own registry. Never replicated.</summary>
[RegisterComponent]
public sealed partial class ResearchAccountsComponent : Component
{
    public readonly Dictionary<string, ResearchAccount> Accounts = new(StringComparer.OrdinalIgnoreCase);
    public int NextEmployeeNumber;
    public readonly HashSet<string> DirectorAccounts = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, ResearchIssuedCredential> Credentials = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> PublicIds = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<(Robust.Shared.Network.NetUserId User, int? Slot, string PublicId), string> PlayerAccounts = new();
    public readonly Dictionary<string, List<Content.Shared.Research.Components.ResearchMailMessage>> Mailboxes = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, List<Content.Shared.Research.Components.ResearchMailMessage>> SentMail = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, TimeSpan> NextMail = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> AnalyzedTechnologies = new();
}

public sealed record ResearchAccount(byte[] Salt, byte[] Hash, bool Administrator, bool CanResearch = true);
public sealed record ResearchIssuedCredential(string Username, string Password, string Name, string Job, bool Administrator = false, bool CanResearch = false, string PublicId = "", string JobId = "");

[RegisterComponent]
public sealed partial class ResearchCrewAccountComponent : Component
{
    public EntityUid Domain;
    public string Username = "";
}

[RegisterComponent]
public sealed partial class ResearchPdaMailboxComponent : Component
{
    public EntityUid Domain;
    public string Username = "";
}

[RegisterComponent]
public sealed partial class ResearchMailCartridgeComponent : Component;


public sealed class ResearchAnalysis
{
    public string Technology = "";
    public int Stage;
    public int Node;
    public int Offset;
    public int Checksum;
}
