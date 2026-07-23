using Robust.Shared.GameStates;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>
/// Item pipes that auto-connect on all sides like power cables.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ItemPipeWireConnectComponent : Component;
