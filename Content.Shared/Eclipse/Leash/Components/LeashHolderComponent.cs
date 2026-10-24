namespace Content.Shared.Eclipse.Leash.Components;

[RegisterComponent]
public sealed partial class LeashHolderComponent : Component
{
    public readonly HashSet<EntityUid> Leashes = new();
}
