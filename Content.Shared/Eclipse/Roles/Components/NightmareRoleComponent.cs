using Robust.Shared.GameStates;
using Content.Shared.Roles.Components;
using Content.Shared.Eclipse.Nightmare.Components;

namespace Content.Shared.Eclipse.Roles.Components;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedNightmareSystem))]
public sealed partial class NightmareRoleComponent : BaseMindRoleComponent
{
    [DataField]
    public bool PolymorphState = false;
}
