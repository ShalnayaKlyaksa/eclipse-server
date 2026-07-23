using Robust.Shared.GameStates;

namespace Content.Shared.GhostTypes;

/// <summary>
/// Marks an entity whose ghost sprite variant can be selected.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GhostSpriteStateComponent : Component;
