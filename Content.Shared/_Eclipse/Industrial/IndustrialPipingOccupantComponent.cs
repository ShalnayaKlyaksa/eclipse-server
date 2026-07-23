using Robust.Shared.GameObjects;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>
/// Marks a tile as occupied by industrial item or liquid piping.
/// Other anchored structures cannot share the tile.
/// </summary>
[RegisterComponent]
public sealed partial class IndustrialPipingOccupantComponent : Component;
