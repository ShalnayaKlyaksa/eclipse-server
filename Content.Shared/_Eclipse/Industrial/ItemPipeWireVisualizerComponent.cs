using Robust.Shared.GameStates;

namespace Content.Shared._Eclipse.Industrial;

[RegisterComponent, NetworkedComponent]
public sealed partial class ItemPipeWireVisualizerComponent : Component
{
    [DataField]
    public string StatePrefix = "lp";
}
