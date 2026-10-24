using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client.HealthAnalyzer.UI;
using Content.Client.Hands.Systems;
using Content.Client.Inventory;
using Content.Client.UserInterface.Systems.Storage;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Input;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Audio;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>Shared palette and font helpers for the fullscreen health menu (amber terminal look).</summary>
internal static class HealthMenuStyle
{
    public const string TextureDir = "/Textures/_Eclipse/Interface/AdvancedHealth";

    public static readonly Color Amber = Color.FromHex("#f2a53c");
    public static readonly Color AmberBright = Color.FromHex("#ffc45e");
    public static readonly Color AmberDim = Color.FromHex("#c9a477");
    public static readonly Color AmberBorder = Color.FromHex("#b87827cc");
    public static readonly Color Red = Color.FromHex("#e0554f");
    public static readonly Color Backdrop = Color.FromHex("#050302e3");
    public static readonly Color PanelBg = Color.FromHex("#0b0805f2");
    public static readonly Color BarBg = Color.FromHex("#100b06");
    public static readonly Color SeparatorColor = Color.FromHex("#b87827");

    // Matches AdvancedHealthBodyDollControl.GetColor so the legend is truthful.
    public static readonly Color SeverityHealthy = Color.FromHex("#57574e");
    public static readonly Color SeverityMinor = Color.FromHex("#e0a3a3");
    public static readonly Color SeverityModerate = Color.FromHex("#ef6a6a");
    public static readonly Color SeveritySevere = Color.FromHex("#d81b2a");
    public static readonly Color SeverityCritical = Color.FromHex("#8a0f18");

    public static Font Mono(int size, bool bold = false)
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        var path = bold ? "/Fonts/RobotoMono/RobotoMono-Bold.ttf" : "/Fonts/RobotoMono/RobotoMono-Regular.ttf";
        // Font resources already belong to the client's cache. A static Font cache would retain
        // resources across different clients and race when UI integration tests run in parallel.
        return new VectorFont(cache.GetResource<FontResource>(path), size);
    }

    public static Texture Tex(string name)
        => IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>($"{TextureDir}/{name}.png").Texture;

    public static TextureRect IconPath(string path, float size = 20f, Color? modulate = null) => new()
    {
        Texture = IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>(path).Texture,
        Stretch = TextureRect.StretchMode.KeepAspectCentered,
        SetSize = new Vector2(size, size),
        MinSize = new Vector2(size, size),
        VerticalAlignment = Control.VAlignment.Center,
        Modulate = modulate ?? Color.White,
    };

    public static StyleBoxTexture PanelBox()
    {
        // Reuse Nano's rounded nine-patch, so corners stay the same size when a card stretches.
        var panel = new StyleBoxTexture
        {
            Texture = IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>(
                "/Textures/Interface/Nano/rounded_button_bordered.svg.96dpi.png").Texture,
            Modulate = Color.FromHex("#0c0907fc"),
        };
        panel.SetPatchMargin(StyleBox.Margin.All, 5);
        return panel;
    }

    /// <summary>Horizontal section divider.</summary>
    public static Control Separator(int verticalMargin = 5) => new HealthMenuSectionDivider
    {
        Margin = new Thickness(0, verticalMargin, 0, verticalMargin),
    };

    public static Label Text(string text, int size, Color color, bool bold = false) => new()
    {
        Text = text,
        FontOverride = Mono(size, bold),
        FontColorOverride = color,
        VerticalAlignment = Control.VAlignment.Center,
    };

    public static TextureRect Icon(string name, float size = 20f, Color? modulate = null) => new()
    {
        Texture = Tex(name),
        Stretch = TextureRect.StretchMode.KeepAspectCentered,
        SetSize = new Vector2(size, size),
        MinSize = new Vector2(size, size),
        VerticalAlignment = Control.VAlignment.Center,
        Modulate = modulate ?? Color.White,
    };


}

/// <summary>Solid amber separator with a short illuminated leading edge.</summary>
public sealed class HealthMenuSectionDivider : Control
{

    public bool Vertical;
    public Color LineColor = HealthMenuStyle.SeparatorColor;

    public HealthMenuSectionDivider(bool vertical = false)
    {
        Vertical = vertical;
        if (vertical)
        {
            MinWidth = 2;
            VerticalExpand = true;
        }
        else
        {
            MinHeight = 2;
            HorizontalExpand = true;
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        if (box.Width <= 0 || box.Height <= 0)
            return;

        var start = Vertical ? new Vector2(box.Center.X, box.Top) : new Vector2(box.Left, box.Center.Y);
        var end = Vertical ? new Vector2(box.Center.X, box.Bottom) : new Vector2(box.Right, box.Center.Y);
        handle.DrawLine(start, end, LineColor.WithAlpha(0.55f));
        var accentEnd = start + (Vertical ? new Vector2(0, Math.Min(24 * UIScale, box.Height))
            : new Vector2(Math.Min(36 * UIScale, box.Width), 0));
        handle.DrawLine(start, accentEnd, HealthMenuStyle.AmberBright);

    }
}

/// <summary>Technical meter with fixed-width edges and calibrated ticks, independent of aspect ratio.</summary>
public sealed class HealthMenuProgressBar : Control
{
    public float Fraction;
    public Color FillTint = Color.White;
    public int Segments = 10;

    public HealthMenuProgressBar()
    {
        MinHeight = 14;
        HorizontalExpand = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        var inset = 3f * UIScale;
        if (box.Width <= inset * 2 || box.Height <= inset * 2)
            return;
        var inside = new UIBox2(box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset);
        handle.DrawRect(box, HealthMenuStyle.BarBg);
        handle.DrawRect(box, HealthMenuStyle.AmberBorder.WithAlpha(0.5f), false);
        var fraction = Math.Clamp(Fraction, 0f, 1f);
        if (fraction > 0f)
        {
            var fill = new UIBox2(inside.Left, inside.Top, inside.Left + inside.Width * fraction, inside.Bottom);
            handle.DrawRect(fill, HealthMenuStyle.Amber * FillTint);
            handle.DrawLine(fill.TopLeft, fill.TopRight, HealthMenuStyle.AmberBright.WithAlpha(0.5f));
        }
        for (var i = 1; i < Segments; i++)
        {
            var x = inside.Left + inside.Width * i / Segments;
            handle.DrawLine(new Vector2(x, inside.Bottom - 2 * UIScale), new Vector2(x, inside.Bottom), HealthMenuStyle.BarBg);
        }
    }
}

/// <summary>Brain integrity shown on a large anatomical symbol, not a stretched bar.</summary>
public sealed class HealthChipBrainGauge : Control
{
    private readonly Texture _brain = HealthMenuStyle.Tex("Lucide/brain");
    public float Fraction;
    public Color FillColor = HealthMenuStyle.Amber;
    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        var side = Math.Min(box.Width, box.Height);
        var square = UIBox2.FromDimensions(box.Center - new Vector2(side / 2), new Vector2(side));
        handle.DrawTextureRect(_brain, square, HealthMenuStyle.AmberDim.WithAlpha(0.25f));
        var fill = Math.Clamp(Fraction, 0, 1);
        if (fill <= 0) return;
        handle.DrawTextureRectRegion(_brain,
            new UIBox2(square.Left, square.Bottom - side * fill, square.Right, square.Bottom),
            new UIBox2(0, _brain.Height * (1 - fill), _brain.Width, _brain.Height), FillColor);
    }
}

/// <summary>The original chip thermometer artwork, with its authored mercury revealed bottom-up.</summary>
public sealed class HealthMenuThermometer : Control
{
    private readonly Texture _empty = HealthMenuStyle.Tex("temperature_low");
    private readonly Texture _full = HealthMenuStyle.Tex("temperature_full");
    public float Fraction = 0.5f;
    public string Reading = "";
    public Color ReadingColor = HealthMenuStyle.AmberBright;
    public Font ReadingFont = HealthMenuStyle.Mono(15, true);
    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        handle.DrawTextureRect(_empty, box);
        var cut = 0.667f - Math.Clamp(Fraction, 0, 1) * (0.667f - 0.146f);
        handle.DrawTextureRectRegion(_full,
            new UIBox2(box.Left, box.Top + box.Height * cut, box.Right, box.Bottom),
            new UIBox2(0, _full.Height * cut, _full.Width, _full.Height));
        // The authored callout lies on row 22 of the 48px image. Text uses that exact baseline.
        handle.DrawString(ReadingFont, new Vector2(box.Left + 2 * UIScale, box.Top + box.Height * 22f / 48f),
            Reading, UIScale, ReadingColor);
    }
}

/// <summary>A heart monitor icon whose visible pulse is driven by the patient's real heart rate.</summary>
public sealed class HealthMenuHeartIcon : Control
{
    public float Pulse;
    public bool Beating = true;

    public HealthMenuHeartIcon(float size)
    {
        SetSize = new Vector2(size, size);
        MinSize = new Vector2(size, size);
        VerticalAlignment = VAlignment.Center;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        if (box.Width <= 2 || box.Height <= 2)
            return;

        // At rest the heart has a small margin. A real beat expands it into that margin and
        // briefly brightens it, without changing the header's measured size or moving the BP text.
        var pulse = Math.Clamp(Pulse, 0f, 1f);
        var inset = MathHelper.Lerp(MathF.Min(box.Width, box.Height) * 0.11f, 0f, pulse);
        var rect = new UIBox2(box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset);
        var color = Beating
            ? Color.InterpolateBetween(Color.FromHex("#b83932"), Color.FromHex("#ff6657"), pulse)
            : Color.FromHex("#5a2925");

        Vector2 Point(float x, float y) => new(rect.Left + rect.Width * x, rect.Top + rect.Height * y);
        var outline = new Vector2[]
        {
            Point(0.50f, 0.92f),
            Point(0.14f, 0.57f),
            Point(0.08f, 0.38f),
            Point(0.12f, 0.22f),
            Point(0.25f, 0.11f),
            Point(0.39f, 0.13f),
            Point(0.50f, 0.27f),
            Point(0.61f, 0.13f),
            Point(0.75f, 0.11f),
            Point(0.88f, 0.22f),
            Point(0.92f, 0.38f),
            Point(0.86f, 0.57f),
        };
        var fill = new Vector2[]
        {
            Point(0.50f, 0.46f),
            outline[0], outline[1], outline[2], outline[3], outline[4], outline[5],
            outline[6], outline[7], outline[8], outline[9], outline[10], outline[11], outline[0],
        };
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, fill, color);
        handle.DrawPrimitives(DrawPrimitiveTopology.LineLoop, outline, Color.InterpolateBetween(color, Color.White, 0.2f));
    }
}

/// <summary>Plain shield used for skin/protection values; unlike immunity it has no virus mark.</summary>
public sealed class HealthMenuShieldIcon : Control
{
    public HealthMenuShieldIcon(float size)
    {
        SetSize = new Vector2(size, size);
        MinSize = new Vector2(size, size);
        VerticalAlignment = VAlignment.Center;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        var c = box.Center;
        var w = box.Width;
        var h = box.Height;
        var shield = new Vector2[]
        {
            new(c.X, box.Top + h * 0.08f),
            new(box.Right - w * 0.13f, box.Top + h * 0.24f),
            new(box.Right - w * 0.20f, box.Top + h * 0.65f),
            new(c.X, box.Bottom - h * 0.07f),
            new(box.Left + w * 0.20f, box.Top + h * 0.65f),
            new(box.Left + w * 0.13f, box.Top + h * 0.24f),
        };
        handle.DrawPrimitives(DrawPrimitiveTopology.LineLoop, shield, HealthMenuStyle.AmberBright);
        handle.DrawLine(
            new Vector2(c.X, box.Top + h * 0.17f),
            new Vector2(c.X, box.Bottom - h * 0.18f),
            HealthMenuStyle.AmberDim);
    }
}

/// <summary>
/// Scrolling ECG monitor trace. Paper scrolls at a constant speed so faster heart rates place the
/// QRS complexes closer together, and the trace amplitude fades to a flatline instead of snapping.
/// </summary>
/// <summary>
/// A bedside-monitor ECG: the trace is drawn into a fixed buffer that stays in place while a bright
/// sweep head travels left→right, erasing a small gap ahead of it and laying down fresh signal behind —
/// so the waveform doesn't scroll. Complex spacing follows the real heart rate.
/// </summary>
public sealed class HealthMenuEcgControl : Control
{
    /// <summary>Beats per minute driving the complex spacing.</summary>
    public float Rate = 70f;
    /// <summary>Whether the heart is beating; when false the trace decays toward a flatline.</summary>
    public bool Beating = true;

    private static readonly Color EcgBg = Color.FromHex("#080603");
    private static readonly Color EcgGrid = Color.FromHex("#21170b");
    private static readonly Color EcgGridStrong = Color.FromHex("#392713");

    private float[] _samples = Array.Empty<float>();
    private int _bufWidth;      // physical width the buffer is sized for
    private float _sweep;       // sweep-head position, in pixels
    private float _phase;       // beat phase, 0..1
    private float _strength = 1f;

