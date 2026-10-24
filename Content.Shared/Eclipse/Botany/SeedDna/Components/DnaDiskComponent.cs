using Robust.Shared.GameStates;

namespace Content.Shared.Eclipse.Botany.SeedDna.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class DnaDiskComponent : Component
{
    public SeedDataDto? SeedData;
}
