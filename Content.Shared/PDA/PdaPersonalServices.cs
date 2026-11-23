using Robust.Shared.Serialization;

namespace Content.Shared.PDA;

[Serializable, NetSerializable]
public enum PdaServiceAction : byte { AddContact, RemoveContact, ToggleBlock, SendChat, Transfer }

[Serializable, NetSerializable]
public sealed class PdaServicesRequestMessage(string peer = "") : BoundUserInterfaceMessage
{
    public string Peer = peer;
}

[Serializable, NetSerializable]
public sealed class PdaServicesActionMessage(PdaServiceAction action, string peer, string text = "", int amount = 0, Guid requestId = default) : BoundUserInterfaceMessage
{
    public PdaServiceAction Action = action;
    public string Peer = peer;
    public string Text = text;
    public int Amount = amount;
    public Guid RequestId = requestId;
}

[Serializable, NetSerializable]
public sealed record PdaContact(string Id, string Name, bool Unread, bool Blocked);

[Serializable, NetSerializable]
public sealed record PdaChatLine(string From, string To, string Text, int Seconds, long Id = 0, long ReplyTo = 0, bool Edited = false);

[Serializable, NetSerializable]
public sealed record PdaBankEntry(long Id, string Counterparty, int Amount, string Note, int Seconds);

[Serializable, NetSerializable]
public sealed class PdaServicesStateMessage : BoundUserInterfaceMessage
{
    public bool Available;
    public string Id = "";
    public string Name = "";
    public int Balance;
    public int Salary;
    public int SecondsToSalary;
    public string Peer = "";
    public string PeerName = "";
    public PdaContact[] Contacts = [];
    public PdaChatLine[] Messages = [];
    public PdaBankEntry[] Transactions = [];
    public string? Feedback;
}
