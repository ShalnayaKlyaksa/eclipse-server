using Robust.Shared.Serialization;
namespace Content.Shared.Research.Components;

[Serializable, NetSerializable]
public sealed class ResearchMailMessage
{
    public string Sender;
    public string Subject;
    public string Body;
    public Guid Id = Guid.NewGuid();
    public bool Read;
    public string SenderId = "";
    public string RecipientId = "";
    public ResearchMailMessage(string sender, string subject, string body)
    { Sender = sender; Subject = subject; Body = body; }
}

[Serializable, NetSerializable]
public sealed class ResearchMailUiState : BoundUserInterfaceState
{
    public string Recipient;
    public ResearchMailMessage[] Messages;
    public ResearchMailMessage[] Sent = [];
    public string? Feedback;
    public ResearchMailUiState(string recipient, ResearchMailMessage[] messages)
    { Recipient = recipient; Messages = messages; }
}
