using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Maths;

namespace Content.Client.MainMenu.UI;

public sealed class EclipseStyleBoxRounded : StyleBox
{
    public Color BackgroundColor { get; set; }
    public Color BorderColor { get; set; }
    public Thickness BorderThickness { get; set; }
    public float Radius { get; set; } = 8f;

    protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
    {
        var thickness = BorderThickness.Scale(uiScale);
        var (left, top, right, bottom) = thickness;
        var radius = Radius * uiScale;

        if (left > 0 && MathHelper.CloseTo(left, top) && MathHelper.CloseTo(left, right) && MathHelper.CloseTo(left, bottom))
        {
            DrawRoundedRect(handle, box, radius, BorderColor);
            DrawRoundedRect(handle, thickness.Deflate(box), MathF.Max(0f, radius - left), BackgroundColor);
            return;
        }

        DrawRoundedRect(handle, box, radius, BackgroundColor);

        if (left > 0)
            handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + left, box.Bottom), BorderColor);

        if (top > 0)
            handle.DrawRect(new UIBox2(box.Left, box.Top, box.Right, box.Top + top), BorderColor);

        if (right > 0)
            handle.DrawRect(new UIBox2(box.Right - right, box.Top, box.Right, box.Bottom), BorderColor);

        if (bottom > 0)
            handle.DrawRect(new UIBox2(box.Left, box.Bottom - bottom, box.Right, box.Bottom), BorderColor);
    }

    private static void DrawRoundedRect(DrawingHandleScreen handle, UIBox2 box, float radius, Color color)
    {
        radius = MathF.Min(radius, MathF.Min(box.Width, box.Height) / 2f);

        if (radius <= 0f)
        {
            handle.DrawRect(box, color);
            return;
        }

        const int segments = 6;
        var vertices = new Vector2[segments * 4 + 6];
        var count = 0;
        vertices[count++] = box.Center;

        AddArc(vertices, ref count, new Vector2(box.Right - radius, box.Top + radius), radius, -MathF.PI / 2f, 0f, segments);
        AddArc(vertices, ref count, new Vector2(box.Right - radius, box.Bottom - radius), radius, 0f, MathF.PI / 2f, segments);
        AddArc(vertices, ref count, new Vector2(box.Left + radius, box.Bottom - radius), radius, MathF.PI / 2f, MathF.PI, segments);
        AddArc(vertices, ref count, new Vector2(box.Left + radius, box.Top + radius), radius, MathF.PI, MathF.PI * 3f / 2f, segments);

        vertices[count] = vertices[1];
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, vertices, color);
    }

    private static void AddArc(Vector2[] vertices, ref int count, Vector2 center, float radius, float start, float end, int segments)
    {
        for (var i = 0; i <= segments; i++)
        {
            var t = (float) i / segments;
            var angle = MathHelper.Lerp(start, end, t);
            vertices[count++] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }
    }

    protected override float GetDefaultContentMargin(Margin margin)
    {
        return margin switch
        {
            Margin.Top => BorderThickness.Top,
            Margin.Bottom => BorderThickness.Bottom,
            Margin.Right => BorderThickness.Right,
            Margin.Left => BorderThickness.Left,
            _ => throw new ArgumentOutOfRangeException(nameof(margin), margin, null),
        };
    }
}
