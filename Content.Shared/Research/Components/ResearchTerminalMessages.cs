using Robust.Shared.Serialization;

namespace Content.Shared.Research.Components;

[Serializable, NetSerializable]
public sealed class ResearchTerminalAuthMessage : BoundUserInterfaceMessage
{
    public string Username;
    public string Password;
    public bool Register;
    public ResearchTerminalAuthMessage(string username, string password, bool register = false)
    { Username = username; Password = password; Register = register; }
}

[Serializable, NetSerializable]
public sealed class ResearchTerminalCommandMessage : BoundUserInterfaceMessage
{
    public string Command;
    public ResearchTerminalCommandMessage(string command) { Command = command; }
}

[Serializable, NetSerializable]
public sealed class ResearchTerminalAdminStateMessage(ResearchConsoleBoundInterfaceState state) : BoundUserInterfaceMessage
{
    public readonly ResearchConsoleBoundInterfaceState State = state;
}

[Serializable, NetSerializable]
public sealed class ResearchTerminalState
{
    public string? Username;
    public bool Administrator;
    public bool CanResearch;
    public string ServerName = "";
    public int Address;
    public string[] Lines = [];
    public string[] QueuedTechnologies = [];
}