    private const float Step = 1f;           // px between stored samples
    private const float SweepSeconds = 3.2f; // time for the head to cross the whole strip
    private const int BlankSteps = 5;        // samples blanked just ahead of the head

    public HealthMenuEcgControl()
    {
        MinHeight = 58;
        HorizontalExpand = true;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        var dt = args.DeltaSeconds;
        var target = Beating ? 1f : 0f;
        _strength = MathHelper.Lerp(_strength, target, Math.Clamp(dt * 2.2f, 0f, 1f));

        // Beat phase advances at the real rate; it freezes as the trace flatlines.
        var beatsPerSec = _strength > 0.02f ? Math.Max(Rate, 1f) / 60f : 0f;
        _phase += beatsPerSec * dt;
        _phase -= MathF.Floor(_phase);

        if (_bufWidth <= 0 || _samples.Length == 0)
            return;

        // Move the head and write the signal into every column it crossed this frame.
        var pxPerSec = _bufWidth / SweepSeconds;
        var prev = _sweep;
        _sweep += pxPerSec * dt;
        if (_sweep >= _bufWidth)
        {
            _sweep -= _bufWidth;
            WriteColumns(prev, _bufWidth, _phase - _sweep / pxPerSec * beatsPerSec, beatsPerSec);
            WriteColumns(0f, _sweep, _phase, beatsPerSec);
        }
        else
        {
            WriteColumns(prev, _sweep, _phase, beatsPerSec);
        }
    }

    private void WriteColumns(float from, float to, float phaseAtEnd, float beatsPerSecond)
    {
        var n = _samples.Length;
        var i0 = Math.Clamp((int) (from / Step), 0, n - 1);
        var i1 = Math.Clamp((int) (to / Step), 0, n - 1);
        var pixelsPerSecond = _bufWidth / SweepSeconds;
        for (var i = i0; i <= i1; i++)
        {
            // Sample at each pixel's time, not once per rendered frame. This keeps QRS peaks sharp
            // and consistent when rendering frames take different amounts of time.
            var phase = phaseAtEnd - (to - i * Step) / pixelsPerSecond * beatsPerSecond;
            phase -= MathF.Floor(phase);
            _samples[i] = EcgShape(phase) * _strength;
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        var w = (int) box.Width;
        var h = (int) box.Height;
        if (w <= 4 || h <= 4)
            return;

        var count = Math.Max(2, (int) (w / Step) + 1);
        if (_bufWidth != w || _samples.Length != count)
        {
            _samples = new float[count];
            _bufWidth = w;
            _sweep = 0f;
        }

        DrawGrid(handle, box);

        var mid = box.Top + h * 0.56f;
        var amp = h * 0.40f;
        // Amber when beating strongly, fading to red as it flatlines.
        var color = Color.InterpolateBetween(HealthMenuStyle.Red, HealthMenuStyle.Amber, _strength);
        var sweepIdx = (int) (_sweep / Step);

        Vector2? prev = null;
        for (var i = 0; i < _samples.Length; i++)
        {
            // Blank a few samples just ahead of the head so the previous pass is visibly wiped.
            var ahead = (i - sweepIdx + _samples.Length) % _samples.Length;
            if (ahead >= 1 && ahead <= BlankSteps)
            {
                prev = null;
                continue;
            }

            var point = new Vector2(box.Left + i * Step, mid - _samples[i] * amp);
            if (prev is { } p)
            {
                var age = (sweepIdx - i + _samples.Length) % _samples.Length / (float) _samples.Length;
                var ink = color.WithAlpha(0.25f + 0.75f * (1f - age));
                handle.DrawLine(p, point, ink);
                handle.DrawLine(p + new Vector2(0, UIScale), point + new Vector2(0, UIScale), ink.WithAlpha(0.18f));
            }
            prev = point;
        }

        // Bright sweep head.
        var headX = box.Left + _sweep;
        var headColor = Color.InterpolateBetween(HealthMenuStyle.Red, HealthMenuStyle.AmberBright, _strength);
        var headY = mid - _samples[Math.Clamp(sweepIdx, 0, _samples.Length - 1)] * amp;
        handle.DrawRect(new UIBox2(headX - UIScale, headY - UIScale, headX + UIScale, headY + UIScale), headColor);
    }

    private static void DrawGrid(DrawingHandleScreen handle, UIBox2 box)
    {
        handle.DrawRect(box, EcgBg);

        // Fine grid, with every fourth line a touch brighter.
        var col = 0;
        for (var x = box.Left; x <= box.Right; x += 12f, col++)
            handle.DrawLine(new Vector2(x, box.Top), new Vector2(x, box.Bottom), col % 4 == 0 ? EcgGridStrong : EcgGrid);
        var row = 0;
        for (var y = box.Top; y <= box.Bottom; y += 12f, row++)
            handle.DrawLine(new Vector2(box.Left, y), new Vector2(box.Right, y), row % 4 == 0 ? EcgGridStrong : EcgGrid);
    }

    /// <summary>Rough PQRST shape over one beat (phase 0..1), peak normalized to 1.</summary>
    private static float EcgShape(float p) => p switch
    {
        < 0.10f => 0f,
        < 0.18f => MathF.Sin((p - 0.10f) / 0.08f * MathF.PI) * 0.12f,
        < 0.22f => 0f,
        < 0.245f => -(p - 0.22f) / 0.025f * 0.18f,
        < 0.275f => -0.18f + (p - 0.245f) / 0.03f * 1.18f,
        < 0.305f => 1.0f - (p - 0.275f) / 0.03f * 1.32f,
        < 0.33f => -0.32f + (p - 0.305f) / 0.025f * 0.32f,
        < 0.42f => 0f,
        < 0.58f => MathF.Sin((p - 0.42f) / 0.16f * MathF.PI) * 0.28f,
        _ => 0f,
    };
}

/// <summary>
/// Loadout panel showing the player's hands, belt, back, pockets etc. as item sprites, with the
/// active hand highlighted. Supports the same interaction keys as the normal inventory UI.
/// </summary>
public sealed class HealthMenuToolbelt : Control
{
    private readonly IEntityManager _entities;
    private readonly IPlayerManager _player;
    private readonly EntityUid _owner;
    private float _s;
    private readonly List<Slot> _slots = new();

    private readonly StyleBoxFlat _normalSlot;
    private readonly StyleBoxFlat _activeSlot;
    private readonly StyleBoxFlat _storageSlot;

    private sealed class Slot
    {
        public string Tag = "";
        public string? HandId;
        public string? InventorySlot;
        public SpriteView View = default!;
        public TextureRect EmptyIcon = default!;
        public Label Title = default!;
        public Label? ItemName;
        public PanelContainer Frame = default!;
    }

    public HealthMenuToolbelt(EntityUid owner, float scale = 1f)
    {
        _entities = IoCManager.Resolve<IEntityManager>();
        _player = IoCManager.Resolve<IPlayerManager>();
        _owner = owner;
        _s = scale;
        _normalSlot = SlotBox(false, scale);
        _activeSlot = SlotBox(true, scale);
        _storageSlot = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#080502b3"),
            BorderColor = HealthMenuStyle.AmberBorder.WithAlpha(0.45f), BorderThickness = new Thickness(0, 0, 0, 1),
            ContentMarginLeftOverride = 3 * _s, ContentMarginRightOverride = 3 * _s,
            ContentMarginTopOverride = 3 * _s, ContentMarginBottomOverride = 3 * _s,
        };

        var panel = new PanelContainer { PanelOverride = HealthMenuStyle.PanelBox() };
        AddChild(panel);

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(10 * _s, 8 * _s),
            SeparationOverride = (int) (6 * _s),
        };
        panel.AddChild(box);
        box.AddChild(HealthMenuStyle.Text(
            Loc.GetString("advanced-health-menu-equipment").ToUpperInvariant(),
            Math.Max(1, (int) MathF.Round(14 * _s)), HealthMenuStyle.AmberBright, bold: true));

        var handsRow = new GridContainer
        {
            Name = "MedicalChipHands", Columns = 2, HorizontalExpand = true,
            HSeparationOverride = Math.Max(3, (int) (8 * _s)),
        };
        box.AddChild(handsRow);

        if (_entities.TryGetComponent(owner, out HandsComponent? hands))
        {
            var handSystem = _entities.System<SharedHandsSystem>();
            foreach (var handId in handSystem.EnumerateHands((owner, hands)))
            {
                var key = handSystem.TryGetHand((owner, hands), handId, out var hand)
                    ? hand.Value.Location switch { HandLocation.Left => "left", HandLocation.Right => "right", _ => "extra" }
                    : "extra";
                AddSlot(handsRow, Loc.GetString($"advanced-health-chip-hand-{key}"), handId: handId);
            }
        }

        box.AddChild(HealthMenuStyle.Separator(1));
        box.AddChild(HealthMenuStyle.Text(Loc.GetString("advanced-health-chip-storage"),
            Math.Max(9, (int) (10 * _s)), HealthMenuStyle.AmberDim));
        var storage = new GridContainer
        {
            Name = "MedicalChipStorage", Columns = 6, HorizontalExpand = true,
            HSeparationOverride = Math.Max(2, (int) (6 * _s)),
        };
        box.AddChild(storage);
        AddSlot(storage, Loc.GetString("advanced-health-slot-belt"), inv: "belt");
        AddSlot(storage, Loc.GetString("advanced-health-slot-back"), inv: "back");
        AddSlot(storage, "1", inv: "pocket1");
        AddSlot(storage, "2", inv: "pocket2");
        AddSlot(storage, Loc.GetString("advanced-health-slot-suit"), inv: "suitstorage");
        AddSlot(storage, "ID", inv: "id");

