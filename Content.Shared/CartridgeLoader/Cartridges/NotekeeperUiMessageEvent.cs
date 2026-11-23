using Robust.Shared.Serialization;

namespace Content.Shared.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class NotekeeperUiMessageEvent : CartridgeMessageEvent
{
    public readonly NotekeeperUiAction Action;
    public readonly string Note;
    public readonly string Title;
    public readonly Guid Id;
    public readonly int Revision;
    public readonly Guid Request;

    public NotekeeperUiMessageEvent(NotekeeperUiAction action, string note, string title = "", Guid id = default, int revision = 0, Guid request = default)
    {
        Action = action;
        Note = note;
        Title = title; Id = id; Revision = revision; Request = request;
    }
}

[Serializable, NetSerializable]
public enum NotekeeperUiAction
{
    Add,
    Remove,
    Edit
}
