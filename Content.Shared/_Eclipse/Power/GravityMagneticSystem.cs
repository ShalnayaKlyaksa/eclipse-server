using Content.Shared.Maps;
using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._Eclipse.Power;

[RegisterComponent, NetworkedComponent]
public sealed partial class GravityMagneticComponent : Component;

public sealed class GravityMagneticSystem : EntitySystem
{
    public const float HoldingAcceleration = 10f;

    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly ITileDefinitionManager _tiles = default!;

    public bool IsAdhering(EntityUid uid) =>
        HasComp<GravityMagneticComponent>(uid) && IsMetal(Transform(uid).Coordinates);

    public bool IsMetal(EntityCoordinates coordinates)
    {
        if (_turf.GetTileRef(coordinates) is not { } tile || _tiles[tile.Tile.TypeId] is not ContentTileDefinition definition)
            return false;

        var id = definition.ID;
        return id == "Plating" || id.StartsWith("FloorSteel", StringComparison.Ordinal) ||
            id.StartsWith("FloorDarkSlats", StringComparison.Ordinal);
    }
}