        Refresh();
    }

    private void AddSlot(GridContainer parent, string tag, string? handId = null, string? inv = null)
    {
        var isHand = handId != null;
        var iconSize = (isHand ? 36f : 28f) * _s;
        var iconArea = new Control { SetSize = new Vector2(iconSize), MinSize = new Vector2(iconSize) };
        var emptyName = inv switch
        {
            "suitstorage" => "suit_storage", "pocket1" or "pocket2" => "pocket", null => "hand_m", _ => inv,
        };
        if (handId != null && _entities.System<SharedHandsSystem>().TryGetHand(_owner, handId, out var hand))
            emptyName = hand.Value.Location == HandLocation.Left ? "hand_l_no_letter" : "hand_r_no_letter";
        var placeholder = HealthMenuStyle.IconPath($"/Textures/Interface/Minimalist/Slots/{emptyName}.png",
            iconSize, HealthMenuStyle.AmberDim.WithAlpha(0.5f));
        placeholder.MouseFilter = MouseFilterMode.Ignore;
        iconArea.AddChild(placeholder);
        var view = new SpriteView
        {
            OverrideDirection = Direction.South, SetSize = new Vector2(iconSize), MinSize = new Vector2(iconSize),
            MouseFilter = MouseFilterMode.Ignore, HorizontalAlignment = HAlignment.Center, VerticalAlignment = VAlignment.Center,
        };
        iconArea.AddChild(view);
        var frame = new PanelContainer
        {
            Name = isHand ? "MedicalChipHandSlot" : "MedicalChipStorageSlot",
            PanelOverride = isHand ? _normalSlot : _storageSlot,
            MouseFilter = MouseFilterMode.Stop, HorizontalExpand = isHand,
        };
        var cell = new BoxContainer
        {
            Orientation = isHand ? BoxContainer.LayoutOrientation.Horizontal : BoxContainer.LayoutOrientation.Vertical,
            MinWidth = (isHand ? 172 : 44) * _s, Margin = new Thickness(2 * _s),
            SeparationOverride = Math.Max(2, (int) (6 * _s)),
        };
        cell.AddChild(iconArea);
        var text = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        var caption = HealthMenuStyle.Text(tag, Math.Max(9, (int) (10 * _s)), HealthMenuStyle.AmberDim);
        if (!isHand)
            caption.HorizontalAlignment = HAlignment.Center;
        text.AddChild(caption);
        Label? itemName = null;
        if (isHand)
        {
            itemName = HealthMenuStyle.Text("", Math.Max(10, (int) (12 * _s)), HealthMenuStyle.AmberBright, true);
            itemName.ClipText = true;
            itemName.HorizontalExpand = true;
            text.AddChild(itemName);
        }
        cell.AddChild(text);
        frame.AddChild(cell);
        parent.AddChild(frame);
        var slot = new Slot
        {
            Tag = tag, HandId = handId, InventorySlot = inv, View = view, EmptyIcon = placeholder,
            Title = caption, ItemName = itemName, Frame = frame,
        };
        frame.OnKeyBindDown += args => OnSlotKeyBind(args, slot);
        _slots.Add(slot);
    }

    /// <summary>
    /// Left click takes/equips, right click opens the verb menu, and "Use" opens storage.
    /// These UI actions are predicted on the local player, so only fire on the player's own belt.
    /// </summary>
    private void OnSlotKeyBind(GUIBoundKeyEventArgs args, Slot slot)
    {
        if (_owner != _player.LocalEntity)
            return;

        if (args.Function == EngineKeyFunctions.UIClick)
        {
            if (slot.HandId is { } handId)
            {
                if (_entities.TryGetComponent(_owner, out HandsComponent? hands))
                    _entities.System<HandsSystem>().UIHandClick((_owner, hands), handId);
            }
            else if (slot.InventorySlot is { } inv)
            {
                _entities.System<ClientInventorySystem>().UIInventoryActivate(inv);
            }

            args.Handle();
        }
        else if (args.Function == ContentKeyFunctions.ActivateItemInWorld)
        {
            if (slot.HandId is { } handId)
                _entities.System<HandsSystem>().UIHandActivate(handId);
            else if (slot.InventorySlot is { } inv)
                _entities.System<ClientInventorySystem>().UIInventoryActivateItem(inv, _owner);

            args.Handle();
        }
        else if (args.Function == EngineKeyFunctions.UseSecondary)
        {
            if (slot.HandId is { } handId)
                _entities.System<HandsSystem>().UIHandOpenContextMenu(handId);
            else if (slot.InventorySlot is { } inv)
                _entities.System<ClientInventorySystem>().UIInventoryOpenContextMenu(inv, _owner);

            args.Handle();
        }
        else if (args.Function == ContentKeyFunctions.ExamineEntity)
        {
            if (slot.HandId is { } handId)
                _entities.System<HandsSystem>().UIInventoryExamine(handId);
            else if (slot.InventorySlot is { } inv)
                _entities.System<ClientInventorySystem>().UIInventoryExamine(inv, _owner);

            args.Handle();
        }
        else if (args.Function == ContentKeyFunctions.AltActivateItemInWorld)
        {
            if (slot.HandId is { } handId)
                _entities.System<HandsSystem>().UIHandAltActivateItem(handId);
            else if (slot.InventorySlot is { } inv)
                _entities.System<ClientInventorySystem>().UIInventoryAltActivateItem(inv, _owner);

            args.Handle();
        }
    }

    private static StyleBoxFlat SlotBox(bool active, float scale) => new()
    {
        BackgroundColor = HealthMenuStyle.BarBg,
        BorderColor = active ? HealthMenuStyle.AmberBright : HealthMenuStyle.AmberBorder,
        BorderThickness = new Thickness(active ? 2 : 1),
        ContentMarginLeftOverride = 3 * scale,
        ContentMarginRightOverride = 3 * scale,
        ContentMarginTopOverride = 3 * scale,
        ContentMarginBottomOverride = 3 * scale,
    };

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        // Refresh every frame so the active-hand highlight tracks the real hand in real time.
        Refresh();
    }

    private void Refresh()
    {
        _entities.TryGetComponent(_owner, out HandsComponent? hands);
        var handsSys = _entities.System<SharedHandsSystem>();
        var inv = _entities.System<InventorySystem>();
        var activeHand = hands != null ? handsSys.GetActiveHand((_owner, hands)) : null;

        foreach (var slot in _slots)
        {
            EntityUid? item = null;
            if (slot.HandId != null && hands != null)
                item = handsSys.GetHeldItem((_owner, hands), slot.HandId);
            else if (slot.InventorySlot != null && inv.TryGetSlotEntity(_owner, slot.InventorySlot, out var e))
                item = e;

            slot.View.SetEntity(item);
            var active = slot.HandId != null && slot.HandId == activeHand;
            slot.Frame.PanelOverride = slot.HandId == null ? _storageSlot : active ? _activeSlot : _normalSlot;
            slot.EmptyIcon.Visible = item == null;
            slot.View.Visible = item != null;
            slot.Title.Text = active ? Loc.GetString("advanced-health-chip-active-hand", ("hand", slot.Tag)) : slot.Tag;
            slot.Title.FontColorOverride = active ? HealthMenuStyle.AmberBright : HealthMenuStyle.AmberDim;
            var name = item is { } uid ? _entities.GetComponent<MetaDataComponent>(uid).EntityName
                : Loc.GetString("advanced-health-chip-slot-empty");
            if (slot.ItemName != null)
                slot.ItemName.Text = item != null ? name : Loc.GetString("advanced-health-chip-hand-free");
            var slotHint = slot.InventorySlot is "pocket1" or "pocket2"
                ? Loc.GetString("advanced-health-slot-pocket") + " " + slot.Tag : slot.Tag;
            slot.Frame.ToolTip = name + "\n" + Loc.GetString("advanced-health-slot-interact-hint", ("slot", slotHint));
        }
    }
}

/// <summary>
/// Implanted medical chip: organism-wide vitals, interactive anatomical map and per-part
/// conditions/treatments. Local visual effects never change authoritative medical state.
/// </summary>
public sealed class AdvancedHealthStatusWindow : BaseWindow
{
    private readonly IEntityManager _entities;
    private readonly IClyde _clyde;
    private readonly EntityUid _target;
    private readonly EntityUid _equipmentOwner;

    /// <summary>
    /// Uniform design-space scale for the complete menu, calculated directly from the client area.
    /// </summary>
    private float _s;

    /// <summary>Reference viewport for the three diagnostic columns.</summary>
    private const float DesignHeight = 820f;
    private const float DesignWidth = 1440f;

    private BoxContainer _leftColumn = default!;
    private AdvancedHealthBodyDollControl _bodyDoll = default!;
    private Control _dollArea = default!;
    private float _maxDollSide;
    private LayoutContainer _overlayLayer = default!;
    private Button _extractButton = default!;
    private BodyPartSlot? _extractSlot;

    private Label _brainLabel = default!;
    private HealthChipBrainGauge _brainGauge = default!;
    private HealthMenuProgressBar _consciousnessGauge = default!;
    private HealthMenuProgressBar _painGauge = default!;
    private HealthMenuThermometer _thermometer = default!;
    private SpriteView _heldCursor = default!;
    private RichTextLabel _zoneEquipment = default!;
    private BoxContainer _regionalSelector = default!;
    private Button _primaryRegionButton = default!;
    private Button _secondaryRegionButton = default!;
    private BodyPartSlot _primaryRegion = BodyPartSlot.Abdomen;
    private BodyPartSlot _secondaryRegion = BodyPartSlot.Pelvis;
    private HealthMenuProgressBar _bloodBar = default!;
    private Label _bloodLabel = default!;
    private BoxContainer _bleedRow = default!;
    private Label _bleedLabel = default!;
    private Label _consciousnessValue = default!;
    private Label _painValue = default!;
    private Label _immunityLabel = default!;
    private HealthChipLungGauge _lungsIcon = default!;
    private Label _respLabel = default!;
    private Label _radLabel = default!;
    private BoxContainer _radRow = default!;
    private Control _tempColumn = default!;
    private HealthMenuProgressBar _thirstBar = default!;
    private Label _thirstValue = default!;
    private BoxContainer _thirstRow = default!;
    private HealthMenuProgressBar _hungerBar = default!;
    private Label _hungerValue = default!;
    private BoxContainer _hungerRow = default!;
    private readonly StorageUIController _storageUi;
    private bool _storageModeAdjusted;
    private static int _healthWindowsOpen;
    private static bool _restoreStaticStorage;

    private Label _bpLabel = default!;
    private Label _pulseLabel = default!;
    private Label _o2Label = default!;
    private HealthMenuEcgControl _ecg = default!;
    private HealthMenuHeartIcon _heartIcon = default!;

    private Label _partName = default!;
    private Label _partSeverity = default!;
    private BoxContainer _zoneStats = default!;
    private HealthMenuProgressBar _zoneHealthBar = default!;
    private Label _zoneHealthValue = default!;
    private HealthMenuProgressBar _zoneSkinBar = default!;
    private Label _zoneSkinValue = default!;
    private Control _woundsSeparator = default!;
    private Label _woundsCaption = default!;
    private BoxContainer _woundsList = default!;
    private BoxContainer _zoneBottom = default!;
    private Label _zoneBleeding = default!;
    private Control _zoneBleedingCell = default!;
    private RichTextLabel _patientLabel = default!;
    private RichTextLabel _summaryLabel = default!;
    private HealthChipIndicator _shockIndicator = default!;
    private HealthChipIndicator _perfusionIndicator = default!;
    private HealthChipIndicator _reserveIndicator = default!;
    private HealthChipIndicator _sleepIndicator = default!;
    private RichTextLabel _functionDetails = default!;
    private BoxContainer _treatmentActions = default!;
    private HealthChipAlarmControl _chipAlarm = default!;
    private string _conditionSignature = "";
    private readonly List<(string Key, string Text, string Description, Color Color)> _conditionRows = new();

    private BodyPartSlot? _selectedSlot;
    private float _refreshAccumulator;
    private bool IsTargetDead => _entities.TryGetComponent<MobStateComponent>(_target, out var state) && state.CurrentState == MobState.Dead;
    private bool _closing;
    private bool _resizeQueued;
    private bool _resizeSubscribed;
    private Vector2i _pendingViewport;
    private bool _bleedCritical;
    private bool _vitalsCritical;
    private float _blinkTimer;
    private bool _blinkOn = true;
    private float _heartbeatTimer;
    private float _heartPulse;

    private static readonly SoundPathSpecifier HeartbeatSound = new("/Audio/_Lavaland/heartbeat.ogg");

    /// <summary>
    /// <paramref name="target"/> is the patient whose vitals/wounds are shown and treated;
    /// <paramref name="equipmentOwner"/> owns the toolbelt/held items used for treatment (the medic).
    /// For self-diagnosis both are the same entity.
    /// </summary>
    public AdvancedHealthStatusWindow(EntityUid target, EntityUid? equipmentOwner = null)
    {
        _entities = IoCManager.Resolve<IEntityManager>();
        _clyde = IoCManager.Resolve<IClyde>();
        _storageUi = IoCManager.Resolve<IUserInterfaceManager>().GetUIController<StorageUIController>();
        _target = target;
        _equipmentOwner = equipmentOwner ?? target;

        BuildLayout(_clyde.MainWindow.Size);
        _clyde.MainWindow.Resized += OnWindowResized;
        _resizeSubscribed = true;
    }

    /// <summary>
    /// Rebuilds the fixed-design controls for the current viewport. All measurements are authored
    /// from the same scale, which avoids partially resized rows and keeps the selected zone intact.
    /// </summary>
    private void BuildLayout(Vector2i viewportSize)
    {
        RemoveAllChildren();

        // MainWindow.Size matches the actual client area captured by the game. Do not apply either
        // ContentScale or UI scale here: both are already handled by Robust while arranging and
        // rendering controls. Applying either a second time is what made the large layout 20% too
        // small without reliably helping the 720p layout.
        var viewport = new Vector2(viewportSize.X, viewportSize.Y);
        var usableHeight = Math.Max(1f, viewport.Y - 24f);
        _s = Math.Clamp(
            MathF.Min(usableHeight / DesignHeight, viewport.X / DesignWidth),
            0.4f,
            1f);
        // Keep the body readable on large viewports while the available doll area still constrains
        // it on 720p. The texture contains transparent padding, so the control must be substantially
        // larger than the visible silhouette.
        _maxDollSide = 1050f * _s;

        MouseFilter = MouseFilterMode.Stop;
        // Fullscreen, non-draggable. Registering as a BaseWindow lets Escape close it through the
        // standard close-recent-window path (so it no longer also pops the game menu).
        Resizable = false;

        var backdrop = new PanelContainer
        {
            MouseFilter = MouseFilterMode.Stop,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = HealthMenuStyle.Backdrop,
                BorderColor = HealthMenuStyle.AmberBorder,
                BorderThickness = new Thickness(1),
            },
            Margin = new Thickness(6),
        };
        AddChild(backdrop);
        LayoutContainer.SetAnchorAndMarginPreset(backdrop, LayoutContainer.LayoutPreset.Wide);

        _chipAlarm = new HealthChipAlarmControl { MouseFilter = MouseFilterMode.Ignore };
        AddChild(_chipAlarm);
        LayoutContainer.SetAnchorAndMarginPreset(_chipAlarm, LayoutContainer.LayoutPreset.Wide);

        var main = new HealthChipColumns
        {
            Name = "MedicalChipColumns",
            LeftWidth = SystemPanelWidth * _s,
            RightWidth = 400 * _s,
            Gap = Math.Max(4, (int) MathF.Round(10 * _s)),
            DividerWidth = Math.Max(3f, 6 * _s),
            Margin = new Thickness(12 * _s),
        };
        AddChild(main);
        LayoutContainer.SetAnchorAndMarginPreset(main, LayoutContainer.LayoutPreset.Wide);

        // ---- Left column ---- (always one column, uniformly scaled to fit the viewport)
        _leftColumn = new BoxContainer
        {
            Name = "MedicalChipVitals",
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            VerticalAlignment = VAlignment.Stretch,
            HorizontalExpand = false,
            MinWidth = SystemPanelWidth * _s,
            MaxWidth = SystemPanelWidth * _s,
            SeparationOverride = Math.Max(4, (int) MathF.Round(12 * _s)),
        };
        main.AddChild(_leftColumn);

