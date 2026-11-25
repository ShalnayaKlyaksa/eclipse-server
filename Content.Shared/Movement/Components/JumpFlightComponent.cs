using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

/// <summary>Marks a jump in progress and lists the low obstacles the jumper can clear before landing.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class JumpFlightComponent : Component
{
    [AutoNetworkedField]
    public List<EntityUid> VaultedObstacles = new();
}
