using Robust.Shared.GameStates;

namespace Content.Shared._Eclipse.Power;

/// <summary>Rendering state only. It does not grant gravity, fuel, containment or electrical output.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MicroBlackHoleVisualComponent : Component
{
    [DataField, AutoNetworkedField] public float Activity = .6f;
    [DataField, AutoNetworkedField] public float Instability;
    [DataField, AutoNetworkedField] public float Radius = 2.5f;
    [DataField, AutoNetworkedField] public float FieldRadius;
}
