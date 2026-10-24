namespace Content.Shared.CharacterInfo;

[RegisterComponent]
public sealed partial class PersonalTasksComponent : Component
{
    [DataField]
    public string? JobId;

    [DataField]
    public int Version;

    [DataField]
    public List<PersonalTaskInfo> Tasks = new();
}
