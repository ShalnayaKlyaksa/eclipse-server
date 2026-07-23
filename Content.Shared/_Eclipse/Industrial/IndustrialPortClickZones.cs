using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>
/// Rectangular click zones on each face of an industrial processor for port wrench configuration.
/// Zones are confined to the anchored tile, not the physics fixture.
/// </summary>
public static class IndustrialPortClickZones
{
    /// <summary>Depth of each side strip measured inward from the tile edge (world units).</summary>
    public const float EdgeDepth = 0.16f;

    /// <summary>Half-length of each side strip along the edge (world units).</summary>
    public const float EdgeSpan = 0.28f;

    /// <summary>Half of a single grid tile in world units.</summary>
    public const float TileHalfExtent = 0.5f;

    /// <summary>Default half-extent for industrial machine fixtures (matches BaseIndustrialProcessor).</summary>
    public const float DefaultHalfExtent = 0.6f;

    public static Vector2 GetHalfExtents(EntityUid uid, IEntityManager ent)
    {
        _ = uid;
        _ = ent;
        return new Vector2(DefaultHalfExtent, DefaultHalfExtent);
    }

    public static Vector2 GetFaceZoneCenter(Direction direction)
    {
        var inset = EdgeDepth * 0.5f;

        return direction switch
        {
            Direction.North => new Vector2(0, TileHalfExtent - inset),
            Direction.South => new Vector2(0, -TileHalfExtent + inset),
            Direction.East => new Vector2(TileHalfExtent - inset, 0),
            Direction.West => new Vector2(-TileHalfExtent + inset, 0),
            _ => Vector2.Zero,
        };
    }

    public static bool TryGetClickedFace(
        EntityUid processor,
        EntityCoordinates clickLocation,
        IEntityManager ent,
        SharedTransformSystem transform,
        out Direction direction)
    {
        direction = default;

        if (!ent.TryGetComponent<TransformComponent>(processor, out var xform))
            return false;

        var clickMap = transform.ToMapCoordinates(clickLocation);
        var local = Vector2.Transform(clickMap.Position, transform.GetInvWorldMatrix(processor));
        var halfExtents = new Vector2(TileHalfExtent, TileHalfExtent);

        Direction? best = null;
        var bestScore = 0f;

        if (IsInNorthZone(local, halfExtents))
        {
            var score = local.Y - (halfExtents.Y - EdgeDepth);
            if (score > bestScore)
            {
                bestScore = score;
                best = Direction.North;
            }
        }

        if (IsInSouthZone(local, halfExtents))
        {
            var score = (-halfExtents.Y + EdgeDepth) - local.Y;
            if (score > bestScore)
            {
                bestScore = score;
                best = Direction.South;
            }
        }

        if (IsInEastZone(local, halfExtents))
        {
            var score = local.X - (halfExtents.X - EdgeDepth);
            if (score > bestScore)
            {
                bestScore = score;
                best = Direction.East;
            }
        }

        if (IsInWestZone(local, halfExtents))
        {
            var score = (-halfExtents.X + EdgeDepth) - local.X;
            if (score > bestScore)
            {
                bestScore = score;
                best = Direction.West;
            }
        }

        if (best == null)
        {
            if (TryGetNearestFace(local, out var nearest))
            {
                direction = nearest;
                return true;
            }

            return false;
        }

        direction = best.Value;
        return true;
    }

    private static bool TryGetNearestFace(Vector2 local, out Direction direction)
    {
        direction = default;

        if (MathF.Abs(local.X) > TileHalfExtent || MathF.Abs(local.Y) > TileHalfExtent)
            return false;

        var north = TileHalfExtent - local.Y;
        var south = TileHalfExtent + local.Y;
        var east = TileHalfExtent - local.X;
        var west = TileHalfExtent + local.X;

        var min = MathF.Min(MathF.Min(north, south), MathF.Min(east, west));

        if (MathHelper.CloseTo(min, north))
            direction = Direction.North;
        else if (MathHelper.CloseTo(min, south))
            direction = Direction.South;
        else if (MathHelper.CloseTo(min, east))
            direction = Direction.East;
        else
            direction = Direction.West;

        return true;
    }

    public static IEnumerable<(Direction Direction, Box2 LocalBounds)> GetFaceZones(EntityUid uid, IEntityManager ent)
    {
        _ = uid;
        _ = ent;

        var half = TileHalfExtent;

        yield return (Direction.North, new Box2(-EdgeSpan, half - EdgeDepth, EdgeSpan, half));
        yield return (Direction.South, new Box2(-EdgeSpan, -half, EdgeSpan, -half + EdgeDepth));
        yield return (Direction.East, new Box2(half - EdgeDepth, -EdgeSpan, half, EdgeSpan));
        yield return (Direction.West, new Box2(-half, -EdgeSpan, -half + EdgeDepth, EdgeSpan));
    }

    private static bool IsInNorthZone(Vector2 local, Vector2 halfExtents)
    {
        return local.Y >= halfExtents.Y - EdgeDepth
               && local.Y <= halfExtents.Y
               && MathF.Abs(local.X) <= EdgeSpan;
    }

    private static bool IsInSouthZone(Vector2 local, Vector2 halfExtents)
    {
        return local.Y <= -halfExtents.Y + EdgeDepth
               && local.Y >= -halfExtents.Y
               && MathF.Abs(local.X) <= EdgeSpan;
    }

    private static bool IsInEastZone(Vector2 local, Vector2 halfExtents)
    {
        return local.X >= halfExtents.X - EdgeDepth
               && local.X <= halfExtents.X
               && MathF.Abs(local.Y) <= EdgeSpan;
    }

    private static bool IsInWestZone(Vector2 local, Vector2 halfExtents)
    {
        return local.X <= -halfExtents.X + EdgeDepth
               && local.X >= -halfExtents.X
               && MathF.Abs(local.Y) <= EdgeSpan;
    }
}
