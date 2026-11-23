using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared.Research.Components;

[Serializable, NetSerializable]
public enum ResearchMailAction : byte { Send, Read, Delete }

[Serializable, NetSerializable]
public sealed class ResearchMailActionMessage : CartridgeMessageEvent
{
    public ResearchMailAction Action;
    public string Recipient = "";
    public string Subject = "";
    public string Body = "";
    public Guid Id;
    public bool SentFolder;
}
