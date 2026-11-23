using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>Port hit areas in grid axes, matching the faces used by industrial pipe connections.</summary>
public static class IndustrialPortClickZones
{
    public const float EdgeDepth = 0.25f;
    public const float EdgeSpan = 0.22f;
    public const float TileHalfExtent = 0.5f;
    public const float DefaultHalfExtent = 0.6f;

    private static readonly (Direction Direction, Box2 LocalBounds)[] Zones =
    [
        (Direction.North, new Box2(-EdgeSpan, TileHalfExtent - EdgeDepth, EdgeSpan, TileHalfExtent)),
        (Direction.East, new Box2(TileHalfExtent - EdgeDepth, -EdgeSpan, TileHalfExtent, EdgeSpan)),
        (Direction.South, new Box2(-EdgeSpan, -TileHalfExtent, EdgeSpan, -TileHalfExtent + EdgeDepth)),
        (Direction.West, new Box2(-TileHalfExtent, -EdgeSpan, -TileHalfExtent + EdgeDepth, EdgeSpan)),
    ];

    public static Vector2 GetHalfExtents(EntityUid uid, IEntityManager ent)
        => new(DefaultHalfExtent, DefaultHalfExtent);

    public static Vector2 GetFaceZoneCenter(Direction direction)
        => direction.ToVec() * (TileHalfExtent - EdgeDepth / 2);

    public static IReadOnlyList<(Direction Direction, Box2 LocalBounds)> GetFaceZones(EntityUid uid, IEntityManager ent)
        => Zones;

    /// <summary>
    /// Pipe faces follow the grid rather than the sprite's rotation. Drawing and clicking use the same transform.
    /// </summary>
    public static Matrix3x2 GetPortWorldMatrix(TransformComponent xform, SharedTransformSystem transform)
    {
        var rotation = xform.GridUid is { } grid ? transform.GetWorldRotation(grid) : Angle.Zero;
        var matrix = Matrix3Helpers.CreateRotation(rotation);
        matrix.Translation = transform.GetWorldPosition(xform);
        return matrix;
    }

    public static bool TryGetClickedFace(EntityUid processor, EntityCoordinates clickLocation,
        IEntityManager ent, SharedTransformSystem transform, out Direction direction)
    {
        direction = default;
        if (!ent.TryGetComponent<TransformComponent>(processor, out var xform))
            return false;
        var clickMap = transform.ToMapCoordinates(clickLocation);
        if (clickMap.MapId != xform.MapID || !Matrix3x2.Invert(GetPortWorldMatrix(xform, transform), out var inverse))
            return false;
        return TryGetLocalFace(Vector2.Transform(clickMap.Position, inverse), out direction);
    }

    public static bool TryGetLocalFace(Vector2 local, out Direction direction)
    {
        foreach (var (face, bounds) in Zones)
        {
            if (!bounds.Contains(local))
                continue;
            direction = face;
            return true;
        }
        // The centre and corners are not ports: clicking them must not change an unrelated face.
        direction = default;
        return false;
    }
}
