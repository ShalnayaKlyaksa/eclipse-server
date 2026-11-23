using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using System.Numerics;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>Reserve both diagnostic panels before measuring the expandable body map.</summary>
public sealed class HealthChipColumns : Control
{
    public float LeftWidth;
    public float RightWidth;
    public float Gap;
    public float DividerWidth;

    private (float Left, float Right, float Centre) Widths(float width)
    {
        var left = Math.Min(LeftWidth, width * 0.4f);
        var right = Math.Min(RightWidth, width * 0.36f);
        return (left, right, Math.Max(1f, width - left - right - Gap * 4f - DividerWidth * 2f));
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var widths = Widths(availableSize.X);
        foreach (var child in Children)
        {
            var width = child.Name switch
            {
                "MedicalChipVitals" => widths.Left,
                "MedicalChipDetails" => widths.Right,
                "MedicalChipBody" => widths.Centre,
                _ => DividerWidth,
            };
            child.Measure(new Vector2(width, availableSize.Y));
        }
        return Vector2.Zero;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var widths = Widths(finalSize.X);
        foreach (var child in Children)
        {
            var (x, width) = child.Name switch
            {
                "MedicalChipVitals" => (0f, widths.Left),
                "MedicalChipDetails" => (finalSize.X - widths.Right, widths.Right),
                "MedicalChipBody" => (widths.Left + Gap * 2f + DividerWidth, widths.Centre),
                "MedicalChipRightDivider" => (finalSize.X - widths.Right - Gap - DividerWidth, DividerWidth),
                _ => (widths.Left + Gap, DividerWidth),
            };
            child.Arrange(new UIBox2(new Vector2(x, 0), new Vector2(x + width, finalSize.Y)));
        }
        return finalSize;
    }
}

/// <summary>The illustration uses spare space; its requested square never sets the column's minimum width.</summary>
public sealed class HealthChipBodyArea : Control
{
    public float MaximumSide = 1050f;

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var box = PixelSizeBox;
        if (box.Width < 80 || box.Height < 80)
            return;

        // Static registration guides keep the anatomical map distinct from the game behind it.
        var grid = HealthMenuStyle.AmberDim.WithAlpha(0.025f);
        for (var i = 1; i < 4; i++)
        {
            var x = box.Left + box.Width * i / 4f;
            var y = box.Top + box.Height * i / 4f;
            handle.DrawLine(new Vector2(x, box.Top), new Vector2(x, box.Bottom), grid);
            handle.DrawLine(new Vector2(box.Left, y), new Vector2(box.Right, y), grid);
        }
        var color = HealthMenuStyle.AmberDim.WithAlpha(0.4f);
        var length = 18f * UIScale;
        for (var i = 0; i < 4; i++)
        {
            var direction = new Vector2((i & 1) == 0 ? 1 : -1, (i & 2) == 0 ? 1 : -1);
            var corner = new Vector2(direction.X > 0 ? box.Left : box.Right, direction.Y > 0 ? box.Top : box.Bottom);
            handle.DrawLine(corner, corner + new Vector2(length * direction.X, 0), color);
            handle.DrawLine(corner, corner + new Vector2(0, length * direction.Y), color);
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize) => Vector2.Zero;

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var side = Math.Max(1f, Math.Min(MaximumSide, Math.Min(finalSize.X, finalSize.Y)));
        foreach (var child in Children)
        {
            child.SetSize = new Vector2(side);
            child.MinSize = Vector2.Zero;
            child.Measure(new Vector2(side));
            child.Arrange(new UIBox2((finalSize - new Vector2(side)) * 0.5f, new Vector2(side) + (finalSize - new Vector2(side)) * 0.5f));
        }
        return finalSize;
    }
}

/// <summary>Oxygen saturation is a fill; the breathing pulse is independent of saturation.</summary>
public sealed class HealthChipLungGauge : Control
{
    private readonly Texture _texture = HealthMenuStyle.Tex("lungs");
    public float Oxygenation = 1f;
    public float RespiratoryRate = 14f;
    public bool Animate = true;
    private float _phase;

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _phase += args.DeltaSeconds * Math.Max(0f, RespiratoryRate) / 60f * MathF.Tau;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        handle.DrawTextureRect(_texture, box, Color.White.WithAlpha(0.15f));
        var fill = Math.Clamp(Oxygenation, 0f, 1f);
        if (fill <= 0f)
            return;
        var source = new UIBox2(0, _texture.Height * (1f - fill), _texture.Width, _texture.Height);
        var destination = new UIBox2(box.Left, box.Bottom - box.Height * fill, box.Right, box.Bottom);
        var color = fill < 0.55f ? HealthMenuStyle.Red : Color.White;
        var alpha = Animate && RespiratoryRate > 0f ? 0.85f + 0.15f * MathF.Sin(_phase) : 1f;
        handle.DrawTextureRectRegion(_texture, destination, source, color.WithAlpha(alpha));
    }
}

/// <summary>Bounded chip warning at the edge, leaving all text and treatment controls readable.</summary>
public sealed class HealthChipAlarmControl : Control
{
    public float Severity;
    public bool Enabled = true;
    private float _time;

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        if (!Enabled || Severity < 0.3f)
            return;
        var box = PixelSizeBox;
        var alpha = Math.Clamp(Severity, 0f, 1f) * (0.2f + 0.12f * MathF.Sin(_time * 3f));
        var color = HealthMenuStyle.Red.WithAlpha(alpha);
        for (var i = 0; i < 3; i++)
            handle.DrawRect(new UIBox2(box.Left + i * 3, box.Top + i * 3,
                box.Right - i * 3, box.Bottom - i * 3), color, filled: false);
    }
}


/// <summary>One aligned caption/value, meter and optional short clinical status.</summary>
public sealed class HealthChipIndicator : BoxContainer
{
    public HealthMenuProgressBar Gauge { get; }
    public Label Value { get; }
    private readonly Label _state;

    public HealthChipIndicator(string caption, float scale, bool stackedHeading = false)
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        SeparationOverride = Math.Max(2, (int) (3 * scale));
        var title = HealthMenuStyle.Text(caption, Math.Max(9, (int) (11 * scale)), HealthMenuStyle.AmberDim);
        Value = HealthMenuStyle.Text("", Math.Max(10, (int) (13 * scale)), HealthMenuStyle.AmberBright, true);
        Gauge = new HealthMenuProgressBar { MinHeight = 14 * scale };
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4, HorizontalExpand = true };
        if (stackedHeading)
        {
            AddChild(title);
            row.AddChild(Gauge);
            row.AddChild(Value);
            AddChild(row);
        }
        else
        {
            row.AddChild(title);
            row.AddChild(new Control { HorizontalExpand = true });
            row.AddChild(Value);
            AddChild(row);
            AddChild(Gauge);
        }
        _state = HealthMenuStyle.Text("", Math.Max(9, (int) (10 * scale)), HealthMenuStyle.AmberDim);
        _state.Visible = false;
        AddChild(_state);
    }

    public void SetReading(float fraction, string value, string state, bool alert)
    {
        Gauge.Fraction = fraction;
        Gauge.FillTint = alert ? HealthMenuStyle.Red : Color.White;
        Value.Text = value;
        Value.FontColorOverride = alert ? HealthMenuStyle.Red : HealthMenuStyle.AmberBright;
        _state.Text = state;
        _state.Visible = state.Length > 0;
        _state.FontColorOverride = alert ? HealthMenuStyle.Red : HealthMenuStyle.AmberDim;
    }
}
