using Robust.Shared.Serialization;

namespace Content.Shared.PDA;

[Serializable, NetSerializable]
public enum PdaMessengerAction : byte { OpenDirect, CreateGroup, Send, RenameGroup, AddMembers, RemoveMember, LeaveGroup, Edit, Delete, ToggleBlock }

[Serializable, NetSerializable]
public sealed class PdaMessengerRequestMessage(string? chat = null, bool markRead = true) : BoundUserInterfaceMessage
{
    public string? Chat = chat;
    public bool MarkRead = markRead;
}

[Serializable, NetSerializable]
public sealed class PdaMessengerActionMessage(PdaMessengerAction action, string chat = "", string text = "", string[]? members = null, long message = 0, Guid request = default) : BoundUserInterfaceMessage
{
    public PdaMessengerAction Action = action;
    public string Chat = chat;
    public string Text = text;
    public string[] Members = members ?? [];
    public long Message = message;
    public Guid Request = request;
}

[Serializable, NetSerializable]
public sealed record PdaConversation(string Key, string Title, string Preview, int Unread, bool Group, int Members, int Seconds);
[Serializable, NetSerializable]
public sealed record PdaConversationMember(string Id, string Name, bool Owner);
[Serializable, NetSerializable]
public sealed record PdaConversationMessage(long Id, string From, string Name, string Text, int Seconds, long ReplyTo, bool Edited, bool Own, bool Read);

[Serializable, NetSerializable]
public sealed class PdaMessengerStateMessage : BoundUserInterfaceMessage
{
    public bool Available;
    public string OwnId = "";
    public string Selected = "";
    public string Title = "";
    public bool Group;
    public bool Owner;
    public bool Blocked;
    public PdaConversation[] Chats = [];
    public PdaConversationMember[] Members = [];
    public PdaConversationMessage[] Messages = [];
    public string? Feedback;
    public Guid Request;
}