        // The patient belongs to the column: a root-level stretched row placed its contents
        // halfway down the screen, since BaseWindow is not a LayoutContainer.
        var patientCard = new PanelContainer { PanelOverride = HealthMenuStyle.PanelBox(), Name = "MedicalChipPatient" };
        _patientLabel = WrappedText("", 14, HealthMenuStyle.AmberBright, true);
        _patientLabel.Margin = new Thickness(12 * _s, 10 * _s);
        patientCard.AddChild(_patientLabel);
        _leftColumn.AddChild(patientCard);

        var readouts = new ScrollContainer
        {
            Name = "MedicalChipReadouts", VerticalExpand = true, HScrollEnabled = false,
        };
        _leftColumn.AddChild(readouts);
        var diagnostics = VCol();
        diagnostics.SeparationOverride = Math.Max(4, (int) (10 * _s));
        readouts.AddChild(diagnostics);
        var systemPanel = new PanelContainer { PanelOverride = HealthMenuStyle.PanelBox(), Name = "MedicalChipVitalCard" };
        diagnostics.AddChild(systemPanel);
        var body = VCol();
        body.Margin = new Thickness(12 * _s);
        body.SeparationOverride = Math.Max(3, (int) (5 * _s));
        systemPanel.AddChild(body);
        body.AddChild(SText(Loc.GetString("advanced-health-window-vitals").ToUpperInvariant(),
            16, HealthMenuStyle.AmberBright, true));
        var gauges = HRow();
        gauges.SeparationOverride = Math.Max(4, (int) (8 * _s));
        body.AddChild(gauges);
        var brain = VCol();
        brain.MinWidth = 96 * _s;
        var brainTitle = SText(Loc.GetString("advanced-health-chip-brain"), 12, HealthMenuStyle.AmberDim);
        brainTitle.HorizontalAlignment = HAlignment.Center;
        brain.AddChild(brainTitle);
        _brainGauge = new HealthChipBrainGauge
        {
            Name = "MedicalChipBrainGauge", SetSize = Sz(88, 112), HorizontalAlignment = HAlignment.Center,
        };
        brain.AddChild(_brainGauge);
        _brainLabel = SText("", 17, HealthMenuStyle.AmberBright, true);
        _brainLabel.HorizontalAlignment = HAlignment.Center;
        brain.AddChild(_brainLabel);
        brain.VerticalAlignment = VAlignment.Center;
        gauges.AddChild(brain);
        gauges.AddChild(new Control { HorizontalExpand = true });
        var temperature = VCol();
        temperature.MinWidth = 204 * _s;
        var temperatureTitle = SText(Loc.GetString("advanced-health-vital-temperature"), 12, HealthMenuStyle.AmberDim);
        temperatureTitle.HorizontalAlignment = HAlignment.Center;
        temperature.AddChild(temperatureTitle);
        _thermometer = new HealthMenuThermometer
        {
            Name = "MedicalChipThermometer", SetSize = Sz(204, 204),
            ReadingFont = HealthMenuStyle.Mono(Math.Max(12, (int) (16 * _s)), true),
        };
        temperature.AddChild(_thermometer);
        _tempColumn = temperature;
        _tempColumn.ToolTip = Loc.GetString("advanced-health-vital-temperature-tooltip");
        gauges.AddChild(temperature);
        _brainGauge.ToolTip = Loc.GetString("advanced-health-vital-brain-tooltip");

        var neural = new GridContainer { Columns = 2, HorizontalExpand = true, HSeparationOverride = Math.Max(6, (int) (12 * _s)) };
        var consciousness = new HealthChipIndicator(Loc.GetString("advanced-health-vital-consciousness"), _s);
        _consciousnessGauge = consciousness.Gauge;
        _consciousnessValue = consciousness.Value;
        consciousness.ToolTip = Loc.GetString("advanced-health-vital-consciousness-tooltip");
        neural.AddChild(consciousness);
        var pain = new HealthChipIndicator(Loc.GetString("advanced-health-vital-pain"), _s);
        _painGauge = pain.Gauge;
        _painValue = pain.Value;
        pain.ToolTip = Loc.GetString("advanced-health-vital-pain-tooltip");
        neural.AddChild(pain);
        body.AddChild(neural);
        var immunity = HRow();
        immunity.AddChild(SIcon("Lucide/shield-plus", 32, HealthMenuStyle.Amber));
        immunity.AddChild(SText(Loc.GetString("advanced-health-vital-immunity"), 12, HealthMenuStyle.AmberDim));
        immunity.AddChild(new Control { HorizontalExpand = true });
        _immunityLabel = SText("", 15, HealthMenuStyle.AmberBright, true);
        immunity.AddChild(_immunityLabel);
        immunity.ToolTip = Loc.GetString("advanced-health-vital-immunity-tooltip");
        body.AddChild(immunity);
        body.AddChild(HealthMenuStyle.Separator(2));
        var blood = new HealthChipIndicator(Loc.GetString("advanced-health-vital-blood"), _s);
        _bloodBar = blood.Gauge;
        _bloodLabel = blood.Value;
        blood.ToolTip = Loc.GetString("advanced-health-vital-blood-tooltip");
        body.AddChild(blood);

        _bleedRow = HRow();
        _bleedRow.ToolTip = Loc.GetString("advanced-health-vital-bleeding-tooltip");
        _bleedRow.AddChild(SIcon("Lucide/droplet", 30, HealthMenuStyle.Red));
        _bleedRow.AddChild(SText(Loc.GetString("advanced-health-chip-blood-loss"), 12, HealthMenuStyle.Red));
        _bleedLabel = SText("", 13, HealthMenuStyle.Red, true);
        _bleedRow.AddChild(_bleedLabel);
        body.AddChild(_bleedRow);

        var respiratory = HRow();
        body.AddChild(respiratory);
        _lungsIcon = new HealthChipLungGauge { MinSize = Sz(34, 34), SetSize = Sz(34, 34) };
        respiratory.AddChild(_lungsIcon);
        respiratory.AddChild(SText(Loc.GetString("advanced-health-chip-ventilation"), 12, HealthMenuStyle.AmberDim));
        respiratory.AddChild(new Control { HorizontalExpand = true });
        _respLabel = SText("", 14, HealthMenuStyle.AmberBright, true);
        respiratory.AddChild(_respLabel);
        var reserves = new GridContainer
        {
            Columns = 2, HorizontalExpand = true, HSeparationOverride = Math.Max(4, (int) (12 * _s)),
        };
        body.AddChild(reserves);
        _thirstRow = ResourceRow("thirst", "advanced-health-chip-hydration", out _thirstBar, out _thirstValue);
        _hungerRow = ResourceRow("hunger", "advanced-health-chip-nutrition", out _hungerBar, out _hungerValue);
        reserves.AddChild(_thirstRow);
        reserves.AddChild(_hungerRow);
        _radRow = HRow();
        _radRow.ToolTip = Loc.GetString("advanced-health-vital-radiation-tooltip");
        _radRow.AddChild(SIcon("rad", 24));
        _radRow.AddChild(SText(Loc.GetString("advanced-health-chip-radiation"), 12, HealthMenuStyle.AmberDim));
        _radLabel = SText("", 13, HealthMenuStyle.Amber);
        _radRow.AddChild(_radLabel);
        body.AddChild(_radRow);

        // ECG panel with a dashed inner frame.
        var ecgPanel = new PanelContainer { PanelOverride = HealthMenuStyle.PanelBox() };
        diagnostics.AddChild(ecgPanel);

        var ecgFrame = new PanelContainer();
        ecgPanel.AddChild(ecgFrame);

