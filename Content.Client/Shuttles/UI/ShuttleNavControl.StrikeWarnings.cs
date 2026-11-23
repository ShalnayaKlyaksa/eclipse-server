using System.Numerics;
using Content.Shared._Eclipse.StationEvents;
using Robust.Client.Graphics;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleNavControl
{
    private void DrawIncomingStrike(DrawingHandleScreen handle, MapId map, EntityUid? ourGrid, Matrix3x2 worldToView)
    {
        var query = EntManager.EntityQueryEnumerator<IncomingGungnirComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var warning, out var xform))
        {
            if (xform.MapID != map || (uid != ourGrid && !_grids.Exists(grid => grid.Owner == uid)))
                continue;

            var worldBearing = _transform.GetWorldRotation(uid).RotateVec(warning.SourceBearing);
            var bearing = Vector2.TransformNormal(worldBearing, worldToView);
            if (bearing.LengthSquared() < 0.001f)
                continue;
            bearing = Vector2.Normalize(bearing);
            var side = new Vector2(-bearing.Y, bearing.X);
            var center = PixelSize / 2;
            var point = center + bearing * MathF.Min(PixelSize.X, PixelSize.Y) * 0.41f;
            var color = new Color(1f, 0.48f, 0.12f);
            // An edge chevron marks only the source sector. It never connects to an impact point.
            for (var i = 0; i < 3; i++)
            {
                var tip = point + bearing * i * 6;
                handle.DrawLine(tip + bearing * 7 + side * 6, tip, color.WithAlpha(1 - i * 0.22f));
                handle.DrawLine(tip + bearing * 7 - side * 6, tip, color.WithAlpha(1 - i * 0.22f));
            }
            var label = Loc.GetString("eclipse-gungnir-radar-source");
            var dimensions = handle.GetDimensions(Font, label, 0.8f);
            var textPosition = Vector2.Clamp(point - bearing * 24 - new Vector2(dimensions.X / 2, -dimensions.Y / 2),
                new Vector2(4, dimensions.Y), Vector2.Max(new Vector2(4, dimensions.Y), PixelSize - dimensions - new Vector2(4)));
            handle.DrawString(Font, textPosition, label, 0.8f, color);
        }
    }
}
