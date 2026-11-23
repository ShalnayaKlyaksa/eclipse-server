using Robust.Shared.Serialization;

namespace Content.Shared.CartridgeLoader.Cartridges;

[DataDefinition, Serializable, NetSerializable]
public sealed partial class NotekeeperEntry
{
    [DataField] public Guid Id = Guid.NewGuid();
    [DataField] public string Title = "";
    [DataField] public string Body = "";
    [DataField] public int Revision;
}

[Serializable, NetSerializable]
public sealed class NotekeeperUiState : BoundUserInterfaceState
{
    public List<string> Notes;
    public NotekeeperEntry[] Entries = [];
    public Guid Request;
    public Guid Selected;
    public string? Feedback;

    public NotekeeperUiState(List<string> notes)
    {
        Notes = notes;
    }
}
