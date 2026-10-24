namespace Content.Server.Eclipse.Inferior.Components;

[RegisterComponent, Access(typeof(InferiorSystem))]
public sealed partial class InferiorImplantComponent : Component
{
    [DataField]
    public EntityUid? ImplanterUid = null;
    [DataField]
    public EntityUid? Overlord = null;
}