        var ecgBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(10 * _s, 8 * _s),
            SeparationOverride = Math.Max(2, (int) MathF.Round(4 * _s)),
        };
        ecgFrame.AddChild(ecgBox);

        var monitor = new GridContainer { Columns = 3, HorizontalExpand = true, HSeparationOverride = Math.Max(4, (int) (12 * _s)) };
        ecgBox.AddChild(monitor);
        var pulse = VCol();
        pulse.HorizontalExpand = true;
        var pulseHeading = HRow();
        _heartIcon = new HealthMenuHeartIcon(16 * _s);
        pulseHeading.AddChild(_heartIcon);
        pulseHeading.AddChild(SText(Loc.GetString("advanced-health-chip-pulse"), 11, HealthMenuStyle.AmberDim));
        pulse.AddChild(pulseHeading);
        _pulseLabel = SText("", 22, HealthMenuStyle.AmberBright, true);
        pulse.AddChild(_pulseLabel);
        monitor.AddChild(pulse);
        var pressure = VCol();
        pressure.HorizontalExpand = true;
        pressure.AddChild(SText(Loc.GetString("advanced-health-chip-pressure"), 11, HealthMenuStyle.AmberDim));
        _bpLabel = SText("", 17, HealthMenuStyle.Amber, true);
        _bpLabel.ToolTip = Loc.GetString("advanced-health-chip-pressure-tooltip");
        pressure.AddChild(_bpLabel);
        monitor.AddChild(pressure);
        var oxygen = VCol();
        oxygen.HorizontalExpand = true;
        oxygen.AddChild(SText("O2", 11, HealthMenuStyle.AmberDim));
        _o2Label = SText("", 17, HealthMenuStyle.Amber, true);
        oxygen.AddChild(_o2Label);
        monitor.AddChild(oxygen);
        _ecg = new HealthMenuEcgControl { MinHeight = 76 * _s };
        _ecg.ToolTip = Loc.GetString("advanced-health-chip-rhythm-hint");
        ecgBox.AddChild(_ecg);

        var statusPanel = new PanelContainer { PanelOverride = HealthMenuStyle.PanelBox(), Name = "MedicalChipSummary" };
        diagnostics.AddChild(statusPanel);
        var statusBox = VCol();
        statusBox.Margin = new Thickness(12 * _s);
        statusPanel.AddChild(statusBox);
        _summaryLabel = WrappedText("", 16, HealthMenuStyle.AmberBright, true);
        statusBox.AddChild(_summaryLabel);
        statusBox.AddChild(HealthMenuStyle.Separator());
        var systemic = new GridContainer
        {
            Columns = 2, HorizontalExpand = true,
            HSeparationOverride = Math.Max(6, (int) (14 * _s)), VSeparationOverride = Math.Max(6, (int) (10 * _s)),
        };
        statusBox.AddChild(systemic);
        _shockIndicator = new HealthChipIndicator(Loc.GetString("advanced-health-vital-shock"), _s);
        _perfusionIndicator = new HealthChipIndicator(Loc.GetString("advanced-health-chip-perfusion-short"), _s);
        _reserveIndicator = new HealthChipIndicator(Loc.GetString("advanced-exertion-reserve"), _s);
        _sleepIndicator = new HealthChipIndicator(Loc.GetString("advanced-exertion-sleep-debt"), _s);
        systemic.AddChild(_shockIndicator);
        systemic.AddChild(_perfusionIndicator);
        systemic.AddChild(_reserveIndicator);
        systemic.AddChild(_sleepIndicator);

        // Selected zone panel.
        var zonePanel = new PanelContainer
        {
            Name = "MedicalChipDetails",
            PanelOverride = HealthMenuStyle.PanelBox(),
            VerticalExpand = true,
            MinWidth = 400 * _s,
            MaxWidth = 400 * _s,
        };
        // Spare height has no border: it is spacing, not a giant empty diagnostic card.
        _leftColumn.AddChild(new HealthMenuToolbelt(_equipmentOwner, _s));
        var zone = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(16 * _s),
            VerticalExpand = true,
            SeparationOverride = Math.Max(2, (int) MathF.Round(4 * _s)),
        };
        zonePanel.AddChild(zone);

        var zoneHeader = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = Math.Max(3, (int) MathF.Round(10 * _s)),
        };
        zone.AddChild(zoneHeader);
        _partName = SText(
            Loc.GetString("advanced-health-ui-select-part").ToUpperInvariant(), 18, HealthMenuStyle.AmberBright, bold: true);
        zoneHeader.AddChild(_partName);
        zoneHeader.AddChild(new Control { HorizontalExpand = true });
        _partSeverity = SText("", 14, HealthMenuStyle.AmberDim, bold: true);
        zoneHeader.AddChild(_partSeverity);

        _regionalSelector = HRow();
        _regionalSelector.Visible = false;
        zone.AddChild(_regionalSelector);
        _primaryRegionButton = MedicalButton(Loc.GetString("advanced-health-part-abdomen"));
        _secondaryRegionButton = MedicalButton(Loc.GetString("advanced-health-part-pelvis"));
        _primaryRegionButton.ToggleMode = _secondaryRegionButton.ToggleMode = true;
        _primaryRegionButton.OnPressed += _ => OnPartSelected(_primaryRegion);
        _secondaryRegionButton.OnPressed += _ => OnPartSelected(_secondaryRegion);
        _regionalSelector.AddChild(_primaryRegionButton);
        _regionalSelector.AddChild(_secondaryRegionButton);
        _regionalSelector.ToolTip = Loc.GetString("advanced-health-chip-abdominal-procedure");

        // Two aggregate condition bars (+ internal health, shield = skin) and a small overall gauge.
        _zoneStats = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Visible = false,
            SeparationOverride = Math.Max(3, (int) MathF.Round(10 * _s)),
            Margin = new Thickness(0, 4 * _s, 0, 0),
        };
        zone.AddChild(_zoneStats);

        var zoneBars = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = Math.Max(2, (int) MathF.Round(6 * _s)),
        };
        _zoneStats.AddChild(zoneBars);

        zoneBars.AddChild(SText(Loc.GetString("advanced-health-chip-function"), 12, HealthMenuStyle.AmberDim));

        var zoneHealthRow = StatRow(zoneBars, _s);
        _zoneHealthBar = new HealthMenuProgressBar { Segments = 10, MinHeight = 20 * _s };
        zoneHealthRow.AddChild(_zoneHealthBar);
        _zoneHealthValue = SText("", 13, HealthMenuStyle.Amber, bold: true);
        _zoneHealthValue.MinWidth = 46 * _s;
        zoneHealthRow.AddChild(_zoneHealthValue);

        zoneBars.AddChild(SText(Loc.GetString("advanced-health-chip-tissues"), 12, HealthMenuStyle.AmberDim));
        var zoneSkinRow = StatRow(zoneBars, _s);
        _zoneSkinBar = new HealthMenuProgressBar { Segments = 10, MinHeight = 20 * _s };
        zoneSkinRow.AddChild(_zoneSkinBar);
        _zoneSkinValue = SText("", 13, HealthMenuStyle.Amber, bold: true);
        _zoneSkinValue.MinWidth = 46 * _s;
        zoneSkinRow.AddChild(_zoneSkinValue);


        _functionDetails = WrappedText("", 12, HealthMenuStyle.AmberDim);
        zone.AddChild(_functionDetails);

        _woundsSeparator = HealthMenuStyle.Separator(Math.Max(1, (int) MathF.Round(4 * _s)));
        _woundsSeparator.Visible = false;
        zone.AddChild(_woundsSeparator);
        _woundsCaption = SText(Loc.GetString("advanced-health-chip-conditions"), 14, HealthMenuStyle.AmberBright, true);
        _woundsCaption.Visible = false;
        zone.AddChild(_woundsCaption);

        var woundsScroll = new ScrollContainer
        {
            VerticalExpand = true, MinHeight = 56 * _s, HScrollEnabled = false,
            Name = "MedicalChipConditions",
        };
        zone.AddChild(woundsScroll);
        _woundsList = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };
        woundsScroll.AddChild(_woundsList);

        _treatmentActions = VCol();
        zone.AddChild(_treatmentActions);

        // Bottom cells: protection (shield) and bleeding (blood drop) — icon + value, not text.
        _zoneBottom = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = Math.Max(3, (int) MathF.Round(8 * _s)),
            Margin = new Thickness(0, 6 * _s, 0, 0),
            Visible = false,
        };
        zone.AddChild(_zoneBottom);
        _zoneEquipment = WrappedText("", 12, HealthMenuStyle.AmberDim);
        zone.AddChild(HealthMenuStyle.Separator());
        zone.AddChild(SText(Loc.GetString("advanced-health-chip-zone-equipment"), 13, HealthMenuStyle.AmberBright, true));
        zone.AddChild(_zoneEquipment);
        _zoneBleedingCell = MakeZoneCell(SIcon("blood_full", 26), "advanced-health-chip-blood-loss", out _zoneBleeding, _s);
        _zoneBleedingCell.ToolTip = Loc.GetString("advanced-health-vital-bleeding-tooltip");
        _zoneBottom.AddChild(_zoneBleedingCell);

        // Matching dividers separate both diagnostic columns from the body map.
        foreach (var name in new[] { "MedicalChipLeftDivider", "MedicalChipRightDivider" })
        {
            main.AddChild(new PanelContainer
            {
                Name = name,
                MinWidth = Math.Max(3f, 6 * _s),
                VerticalExpand = true,
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Color.FromHex("#050302b3"), BorderColor = HealthMenuStyle.SeparatorColor,
                    BorderThickness = new Thickness(1, 0, 1, 0),
                },
            });
        }

        // ---- Centre: body map ----
        var right = new BoxContainer
        {
            Name = "MedicalChipBody",
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = Math.Max(2, (int) MathF.Round(5 * _s)),
            Margin = new Thickness(0),
        };
        main.AddChild(right);

        var mapCaption = SText(Loc.GetString("advanced-health-chip-body"), 16, HealthMenuStyle.AmberBright, true);
        mapCaption.Margin = new Thickness(0, 8 * _s, 0, 0);
        right.AddChild(mapCaption);

        var legendCaption = WrappedText(Loc.GetString("advanced-health-chip-body-hint"), 12, HealthMenuStyle.AmberDim);
        legendCaption.Margin = new Thickness(0, 4 * _s, 0, 0);
        right.AddChild(legendCaption);

        legendCaption.Text = Loc.GetString("advanced-health-chip-body-hint");

        _dollArea = new HealthChipBodyArea
        {
            MaximumSide = _maxDollSide,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        right.AddChild(_dollArea);
        _bodyDoll = new AdvancedHealthBodyDollControl
        {
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            SetSize = new Vector2(700 * _s, 700 * _s),
            MinSize = Vector2.Zero,
            HoverSelect = false,
            GroupPelvisWithAbdomen = true,
            GroupNeckWithHead = true,
        };
        _bodyDoll.OnPartSelected += OnPartSelected;
        _bodyDoll.OnPartActivated += OnPartActivated;
        _dollArea.AddChild(_bodyDoll);
        main.AddChild(zonePanel);
        _conditionSignature = "";

        // Exit cross in a framed box, top-right corner.
        var closeButton = new TextureButton
        {
            StyleClasses = { DefaultWindow.StyleClassWindowCloseButton },
            Scale = new Vector2(Math.Max(1f, 1.6f * _s), Math.Max(1f, 1.6f * _s)),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
        };
        closeButton.OnPressed += _ => Close();

        var closeBox = new PanelContainer
        {
            HorizontalAlignment = HAlignment.Right,
            VerticalAlignment = VAlignment.Top,
            Margin = new Thickness(0),
            MinSize = new Vector2(36 * _s, 36 * _s),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = HealthMenuStyle.PanelBg,
                BorderColor = HealthMenuStyle.SeparatorColor,
                BorderThickness = new Thickness(2),
                ContentMarginLeftOverride = 5 * _s,
                ContentMarginRightOverride = 5 * _s,
                ContentMarginTopOverride = 5 * _s,
                ContentMarginBottomOverride = 5 * _s,
            },
        };
        closeBox.AddChild(closeButton);
        zoneHeader.AddChild(closeBox);

        // Floating overlay for the "Extract" button that appears over a limb with a foreign body.
        _overlayLayer = new LayoutContainer { MouseFilter = MouseFilterMode.Ignore };
        AddChild(_overlayLayer);
        LayoutContainer.SetAnchorAndMarginPreset(_overlayLayer, LayoutContainer.LayoutPreset.Wide);

        _extractButton = new Button
        {
            Text = Loc.GetString("advanced-health-action-foreignbodyremoval"),
            Visible = false,
        };
        _extractButton.OnPressed += _ => TriggerExtract();
        _overlayLayer.AddChild(_extractButton);
        _heldCursor = new SpriteView
        {
            Name = "MedicalChipHeldCursor", MouseFilter = MouseFilterMode.Ignore,
            SetSize = Sz(36, 36), OverrideDirection = Direction.South, Visible = false,
        };
        _overlayLayer.AddChild(_heldCursor);

        Refresh();
    }

    private void OnWindowResized(WindowResizedEventArgs args)
    {
        if (args.Window != _clyde.MainWindow)
            return;

        _pendingViewport = args.NewSize;
        if (_resizeQueued)
            return;

        // SDL can emit several resize events in one frame. Rebuild once after the current UI walk,
        // using the newest size, so children are never removed while Robust is enumerating them.
        _resizeQueued = true;
        Timer.Spawn(0, () =>
        {
            _resizeQueued = false;
            if (Parent == null || _closing || _pendingViewport.X <= 0 || _pendingViewport.Y <= 0)
                return;

            BuildLayout(_pendingViewport);
            LayoutContainer.SetAnchorAndMarginPreset(this, LayoutContainer.LayoutPreset.Wide);
        });
    }

    private void UnsubscribeResize()
    {
        if (!_resizeSubscribed)
            return;

        _clyde.MainWindow.Resized -= OnWindowResized;
        _resizeSubscribed = false;
    }

    protected override void Opened()
    {
        base.Opened();
        _closing = false;
        if (!_storageModeAdjusted)
        {
            if (_healthWindowsOpen++ == 0)
                _restoreStaticStorage = _storageUi.StaticStorageUIEnabled;
            _storageUi.StaticStorageUIEnabled = false;
            _storageModeAdjusted = true;
        }
        // The window root positions children by layout anchors; without this the menu
        // collapses to its content size in the top-left corner.
        LayoutContainer.SetAnchorAndMarginPreset(this, LayoutContainer.LayoutPreset.Wide);
        Refresh();
    }

    public override void Close()
    {
        if (_closing || Parent == null)
            return;

        // Defer the orphan: Close() can be reached from FrameUpdate/Refresh, and removing the
        // control mid-enumeration would break the UI frame update. base.Close() removes the
        // control and fires OnClose (used by the scanner BUI to remember a manual dismissal).
        _closing = true;
        Timer.Spawn(0, () =>
        {
            if (_closing && Parent != null)
            {
                UnsubscribeResize();
                RestoreStorageMode();
                base.Close();
            }
        });
    }

    private void RestoreStorageMode()
    {
        if (!_storageModeAdjusted)
            return;

        _storageModeAdjusted = false;
        _healthWindowsOpen = Math.Max(0, _healthWindowsOpen - 1);
        if (_healthWindowsOpen == 0)
            _storageUi.StaticStorageUIEnabled = _restoreStaticStorage;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnsubscribeResize();
            RestoreStorageMode();
        }
        base.Dispose(disposing);
    }

    private static BoxContainer StatRow(BoxContainer parent, float scale)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = Math.Max(3, (int) MathF.Round(8 * scale)),
        };
        parent.AddChild(row);
        return row;
    }

    // Scaled diagnostic cards. Layout containers, rather than fixed coordinates, own the spacing.

    private const float SystemPanelWidth = 460f;

    private Button MedicalButton(string text)
    {
        var button = new Button { Text = text, StyleBoxOverride = HealthMenuStyle.PanelBox() };
        button.Label.FontOverride = HealthMenuStyle.Mono(Math.Max(10, (int) (12 * _s)), true);
        button.Label.FontColorOverride = HealthMenuStyle.AmberBright;
        return button;
    }

    private BoxContainer VitalMetric(Control icon, string caption, out Label value)
    {
        var row = HRow();
        row.HorizontalExpand = true;
        row.Margin = new Thickness(2 * _s, 4 * _s);
        row.AddChild(icon);
        var text = VCol();
        text.HorizontalExpand = true;
        text.AddChild(SText(Loc.GetString(caption), 11, HealthMenuStyle.AmberDim));
        value = SText("", 15, HealthMenuStyle.AmberBright, true);
        text.AddChild(value);
        row.AddChild(text);
        return row;
    }

    private BoxContainer ResourceRow(string icon, string caption, out HealthMenuProgressBar bar, out Label value)
    {
        var tile = new HealthChipIndicator(Loc.GetString(caption), _s);
        tile.ToolTip = Loc.GetString($"advanced-health-vital-{icon}-tooltip");
        bar = tile.Gauge;
        value = tile.Value;
        return tile;
    }

    /// <summary>Scaled size vector for the system panel.</summary>
    private Vector2 Sz(float w, float h) => new(w * _s, h * _s);

    /// <summary>Scaled amber text; font point size follows the panel scale.</summary>
    private Label SText(string text, int size, Color color, bool bold = false)
        => HealthMenuStyle.Text(text, Math.Max(1, (int) MathF.Round(size * _s)), color, bold);

    /// <summary>Scaled texture icon.</summary>
    private TextureRect SIcon(string name, float size, Color? modulate = null)
        => HealthMenuStyle.Icon(name, size * _s, modulate);

    /// <summary>Compact horizontal row (icon + value) with tight spacing.</summary>
    private BoxContainer HRow() => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Horizontal,
        SeparationOverride = Math.Max(2, (int) MathF.Round(6 * _s)),
    };

    /// <summary>Compact stack used inside diagnostic cards.</summary>
    private BoxContainer VCol() => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        SeparationOverride = Math.Max(1, (int) MathF.Round(2 * _s)),
    };

    private void OnPartSelected(BodyPartSlot slot)
    {
        _selectedSlot = slot;
        _bodyDoll.SelectedSlot = _bodyDoll.DisplaySlot(slot);
        if (_entities.TryGetComponent<AdvancedHealthComponent>(_target, out var health))
            RefreshSelectedZone(health);
        else
            Refresh();
    }

    /// <summary>Clicking a part with a matching tool in hand starts that treatment's minigame.</summary>
    private void OnPartActivated(BodyPartSlot slot)
    {
        // The medic's hands/tools act on the patient (_target).
        var player = _equipmentOwner;
        if (!_entities.EntityExists(player))
            return;
        if (!_entities.TryGetComponent<AdvancedHealthComponent>(_target, out var health) ||
            !health.BodyParts.TryGetValue(slot, out var part))
            return;

        var client = _entities.System<AdvancedHealthClientSystem>();

        // A held blood/fluid pack transfuses (systemic — any limb click works).
        if (client.TryGetHeldBloodProduct(player, out var pack))
        {
            client.Transfuse(_target, pack);
            return;
        }

        if (!client.TryGetHeldTreatment(player, out var treatment, out var tool) ||
            !PartNeedsTreatment(part, treatment))
            return;

        var window = new AdvancedHealthTreatmentMinigameWindow(_target, slot, treatment, tool, MedicPain());
        IoCManager.Resolve<IUserInterfaceManager>().WindowRoot.AddChild(window);
        window.OpenCentered();
    }

    /// <summary>
    /// Pain that steadies/shakes the treating hand comes from the medic performing the procedure
    /// (the equipment owner), not the patient — a surgeon in agony can't operate on a calm patient.
    /// </summary>
    private float MedicPain()
    {
        return _entities.TryGetComponent<AdvancedHealthComponent>(_equipmentOwner, out var medic)
            ? medic.Pain
            : 0f;
    }

    /// <summary>Shows an "Extract" button above the hovered limb when it holds a foreign body.</summary>
    private void UpdateExtractButton()
    {
        // Foreign-body slot currently under the cursor, if any.
        BodyPartSlot? foreignSlot = null;
        if (_bodyDoll.HoveredSlot is { } slot &&
            _entities.TryGetComponent<AdvancedHealthComponent>(_target, out var health) &&
            health.BodyParts.TryGetValue(slot, out var part) &&
            (part.ForeignBodyCount > 0 || part.Wounds.Any(w => w.HasForeignBody)))
        {
            foreignSlot = slot;
        }

        // Keep the button up while the cursor is on it, so it can actually be reached and clicked.
        var overButton = _extractButton.Visible && new UIBox2(
            _extractButton.GlobalPosition, _extractButton.GlobalPosition + _extractButton.Size)
            .Contains(UserInterfaceManager.MousePositionScaled.Position);

        if (foreignSlot is { } fs)
            _extractSlot = fs;
        else if (!overButton)
        {
            _extractButton.Visible = false;
            _extractSlot = null;
            return;
        }

        if (_extractSlot is { } target)
        {
            var centroid = _bodyDoll.GlobalPosition - GlobalPosition + _bodyDoll.GetSlotCentroidLocal(target);
            var size = _extractButton.DesiredSize;
            LayoutContainer.SetPosition(_extractButton, centroid - new Vector2(size.X / 2f, size.Y + 6f));
            _extractButton.Visible = true;
        }
    }

    private void TriggerExtract()
    {
        var player = _equipmentOwner;
        if (_extractSlot is not { } slot ||
            !_entities.EntityExists(player) ||
            !_entities.TryGetComponent<AdvancedHealthComponent>(_target, out var health))
            return;

        var client = _entities.System<AdvancedHealthClientSystem>();
        // Forceps are optional; foreign-body removal is allowed bare-handed.
        client.TryFindLocalTool(player, AdvancedTreatmentType.ForeignBodyRemoval, out var tool);

        var window = new AdvancedHealthTreatmentMinigameWindow(
            _target, slot, AdvancedTreatmentType.ForeignBodyRemoval, tool, MedicPain());
        IoCManager.Resolve<IUserInterfaceManager>().WindowRoot.AddChild(window);
        window.OpenCentered();
    }

    /// <summary>A framed cell with an icon on top and a value below (for the bottom zone stats).</summary>
    private static Control MakeZoneCell(Control icon, string caption, out Label value, float scale)
    {
        var frame = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = HealthMenuStyle.PanelBox(),
        };
        var col = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = Math.Max(2, (int) MathF.Round(4 * scale)),
            Margin = new Thickness(4 * scale, 8 * scale),
        };
        frame.AddChild(col);
        icon.HorizontalAlignment = HAlignment.Center;
        col.AddChild(icon);
        var title = HealthMenuStyle.Text(Loc.GetString(caption), Math.Max(9, (int) (10 * scale)), HealthMenuStyle.AmberDim);
        title.HorizontalAlignment = HAlignment.Center;
        col.AddChild(title);
        value = HealthMenuStyle.Text("", Math.Max(1, (int) MathF.Round(16 * scale)), HealthMenuStyle.Amber, bold: true);
        value.HorizontalAlignment = HAlignment.Center;
        col.AddChild(value);
        return frame;
    }

    private void RefreshEquipment(BodyPartSlot slot)
    {
        var items = new List<string>();
        var inv = _entities.System<InventorySystem>();
        foreach (var name in ArmorSlotsFor(slot))
        {
            if (!inv.TryGetSlotEntity(_target, name, out var item) || item is not { } uid)
                continue;
            var description = _entities.GetComponent<MetaDataComponent>(uid).EntityName;
            if (_entities.TryGetComponent<BodyArmorRatingComponent>(uid, out var rating))
                description += " · " + Loc.GetString($"advanced-health-chip-armor-{rating.Class.ToString().ToLowerInvariant()}");
            items.Add(description);
        }
        _zoneEquipment.SetMessage(FormattedMessage.FromUnformatted(items.Count > 0
            ? string.Join("\n", items) : Loc.GetString("advanced-health-chip-no-zone-equipment")), HealthMenuStyle.AmberDim);
        _zoneEquipment.ToolTip = Loc.GetString("advanced-health-chip-equipment-rating-hint");
    }

    private static string[] ArmorSlotsFor(BodyPartSlot slot) => slot switch
    {
        BodyPartSlot.Head or BodyPartSlot.Neck => new[] { "head", "mask" },
        BodyPartSlot.LeftHand or BodyPartSlot.RightHand => new[] { "gloves", "outerClothing" },
        BodyPartSlot.LeftFoot or BodyPartSlot.RightFoot => new[] { "shoes", "outerClothing" },
        _ => new[] { "outerClothing", "jumpsuit" },
    };

    private static bool PartNeedsTreatment(BodyPartState part, AdvancedTreatmentType treatment)
        => AdvancedTreatmentRules.IsNeeded(part, treatment);

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // Blink the blood-loss text red at a critical bleed rate.
        _blinkTimer += args.DeltaSeconds;
        if (_blinkTimer >= 0.4f)
        {
            _blinkTimer = 0f;
            _blinkOn = !_blinkOn;
        }
        if (_bleedCritical)
            _bleedLabel.FontColorOverride = _blinkOn ? HealthMenuStyle.Red : Color.FromHex("#5a1512");
        if (_vitalsCritical)
            _bpLabel.FontColorOverride = _blinkOn ? HealthMenuStyle.Red : HealthMenuStyle.Amber;

        // Heartbeat is audible only here in the status menu. The header heart uses the same timer,
        // so its flash is synchronized with both the sound and the patient's actual heart rate.
        _heartPulse = Math.Max(0f, _heartPulse - args.DeltaSeconds * 4.5f);
        var heartBeating = _entities.TryGetComponent<AdvancedHealthComponent>(_target, out var hb) &&
                           !IsTargetDead && !hb.IsHeartStopped && hb.HeartRate > 20f;
        if (heartBeating && hb != null)
        {
            _heartbeatTimer += args.DeltaSeconds;
            var interval = 60f / hb.HeartRate;
            if (_heartbeatTimer >= interval)
            {
                _heartbeatTimer = 0f;
                _heartPulse = 1f;
                _entities.System<AudioSystem>().PlayGlobal(
                    HeartbeatSound, _target, AudioParams.Default.WithVolume(-7f));
            }
        }
        else
        {
            _heartbeatTimer = 0f;
        }
        _heartIcon.Beating = heartBeating;
        _heartIcon.Pulse = _heartPulse;

        UpdateExtractButton();
        var held = _entities.System<HandsSystem>().GetActiveHandEntity();
        _heldCursor.SetEntity(held);
        var cursor = UserInterfaceManager.MousePositionScaled.Position - GlobalPosition;
        _heldCursor.Visible = held != null && new UIBox2(Vector2.Zero, Size).Contains(cursor);
        LayoutContainer.SetPosition(_heldCursor, cursor + Sz(14, 14));
        _refreshAccumulator += args.DeltaSeconds;
        if (_refreshAccumulator < 0.5f)
            return;
        _refreshAccumulator = 0;
        Refresh();
    }

    private void Refresh()
    {
        // Keep the last shown state if the component is momentarily unavailable (e.g. replication
        // lag when opening on a remote patient); only close if the patient no longer exists.
        if (!_entities.TryGetComponent<AdvancedHealthComponent>(_target, out var health))
        {
            if (!_entities.EntityExists(_target))
                Close();
            return;
        }

        _bodyDoll.SetState(AdvancedHealthHudData.GetBodyParts(health));
        var functions = _entities.System<SharedMedicalConditionSystem>();
        foreach (var slot in health.BodyParts.Keys)
            _bodyDoll.SetFunction(slot, functions.GetFunction(health, slot));
        if (_selectedSlot == null || !health.BodyParts.ContainsKey(_selectedSlot.Value))
            _selectedSlot = health.BodyParts.ContainsKey(BodyPartSlot.Chest) ? BodyPartSlot.Chest : health.BodyParts.Keys.FirstOrDefault();
        _bodyDoll.SelectedSlot = _selectedSlot is { } displaySlot ? _bodyDoll.DisplaySlot(displaySlot) : null;

        _patientLabel.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("advanced-health-chip-patient",
            ("name", _entities.GetComponent<MetaDataComponent>(_target).EntityName))), HealthMenuStyle.AmberBright);
        var alarm = health.IsHeartStopped ? 1f : Math.Max(
            health.HasBlood ? 1f - health.BloodVolume / Math.Max(1f, health.MaxBloodVolume) : 0f,
            Math.Max(health.NeedsOxygen ? 1f - health.Oxygenation / 100f : 0f,
                Math.Max(health.Shock / 100f, 1f - health.Consciousness / 100f)));
        _chipAlarm.Severity = alarm;
        _chipAlarm.Enabled = !IsTargetDead;
        _summaryLabel.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(IsTargetDead ? "advanced-health-chip-dead" : health.IsHeartStopped ? "advanced-health-chip-arrest"
            : health.IsUnconscious ? "advanced-health-chip-unconscious"
            : health.IsSuffocating ? "advanced-health-chip-suffocating"
            : alarm >= 0.4f ? "advanced-health-chip-unstable" : "advanced-health-chip-stable")),
            alarm >= 0.4f ? HealthMenuStyle.Red : HealthMenuStyle.AmberBright);
        var shockKey = health.Shock < 10 ? "advanced-health-chip-shock-none"
            : health.Shock >= 50 ? "advanced-health-chip-shock-high" : "advanced-health-chip-shock-low";
        _shockIndicator.SetReading(health.Shock / 100f, $"{MathF.Round(health.Shock)}%",
            Loc.GetString(health.Shock < 10 ? "advanced-health-chip-state-absent"
                : health.Shock >= 50 ? "advanced-health-chip-state-danger" : "advanced-health-chip-state-stress"), health.Shock >= 50);
        _shockIndicator.ToolTip = Loc.GetString(shockKey, ("value", MathF.Round(health.Shock)));
        _perfusionIndicator.SetReading(health.Perfusion, $"{MathF.Round(health.Perfusion * 100)}%",
            Loc.GetString(health.Perfusion < 0.6f ? "advanced-health-chip-state-insufficient" : "advanced-health-chip-state-maintained"),
            health.Perfusion < 0.6f);
        _perfusionIndicator.ToolTip = Loc.GetString(health.Perfusion < 0.6f
            ? "advanced-health-chip-perfusion-low" : "advanced-health-chip-perfusion-normal", ("value", MathF.Round(health.Perfusion * 100)));
        if (_entities.TryGetComponent<AdvancedExertionComponent>(_target, out var exertion))
        {
            _reserveIndicator.SetReading(exertion.Reserve / 100f, $"{MathF.Round(exertion.Reserve)}%",
                Loc.GetString(exertion.Exhausted ? "advanced-exertion-exhausted-name" : exertion.Reserve < 40
                    ? "advanced-exertion-tired" : "advanced-exertion-ready"), exertion.Exhausted);
            _sleepIndicator.SetReading(exertion.SleepDebt / 100f, $"{MathF.Round(exertion.SleepDebt)}%",
                Loc.GetString(exertion.SleepDebt >= 35 ? "advanced-exertion-sleepy-name" : "advanced-exertion-rested"),
                exertion.SleepDebt >= 75);
            _reserveIndicator.ToolTip = Loc.GetString("advanced-exertion-reserve-hint");
            _sleepIndicator.ToolTip = Loc.GetString("advanced-exertion-sleep-hint");
        }
        else
        {
            _reserveIndicator.SetReading(0, "—", "", false);
            _sleepIndicator.SetReading(0, "—", "", false);
        }

        RefreshVitals(health);
        RefreshSelectedZone(health);
    }

    private void RefreshVitals(AdvancedHealthComponent health)
    {
        var brain = health.BodyParts.TryGetValue(BodyPartSlot.Head, out var head) && head.MaxOrganIntegrity > 0f
            ? (int) MathF.Round(head.OrganFraction * 100f)
            : 0;
        _brainLabel.Text = head is { MaxOrganIntegrity: > 0f } ? $"{brain}%" : "—";
        _brainLabel.FontColorOverride = brain > 50 ? HealthMenuStyle.AmberBright : HealthMenuStyle.Red;
        _brainGauge.Fraction = brain / 100f;
        _brainGauge.FillColor = brain > 50 ? HealthMenuStyle.Amber : HealthMenuStyle.Red;
        _consciousnessGauge.Fraction = health.Consciousness / 100f;
        _consciousnessGauge.FillTint = health.Consciousness > 25 ? Color.White : HealthMenuStyle.Red;
        _painGauge.Fraction = health.Pain / 100f;
        _painGauge.FillTint = health.Pain > 50 ? HealthMenuStyle.Red : Color.White;

        var bloodRatio = health.HasBlood ? health.BloodVolume / Math.Max(1f, health.MaxBloodVolume) : 0f;
        _bloodBar.Fraction = bloodRatio;
        _bloodBar.FillTint = bloodRatio > 0.5f ? Color.White : HealthMenuStyle.Red;
        _bloodLabel.Text = health.HasBlood ? Loc.GetString("advanced-health-menu-liters",
            ("value", (health.BloodVolume / 1000f).ToString("0.0", CultureInfo.InvariantCulture))) : "—";

        // Body fluid type and oxygen-carrying fraction, on hover.
        var groupText = BloodCompatibility.Format(health.BodyFluid, health.BloodType);
        var carry = (int) MathF.Round(Math.Clamp(health.OxygenCarryingCapacity, 0f, 1f) * 100f);
        _bloodLabel.ToolTip = _bloodBar.ToolTip = Loc.GetString("advanced-health-blood-group-tooltip",
            ("group", groupText), ("carry", carry));

        // Blood loss: hidden when none, blinking red at critical rate (>= 0.3 L/m).
        var bleedPerMinute = health.BodyParts.Values.Sum(health.GetBloodLossRate) * 60f / 1000f;
        _bleedRow.Visible = health.HasBlood && bleedPerMinute >= 0.005f;
        if (bleedPerMinute < 0.005f)
        {
            _bleedLabel.Visible = false;
            _bleedCritical = false;
        }
        else
        {
            _bleedLabel.Visible = true;
            _bleedLabel.Text = Loc.GetString("advanced-health-menu-liters-per-minute",
                ("value", bleedPerMinute.ToString("0.00", CultureInfo.InvariantCulture)));
            _bleedCritical = bleedPerMinute >= 0.3f;
            if (!_bleedCritical)
                _bleedLabel.FontColorOverride = HealthMenuStyle.Amber;
        }

        _consciousnessValue.Text = Loc.GetString("advanced-health-vital-percent",
            ("value", MathF.Round(health.Consciousness)));
        _consciousnessValue.FontColorOverride = health.Consciousness < 25f
            ? HealthMenuStyle.Red
            : HealthMenuStyle.Amber;

        _painValue.Text = Loc.GetString("advanced-health-vital-percent", ("value", MathF.Round(health.Pain)));
        _painValue.FontColorOverride = health.Pain > 50f ? HealthMenuStyle.Red : HealthMenuStyle.Amber;

        var immune = (int) MathF.Round(Math.Clamp(health.ImmuneDefense, 0f, 100f));
        _immunityLabel.Text = Loc.GetString("advanced-health-vital-percent", ("value", immune));
        _immunityLabel.FontColorOverride = immune < 40 ? HealthMenuStyle.Red
            : immune < 70 ? Color.FromHex("#e07b26")
            : HealthMenuStyle.Amber;

        _respLabel.Text = health.NeedsOxygen ? Loc.GetString("advanced-health-menu-liters-per-minute",
            ("value", health.MinuteVentilation.ToString("0.0", CultureInfo.InvariantCulture))) : "—";
        _lungsIcon.Oxygenation = health.NeedsOxygen ? health.Oxygenation / 100f : 0f;
        _lungsIcon.RespiratoryRate = health.RespiratoryRate;
        _lungsIcon.ToolTip = Loc.GetString("advanced-health-chip-breathing",
            ("oxygen", MathF.Round(health.Oxygenation)), ("rate", MathF.Round(health.RespiratoryRate)));
        var respiratoryColor = health.IsSuffocating || health.Oxygenation < health.CriticalOxygenation
            ? HealthMenuStyle.Red
            : health.Oxygenation < 90f
                ? Color.FromHex("#e07b26")
                : HealthMenuStyle.Amber;
        _respLabel.FontColorOverride = respiratoryColor;
        // The texture is authored amber, so white preserves it while danger states tint it.
        _lungsIcon.Modulate = Color.White;

        _radRow.Visible = health.RadiationLoad > 0f;
        _radLabel.Text = Loc.GetString("advanced-health-chip-radiation-load",
            ("value", MathF.Round(health.RadiationLoad)));

        {
            // BodyTemperature (Kelvin) is mirrored into AdvancedHealth on the server because the core
            // TemperatureComponent is not networked to the client.
            _tempColumn.Visible = true;
            _thermometer.Reading = Loc.GetString("advanced-health-vital-temperature-value",
                ("value", (health.BodyTemperature - 273.15f).ToString("0.0", CultureInfo.InvariantCulture)));

            // Compare with the species-specific normal temperature supplied by the server.
            var normalK = health.NormalBodyTemperature;
            var deviation = health.BodyTemperature - normalK;
            _thermometer.Fraction = 0.5f + deviation / 10f;

            // Preserve the existing five-degree warning thresholds; this change is presentation only.
            _thermometer.ReadingColor = deviation >= 5f ? HealthMenuStyle.Red
                : deviation <= -5f ? Color.FromHex("#4fb4e0")
                : HealthMenuStyle.Amber;
        }

        Dictionary<ThirstThreshold, float>? thirstThresholds = null;
        if (_entities.TryGetComponent<ThirstComponent>(_target, out var thirst))
            thirstThresholds = thirst.ThirstThresholds;

        if (thirst != null && thirstThresholds != null &&
            thirstThresholds.TryGetValue(ThirstThreshold.OverHydrated, out var maxThirst) && maxThirst > 0f)
        {
            _thirstRow.Visible = true;
            var frac = Math.Clamp(thirst.CurrentThirst / maxThirst, 0f, 1f);
            _thirstBar.Fraction = frac;
            _thirstValue.Text = Loc.GetString("advanced-health-vital-percent", ("value", MathF.Round(frac * 100f)));
        }
        else
        {
            _thirstRow.Visible = false;
        }

        Dictionary<HungerThreshold, float>? hungerThresholds = null;
        if (_entities.TryGetComponent<HungerComponent>(_target, out var hunger))
            hungerThresholds = hunger.Thresholds;

        if (hunger != null && hungerThresholds != null &&
            hungerThresholds.TryGetValue(HungerThreshold.Overfed, out var maxHunger) && maxHunger > 0f)
        {
            _hungerRow.Visible = true;
            var current = _entities.System<HungerSystem>().GetHunger(hunger);
            var frac = Math.Clamp(current / maxHunger, 0f, 1f);
            _hungerBar.Fraction = frac;
            _hungerValue.Text = Loc.GetString("advanced-health-vital-percent", ("value", MathF.Round(frac * 100f)));
        }
        else
        {
            _hungerRow.Visible = false;
        }

        RefreshHeart(health);
    }

    /// <summary>Blood pressure / pulse read from the simulated cardiovascular state.</summary>
    private void RefreshHeart(AdvancedHealthComponent health)
    {
        var pulse = (int) MathF.Round(health.HeartRate);
        var stopped = IsTargetDead || health.IsHeartStopped;
        // Systolic/diastolic split from the mean arterial pressure (MAP ≈ dia + pulsePressure/3).
        var map = health.MeanArterialPressure;
        var pulsePressure = Math.Clamp(map * 0.45f, 0f, 60f);
        var sys = stopped ? 0 : (int) MathF.Round(map + pulsePressure * 2f / 3f);
        var dia = stopped ? 0 : (int) MathF.Round(map - pulsePressure / 3f);
        if (stopped)
            pulse = 0;

        _bpLabel.Text = $"{sys}/{dia}";
        _pulseLabel.Text = Loc.GetString("advanced-health-chip-pulse-value", ("value", pulse));
        _pulseLabel.FontColorOverride = pulse < 50 || pulse > 110 ? HealthMenuStyle.Red : HealthMenuStyle.AmberBright;

        // Heart rate / pressure out of the normal window blink red; a stopped heart stays red.
        var pulseOut = pulse < 50 || pulse > 110;
        var pressureOut = sys < 90 || sys > 160 || dia < 55 || dia > 95;
        _vitalsCritical = !stopped && (pulseOut || pressureOut || health.Perfusion < 0.6f);
        if (stopped)
            _bpLabel.FontColorOverride = HealthMenuStyle.Red;
        else if (!_vitalsCritical)
            _bpLabel.FontColorOverride = HealthMenuStyle.Amber;
        // (critical case is animated in FrameUpdate)

        _o2Label.Text = $"{MathF.Round(health.Oxygenation)}%";
        _o2Label.Visible = health.NeedsOxygen;
        _o2Label.FontColorOverride = health.Oxygenation < health.CriticalOxygenation
            ? HealthMenuStyle.Red
            : HealthMenuStyle.Amber;

        _ecg.Beating = !stopped;
        _ecg.Rate = health.HeartRate;
    }

    private void RefreshSelectedZone(AdvancedHealthComponent health)
    {
        if (_selectedSlot is not { } slot || !health.BodyParts.TryGetValue(slot, out var part))
        {
            _partName.Text = Loc.GetString("advanced-health-ui-select-part");
            _zoneStats.Visible = _zoneBottom.Visible = false;
            return;
        }

        var abdominal = slot is BodyPartSlot.Abdomen or BodyPartSlot.Pelvis;
        var cephalic = slot is BodyPartSlot.Head or BodyPartSlot.Neck;
        _primaryRegion = cephalic ? BodyPartSlot.Head : BodyPartSlot.Abdomen;
        _secondaryRegion = cephalic ? BodyPartSlot.Neck : BodyPartSlot.Pelvis;
        var grouped = abdominal || cephalic;
        _partName.Text = Loc.GetString($"advanced-health-part-{_bodyDoll.DisplaySlot(slot).ToString().ToLowerInvariant()}").ToUpperInvariant();
        var hasSecondary = health.BodyParts.TryGetValue(_secondaryRegion, out var secondaryPart);
        _regionalSelector.Visible = grouped && hasSecondary && (abdominal || HasPartDetails(secondaryPart!));
        _primaryRegionButton.Text = Loc.GetString($"advanced-health-part-{_primaryRegion.ToString().ToLowerInvariant()}");
        _secondaryRegionButton.Text = Loc.GetString($"advanced-health-part-{_secondaryRegion.ToString().ToLowerInvariant()}");
        _primaryRegionButton.Pressed = slot == _primaryRegion;
        _secondaryRegionButton.Pressed = slot == _secondaryRegion;
        _primaryRegionButton.Label.FontColorOverride = slot == _primaryRegion ? HealthMenuStyle.AmberBright : HealthMenuStyle.AmberDim;
        _secondaryRegionButton.Label.FontColorOverride = slot == _secondaryRegion ? HealthMenuStyle.AmberBright : HealthMenuStyle.AmberDim;
        var displayPart = part;
        var neighbourSlot = slot == _primaryRegion ? _secondaryRegion : _primaryRegion;
        if (grouped && health.BodyParts.TryGetValue(neighbourSlot, out var neighbouringPart)
            && (neighbouringPart.IsDestroyed || neighbouringPart.GetConditionStage() > part.GetConditionStage()))
            displayPart = neighbouringPart;
        var (severityText, severityColor) = PartSeverity(displayPart);
        _partSeverity.Text = Capitalize(severityText);
        _partSeverity.FontColorOverride = severityColor;
        RefreshZoneBars(health, slot, part);
        _zoneStats.Visible = _zoneBottom.Visible = _woundsCaption.Visible = _woundsSeparator.Visible = true;
        RefreshEquipment(slot);
        var bleed = health.GetBloodLossRate(part) * 0.06f;
        _zoneBleeding.Text = Loc.GetString("advanced-health-menu-liters-per-minute",
            ("value", bleed.ToString("0.00", CultureInfo.InvariantCulture)));
        _zoneBleedingCell.Visible = bleed >= 0.005f;
        _zoneBleeding.FontColorOverride = HealthMenuStyle.Red;

        _conditionRows.Clear();
        if (abdominal)
        {
            if (health.BodyParts.TryGetValue(BodyPartSlot.Abdomen, out var abdomen))
                AppendPartConditions(health, BodyPartSlot.Abdomen, abdomen, true);
            if (health.BodyParts.TryGetValue(BodyPartSlot.Pelvis, out var pelvis))
                AppendPartConditions(health, BodyPartSlot.Pelvis, pelvis, true);
        }
        else if (cephalic)
        {
            if (health.BodyParts.TryGetValue(BodyPartSlot.Head, out var head))
                AppendPartConditions(health, BodyPartSlot.Head, head, false);
            if (health.BodyParts.TryGetValue(BodyPartSlot.Neck, out var neck) && HasPartDetails(neck))
                AppendPartConditions(health, BodyPartSlot.Neck, neck, true);
        }
        else
            AppendPartConditions(health, slot, part, false);

        // Only rebuild cards when their content changes; scrolling/focus survive physiological updates.
        var signature = _bodyDoll.DisplaySlot(slot) + string.Join("\n", _conditionRows.Select(row => row.Key + row.Text + row.Description + row.Color));
        if (signature != _conditionSignature)
        {
            _conditionSignature = signature;
            _woundsList.RemoveAllChildren();
            if (_conditionRows.Count == 0)
            {
                var empty = WrappedText(Loc.GetString("advanced-health-chip-no-conditions"), 13, HealthMenuStyle.AmberDim);
                _woundsList.AddChild(empty);
            }
            foreach (var row in _conditionRows)
            {
                var frame = new PanelContainer
                {
                    PanelOverride = new StyleBoxFlat
                    {
                        BackgroundColor = HealthMenuStyle.BarBg,
                        BorderColor = row.Color.WithAlpha(0.65f),
                        BorderThickness = new Thickness(2, 0, 0, 0),
                    },
                    Margin = new Thickness(0, 3 * _s),
                };
                var content = VCol();
                content.Margin = new Thickness(9 * _s, 7 * _s);
                var title = WrappedText(row.Text, 13, row.Color, true);
                var description = WrappedText(row.Description, 11, HealthMenuStyle.AmberDim);
                content.AddChild(title);
                content.AddChild(description);
                frame.AddChild(content);
                _woundsList.AddChild(frame);
            }
        }
        RefreshTreatmentActions(part);
    }

    private static bool HasPartDetails(BodyPartState part)
        => part.IsDestroyed || part.Wounds.Count > 0 || part.Conditions.Count > 0 || part.TissueFraction < 0.99f;

    private void AppendPartConditions(AdvancedHealthComponent health, BodyPartSlot slot, BodyPartState part, bool section)
    {
        var beneficial = HealthMenuStyle.AmberBright;
        var severityColor = PartSeverity(part).Color;
        var bleed = health.GetBloodLossRate(part) * 0.06f;
        var partTitle = Loc.GetString($"advanced-health-part-{slot.ToString().ToLowerInvariant()}");
        if (section)
            _conditionRows.Add(($"section-{slot}", partTitle.ToUpperInvariant(), "", HealthMenuStyle.AmberBright));
        if (part.IsDestroyed)
            Condition("destroyed", "advanced-health-window-status-destroyed", "advanced-health-chip-destroyed-detail", HealthMenuStyle.Red);

        foreach (var group in part.Wounds.GroupBy(wound => (wound.Type, wound.Severity)))
        {
            var count = group.Sum(wound => (int) wound.StackCount);
            var text = Loc.GetString("advanced-health-chip-wound",
                ("type", Loc.GetString($"advanced-health-wound-{group.Key.Type.ToString().ToLowerInvariant()}")),
                ("count", count),
                ("severity", Loc.GetString($"advanced-health-severity-{group.Key.Severity.ToString().ToLowerInvariant()}")));
            var description = Loc.GetString("advanced-health-chip-wound-detail",
                ("pain", MathF.Round(group.Sum(wound => wound.Pain))),
                ("dirty", Loc.GetString(group.Any(wound => wound.IsDirty)
                    ? "advanced-health-chip-contaminated" : "advanced-health-chip-no-contamination")));
            _conditionRows.Add(($"wound-{group.Key}", text, description, severityColor));
        }
        if (bleed > 0.005f)
        {
            var internalBleed = part.Wounds.Sum(wound => wound.InternalBleedingRate) * 0.06f;
            _conditionRows.Add(("bleeding", Loc.GetString("advanced-health-cond-bleeding"),
                Loc.GetString("advanced-health-chip-bleeding-detail", ("total", bleed.ToString("0.00", CultureInfo.InvariantCulture)),
                    ("internal", internalBleed.ToString("0.00", CultureInfo.InvariantCulture))), HealthMenuStyle.Red));
        }
        if (part.ForeignBodyCount > 0 || part.Wounds.Any(wound => wound.HasForeignBody))
            _conditionRows.Add(("foreign", Loc.GetString("advanced-health-cond-foreign-count",
                    ("count", Math.Max(part.ForeignBodyCount, part.Wounds.Count(wound => wound.HasForeignBody)))),
                Loc.GetString("advanced-health-chip-foreign-detail"), HealthMenuStyle.Amber));
        if (part.IsBandaged || part.Wounds.Any(wound => wound.IsBandaged))
            Condition("bandage", "advanced-health-window-status-bandaged", "advanced-health-chip-bandage-detail", beneficial);
        if (part.Wounds.Any(wound => wound.IsSutured))
            Condition("suture", "advanced-health-wound-flag-sutured", "advanced-health-chip-suture-detail", beneficial);
        if (part.IsSplinted)
            Condition("splint", "advanced-health-window-status-splinted", "advanced-health-chip-splint-detail", beneficial);
        if (part.HasTourniquet)
            Condition("tourniquet", "advanced-health-window-status-tourniquet", "advanced-health-chip-tourniquet-detail", HealthMenuStyle.Amber);

        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        foreach (var state in part.Conditions)
        {
            if (prototypes.TryIndex(state.Prototype, out var prototype))
                _conditionRows.Add(($"custom-{prototype.ID}", Loc.GetString(prototype.Name),
                    Loc.GetString(prototype.Description), prototype.Beneficial ? beneficial : HealthMenuStyle.Red));
        }

    }

    private void Condition(string key, string title, string description, Color color)
        => _conditionRows.Add((key, Loc.GetString(title), Loc.GetString(description), color));

    private readonly Dictionary<AdvancedTreatmentType, Button> _treatmentButtons = new();

    private void RefreshTreatmentActions(BodyPartState part)
    {
        var client = _entities.System<AdvancedHealthClientSystem>();
        foreach (var treatment in Enum.GetValues<AdvancedTreatmentType>())
        {
            if (!_treatmentButtons.TryGetValue(treatment, out var button) || button.Parent != _treatmentActions)
            {
                button = MedicalButton(Loc.GetString($"advanced-health-action-{treatment.ToString().ToLowerInvariant()}"));
                button.OnPressed += _ => StartTreatment(treatment);
                _treatmentButtons[treatment] = button;
                _treatmentActions.AddChild(button);
            }
            button.Visible = PartNeedsTreatment(part, treatment);
            button.Disabled = !client.TryFindLocalTool(_equipmentOwner, treatment, out _);
            button.Label.FontColorOverride = button.Disabled ? HealthMenuStyle.AmberDim.WithAlpha(0.5f) : HealthMenuStyle.AmberBright;
            button.ToolTip = button.Disabled ? Loc.GetString("advanced-health-chip-needs-tool")
                : Loc.GetString("advanced-health-chip-treatment-hint");
        }
    }

    private void StartTreatment(AdvancedTreatmentType treatment)
    {
        if (_selectedSlot is not { } slot || !_entities.TryGetComponent<AdvancedHealthComponent>(_target, out var health)
            || !health.BodyParts.TryGetValue(slot, out var part) || !PartNeedsTreatment(part, treatment))
            return;
        var client = _entities.System<AdvancedHealthClientSystem>();
        if (!client.TryFindLocalTool(_equipmentOwner, treatment, out var tool))
            return;
        var window = new AdvancedHealthTreatmentMinigameWindow(_target, slot, treatment, tool, MedicPain());
        IoCManager.Resolve<IUserInterfaceManager>().WindowRoot.AddChild(window);
        window.OpenCentered();
    }

    private void RefreshZoneBars(AdvancedHealthComponent health, BodyPartSlot slot, BodyPartState part)
    {
        var function = IsTargetDead ? 0f : _entities.System<SharedMedicalConditionSystem>().GetFunction(health, slot);
        SetZoneBar(_zoneHealthBar, _zoneHealthValue, function * 100f);
        SetZoneBar(_zoneSkinBar, _zoneSkinValue, part.TissueFraction * 100f);
        _zoneHealthBar.ToolTip = Loc.GetString("advanced-health-chip-function-tooltip");
        _zoneSkinBar.ToolTip = Loc.GetString("advanced-health-chip-tissue-tooltip",
            ("skin", MathF.Round(part.SkinFraction * 100f)), ("muscle", MathF.Round(part.MuscleFraction * 100f)),
            ("bone", part.MaxBoneIntegrity > 0 ? MathF.Round(part.BoneFraction * 100f).ToString("0") : "—"),
            ("vessels", MathF.Round(part.VesselFraction * 100f)), ("nerves", MathF.Round(part.NerveFraction * 100f)),
            ("organs", part.MaxOrganIntegrity > 0 ? MathF.Round(part.OrganFraction * 100f).ToString("0") : "—"));
        var selectedRegion = slot is BodyPartSlot.Abdomen or BodyPartSlot.Pelvis or BodyPartSlot.Head or BodyPartSlot.Neck
            ? Loc.GetString($"advanced-health-part-{slot.ToString().ToLowerInvariant()}") + " · " : "";
        _functionDetails.SetMessage(FormattedMessage.FromUnformatted(selectedRegion + Loc.GetString(function <= 0.01f ? "advanced-health-chip-function-failed"
            : function < 0.99f ? "advanced-health-chip-function-impaired" : "advanced-health-chip-function-normal",
            ("penalty", MathF.Round((1f - function) * 100f)))),
            function <= 0.01f ? HealthMenuStyle.Red : HealthMenuStyle.AmberDim);
    }

    private RichTextLabel WrappedText(string text, int size, Color color, bool bold = false)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.Stylesheet = new Stylesheet(new[]
        {
            new StyleRule(new SelectorElement(typeof(RichTextLabel), null, null, null),
                new[] { new StyleProperty("font", HealthMenuStyle.Mono(Math.Max(9, (int) (size * _s)), bold)) }),
        });
        label.SetMessage(FormattedMessage.FromUnformatted(text), color);
        return label;
    }

    private static void SetZoneBar(HealthMenuProgressBar bar, Label value, float integrity)
    {
        bar.Fraction = Math.Clamp(integrity / 100f, 0f, 1f);
        // Amber while acceptable, orange when hurt, red only when the tissue is failing.
        var (fill, text) = integrity >= 66f
            ? (Color.White, HealthMenuStyle.Amber)
            : integrity >= 33f
                ? (Color.FromHex("#e07b26"), HealthMenuStyle.Amber)
                : (HealthMenuStyle.Red, HealthMenuStyle.Red);
        bar.FillTint = fill;
        value.Text = Loc.GetString("advanced-health-vital-percent", ("value", MathF.Round(integrity)));
        value.FontColorOverride = text;
    }

    private (string Text, Color Color) PartSeverity(BodyPartState part)
    {
        if (part.IsDestroyed)
            return (Loc.GetString("advanced-health-window-status-destroyed"), HealthMenuStyle.SeverityCritical);

        // Five stages graded from tissue integrity, matching the doll colouring and the legend.
        return part.GetConditionStage() switch
        {
            0 => (Loc.GetString("advanced-health-severity-normal"), HealthMenuStyle.AmberDim),
            1 => (Loc.GetString("advanced-health-severity-minor"), HealthMenuStyle.SeverityMinor),
            2 => (Loc.GetString("advanced-health-severity-moderate"), HealthMenuStyle.SeverityModerate),
            3 => (Loc.GetString("advanced-health-severity-severe"), HealthMenuStyle.SeveritySevere),
            _ => (Loc.GetString("advanced-health-severity-critical"), HealthMenuStyle.SeverityCritical),
        };
    }

    // Roslyn lowers char + string concatenations through a ReadOnlySpan<char> ctor
    // the sandbox rejects, so stick to pure string operations.
    private static string Capitalize(string text)
        => text.Length == 0 ? text : text.Substring(0, 1).ToUpperInvariant() + text.Substring(1);
}
