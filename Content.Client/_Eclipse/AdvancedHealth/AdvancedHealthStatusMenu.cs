using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client.HealthAnalyzer.UI;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Temperature.Components;
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
    public static readonly Color AmberDim = Color.FromHex("#a97a2f");
    public static readonly Color AmberBorder = Color.FromHex("#b87e1dcc");
    public static readonly Color Red = Color.FromHex("#e0554f");
    public static readonly Color Backdrop = Color.FromHex("#060503f5");
    public static readonly Color PanelBg = Color.FromHex("#0b0805e6");
    public static readonly Color BarBg = Color.FromHex("#171006");
    /// <summary>Line color sampled from separation.png.</summary>
    public static readonly Color SeparatorColor = Color.FromHex("#eea824");

    // Matches AdvancedHealthBodyDollControl.GetColor so the legend is truthful.
    public static readonly Color SeverityHealthy = Color.FromHex("#57574e");
    public static readonly Color SeverityMinor = Color.FromHex("#e0a3a3");
    public static readonly Color SeverityModerate = Color.FromHex("#ef6a6a");
    public static readonly Color SeveritySevere = Color.FromHex("#d81b2a");
    public static readonly Color SeverityCritical = Color.FromHex("#8a0f18");

    private static readonly Dictionary<(int Size, bool Bold), Font> Fonts = new();

    public static Font Mono(int size, bool bold = false)
    {
        if (Fonts.TryGetValue((size, bold), out var font))
            return font;

        var cache = IoCManager.Resolve<IResourceCache>();
        var path = bold ? "/Fonts/RobotoMono/RobotoMono-Bold.ttf" : "/Fonts/RobotoMono/RobotoMono-Regular.ttf";
        font = new VectorFont(cache.GetResource<FontResource>(path), size);
        Fonts[(size, bold)] = font;
        return font;
    }

    public static Texture Tex(string name)
        => IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>($"{TextureDir}/{name}.png").Texture;

    public static StyleBoxFlat PanelBox() => new()
    {
        BackgroundColor = PanelBg,
        BorderColor = AmberBorder,
        BorderThickness = new Thickness(1),
    };

    /// <summary>Thin dashed horizontal section separator.</summary>
    public static Control Separator(int verticalMargin = 5) => new HealthMenuDashedLine
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

    /// <summary>
    /// Texture icon when "name.png" exists in the AdvancedHealth folder, otherwise an amber glyph.
    /// Lets missing icons (pain/shock/shield) light up as soon as the texture is dropped in.
    /// </summary>
    public static Control IconOr(string name, string fallbackGlyph, float size = 20f)
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        if (cache.TryGetResource(new ResPath($"{TextureDir}/{name}.png"), out TextureResource? res))
        {
            return new TextureRect
            {
                Texture = res.Texture,
                Stretch = TextureRect.StretchMode.KeepAspectCentered,
                SetSize = new Vector2(size, size),
                MinSize = new Vector2(size, size),
                VerticalAlignment = Control.VAlignment.Center,
            };
        }

        var label = Text(fallbackGlyph, (int) size - 4, Amber, bold: true);
        label.MinWidth = size;
        label.HorizontalAlignment = Control.HAlignment.Center;
        return label;
    }
}

/// <summary>Dashed line separator (horizontal or vertical) matching the segmented dividers in the design.</summary>
public sealed class HealthMenuDashedLine : Control
{
    private const float Dash = 5f;
    private const float Gap = 4f;

    public bool Vertical;
    public Color LineColor = HealthMenuStyle.SeparatorColor;

    public HealthMenuDashedLine(bool vertical = false)
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

        if (Vertical)
        {
            var x = box.Center.X;
            for (var y = (float) box.Top; y < box.Bottom; y += Dash + Gap)
                handle.DrawLine(new Vector2(x, y), new Vector2(x, Math.Min(y + Dash, box.Bottom)), LineColor);
        }
        else
        {
            var y = box.Center.Y;
            for (var x = (float) box.Left; x < box.Right; x += Dash + Gap)
                handle.DrawLine(new Vector2(x, y), new Vector2(Math.Min(x + Dash, box.Right), y), LineColor);
        }
    }
}

/// <summary>
/// Horizontal bar built from progressbar_empty (frame) and progressbar_full (fill cropped by fraction).
/// With <see cref="Segments"/> &gt; 1 dark tick lines split the bar Casualties-style.
/// </summary>
public sealed class HealthMenuTexturedBar : Control
{
    private readonly Texture _empty = HealthMenuStyle.Tex("progressbar_empty");
    private readonly Texture _full = HealthMenuStyle.Tex("progressbar_full");

    public float Fraction;
    public Color FillTint = Color.White;
    public int Segments;

    public HealthMenuTexturedBar()
    {
        MinHeight = 20;
        HorizontalExpand = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        if (box.Width <= 0 || box.Height <= 0)
            return;

        handle.DrawTextureRect(_empty, box);

        var frac = Math.Clamp(Fraction, 0f, 1f);
        if (frac > 0f)
        {
            var sub = new UIBox2(0, 0, _full.Width * frac, _full.Height);
            var rect = new UIBox2(box.Left, box.Top, box.Left + box.Width * frac, box.Bottom);
            handle.DrawTextureRectRegion(_full, rect, sub, FillTint);
        }

        if (Segments > 1)
        {
            var dark = Color.FromHex("#0b0805");
            for (var i = 1; i < Segments; i++)
            {
                var x = box.Left + box.Width * i / Segments;
                handle.DrawLine(new Vector2(x, box.Top + 3), new Vector2(x, box.Bottom - 3), dark);
            }
        }
    }
}

/// <summary>Vertical gauge filled bottom-up, framed in the same amber style.</summary>
public sealed class HealthMenuVerticalBar : Control
{
    public float Fraction;
    public Color FillColor = HealthMenuStyle.Amber;

    public HealthMenuVerticalBar()
    {
        MinWidth = 18;
        VerticalExpand = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        if (box.Width <= 2 || box.Height <= 2)
            return;

        handle.DrawRect(box, HealthMenuStyle.BarBg);

        var frac = Math.Clamp(Fraction, 0f, 1f);
        if (frac > 0f)
        {
            var h = (box.Height - 4) * frac;
            handle.DrawRect(new UIBox2(box.Left + 2, box.Bottom - 2 - h, box.Right - 2, box.Bottom - 2), FillColor);
        }

        handle.DrawRect(box, HealthMenuStyle.AmberBorder, false);
    }
}

/// <summary>Dashed rectangle border like the ECG frame in the reference design.</summary>
public sealed class HealthMenuDashedBorder : Control
{
    public Color LineColor = HealthMenuStyle.AmberDim;

    private const float Dash = 4f;
    private const float Gap = 3f;

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        if (box.Width <= 2 || box.Height <= 2)
            return;

        for (var x = (float) box.Left; x < box.Right; x += Dash + Gap)
        {
            var end = Math.Min(x + Dash, box.Right);
            handle.DrawLine(new Vector2(x, box.Top), new Vector2(end, box.Top), LineColor);
            handle.DrawLine(new Vector2(x, box.Bottom - 1), new Vector2(end, box.Bottom - 1), LineColor);
        }

        for (var y = (float) box.Top; y < box.Bottom; y += Dash + Gap)
        {
            var end = Math.Min(y + Dash, box.Bottom);
            handle.DrawLine(new Vector2(box.Left, y), new Vector2(box.Left, end), LineColor);
            handle.DrawLine(new Vector2(box.Right - 1, y), new Vector2(box.Right - 1, end), LineColor);
        }
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

    private static readonly Color EcgBg = Color.FromHex("#0c0a05");
    private static readonly Color EcgGrid = Color.FromHex("#241a09");
    private static readonly Color EcgGridStrong = Color.FromHex("#332510");

    private float[] _samples = Array.Empty<float>();
    private int _bufWidth;      // physical width the buffer is sized for
    private float _sweep;       // sweep-head position, in pixels
    private float _phase;       // beat phase, 0..1
    private float _strength = 1f;

    private const float Step = 2f;           // px between stored samples
    private const float SweepSeconds = 3.8f; // time for the head to cross the whole strip
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
            WriteColumns(prev, _bufWidth);
            WriteColumns(0f, _sweep);
        }
        else
        {
            WriteColumns(prev, _sweep);
        }
    }

    private void WriteColumns(float from, float to)
    {
        var n = _samples.Length;
        var i0 = Math.Clamp((int) (from / Step), 0, n - 1);
        var i1 = Math.Clamp((int) (to / Step), 0, n - 1);
        var value = EcgShape(_phase) * _strength;
        for (var i = i0; i <= i1; i++)
            _samples[i] = value;
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
                handle.DrawLine(p, point, color);
            prev = point;
        }

        // Bright sweep head.
        var headX = box.Left + _sweep;
        var headColor = Color.InterpolateBetween(HealthMenuStyle.Red, HealthMenuStyle.AmberBright, _strength);
        handle.DrawLine(new Vector2(headX, box.Top + 4), new Vector2(headX, box.Bottom - 4), headColor);
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

/// <summary>Fixed-size panel with a pixel double border: bright amber contour + dark bottom/right kant.</summary>
public sealed class HealthMenuPixelFramePanel : Control
{
    private static readonly Color DarkKant = Color.FromHex("#1c1206");

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        if (box.Width <= 4 || box.Height <= 4)
            return;

        // Near-black fill.
        handle.DrawRect(box, HealthMenuStyle.PanelBg);

        // Dark inner kant along the right and bottom edges (bevel shadow).
        handle.DrawRect(new UIBox2(box.Left + 2, box.Bottom - 4, box.Right - 2, box.Bottom - 2), DarkKant);
        handle.DrawRect(new UIBox2(box.Right - 4, box.Top + 2, box.Right - 2, box.Bottom - 2), DarkKant);

        // Bright amber outer contour, two pixels thick.
        handle.DrawRect(box, HealthMenuStyle.AmberBright, false);
        handle.DrawRect(new UIBox2(box.Left + 1, box.Top + 1, box.Right - 1, box.Bottom - 1),
            HealthMenuStyle.AmberBright, false);
    }
}

/// <summary>
/// Loadout panel showing the player's hands, belt, back, pockets etc. as item sprites, with the
/// active hand highlighted. Read-only display that refreshes a few times a second.
/// </summary>
public sealed class HealthMenuToolbelt : Control
{
    private readonly IEntityManager _entities;
    private readonly EntityUid _owner;
    private readonly List<Slot> _slots = new();

    private sealed class Slot
    {
        public string? HandId;
        public string? InventorySlot;
        public SpriteView View = default!;
        public PanelContainer Frame = default!;
    }

    public HealthMenuToolbelt(EntityUid owner)
    {
        _entities = IoCManager.Resolve<IEntityManager>();
        _owner = owner;

        var panel = new PanelContainer { PanelOverride = HealthMenuStyle.PanelBox() };
        AddChild(panel);

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(10, 8),
            SeparationOverride = 6,
        };
        panel.AddChild(box);
        box.AddChild(HealthMenuStyle.Text(
            Loc.GetString("advanced-health-menu-equipment").ToUpperInvariant(), 14, HealthMenuStyle.AmberBright, bold: true));

        var grid = new GridContainer { Columns = 4, HSeparationOverride = 6, VSeparationOverride = 6 };
        box.AddChild(grid);

        if (_entities.TryGetComponent(owner, out HandsComponent? hands))
        {
            foreach (var handId in _entities.System<SharedHandsSystem>().EnumerateHands((owner, hands)))
                AddSlot(grid, Loc.GetString("advanced-health-slot-hand"), handId: handId);
        }

        AddSlot(grid, Loc.GetString("advanced-health-slot-belt"), inv: "belt");
        AddSlot(grid, Loc.GetString("advanced-health-slot-back"), inv: "back");
        AddSlot(grid, Loc.GetString("advanced-health-slot-pocket"), inv: "pocket1");
        AddSlot(grid, Loc.GetString("advanced-health-slot-pocket"), inv: "pocket2");
        AddSlot(grid, Loc.GetString("advanced-health-slot-suit"), inv: "suitstorage");
        AddSlot(grid, Loc.GetString("advanced-health-slot-id"), inv: "id");

        Refresh();
    }

    private void AddSlot(GridContainer grid, string tag, string? handId = null, string? inv = null)
    {
        var view = new SpriteView
        {
            OverrideDirection = Direction.South,
            SetSize = new Vector2(40, 40),
            MinSize = new Vector2(40, 40),
            VerticalAlignment = VAlignment.Center,
            HorizontalAlignment = HAlignment.Center,
        };
        var frame = new PanelContainer { PanelOverride = SlotBox(false), ToolTip = tag };
        frame.AddChild(view);
        grid.AddChild(frame);
        _slots.Add(new Slot { HandId = handId, InventorySlot = inv, View = view, Frame = frame });
    }

    private static StyleBoxFlat SlotBox(bool active) => new()
    {
        BackgroundColor = HealthMenuStyle.BarBg,
        BorderColor = active ? HealthMenuStyle.AmberBright : HealthMenuStyle.AmberBorder,
        BorderThickness = new Thickness(active ? 2 : 1),
        ContentMarginLeftOverride = 3,
        ContentMarginRightOverride = 3,
        ContentMarginTopOverride = 3,
        ContentMarginBottomOverride = 3,
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
            slot.Frame.PanelOverride = SlotBox(slot.HandId != null && slot.HandId == activeHand);
        }
    }
}

/// <summary>
/// Fullscreen self-diagnostic menu opened by clicking the HumanHealth HUD alert,
/// styled after the Casualties: Unknown health screen: a system-state panel with textured
/// gauges and a big thermometer, an ECG strip in a dashed frame, the selected-zone panel with
/// segmented condition bars on the left; the zone legend and interactive body doll on the right.
/// Closed with the cross button in the bottom-right corner.
/// </summary>
public sealed class AdvancedHealthStatusWindow : BaseWindow
{
    private readonly IEntityManager _entities;
    private readonly EntityUid _target;
    private readonly EntityUid _equipmentOwner;

    private readonly BoxContainer _leftColumn;
    private readonly AdvancedHealthBodyDollControl _bodyDoll;
    private readonly Control _dollArea;
    private readonly LayoutContainer _overlayLayer;
    private readonly Button _extractButton;
    private BodyPartSlot? _extractSlot;

    private readonly HealthMenuVerticalBar _consciousnessBar;
    private readonly Label _consciousnessLabel;
    private readonly HealthMenuTexturedBar _bloodBar;
    private readonly Label _bloodLabel;
    private readonly BoxContainer _bleedRow;
    private readonly Label _bleedLabel;
    private readonly Label _painValue;
    private readonly Label _shockValue;
    private readonly Label _immunityLabel;
    private readonly Label _respLabel;
    private readonly Label _radLabel;
    private readonly BoxContainer _radRow;
    private readonly Label _tempLabel;
    private readonly Control _tempColumn;
    private readonly HealthMenuTexturedBar _thirstBar;
    private readonly Label _thirstValue;
    private readonly BoxContainer _thirstRow;
    private readonly HealthMenuTexturedBar _hungerBar;
    private readonly Label _hungerValue;
    private readonly BoxContainer _hungerRow;

    private readonly Label _bpLabel;
    private readonly Label _o2Label;
    private readonly HealthMenuEcgControl _ecg;

    private readonly Label _partName;
    private readonly Label _partSeverity;
    private readonly BoxContainer _zoneStats;
    private readonly HealthMenuTexturedBar _zoneHealthBar;
    private readonly Label _zoneHealthValue;
    private readonly HealthMenuTexturedBar _zoneSkinBar;
    private readonly Label _zoneSkinValue;
    private readonly HealthMenuVerticalBar _zoneGauge;
    private readonly Control _woundsSeparator;
    private readonly Label _woundsCaption;
    private readonly BoxContainer _woundsList;
    private readonly BoxContainer _zoneBottom;
    private readonly Label _zoneProtection;
    private readonly Label _zoneBleeding;

    private BodyPartSlot? _selectedSlot;
    private float _refreshAccumulator;
    private bool _closing;
    private bool _bleedCritical;
    private bool _vitalsCritical;
    private float _blinkTimer;
    private bool _blinkOn = true;
    private float _heartbeatTimer;

    private static readonly SoundPathSpecifier HeartbeatSound = new("/Audio/_Lavaland/heartbeat.ogg");

    /// <summary>
    /// <paramref name="target"/> is the patient whose vitals/wounds are shown and treated;
    /// <paramref name="equipmentOwner"/> owns the toolbelt/held items used for treatment (the medic).
    /// For self-diagnosis both are the same entity.
    /// </summary>
    public AdvancedHealthStatusWindow(EntityUid target, EntityUid? equipmentOwner = null)
    {
        _entities = IoCManager.Resolve<IEntityManager>();
        _target = target;
        _equipmentOwner = equipmentOwner ?? target;
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

        var main = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(24, 20),
            SeparationOverride = 14,
        };
        AddChild(main);

        // ---- Left column ---- (width driven by the fixed-size system panel)
        _leftColumn = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            HorizontalExpand = false,
            MinWidth = SystemPanelWidth,
            SeparationOverride = 12,
        };
        main.AddChild(_leftColumn);

        // System state panel: fixed size, absolutely positioned to match the reference pixel layout.
        var systemPanel = new HealthMenuPixelFramePanel
        {
            HorizontalAlignment = HAlignment.Left,
            SetSize = new Vector2(SystemPanelWidth, SystemPanelHeight),
            MinSize = new Vector2(SystemPanelWidth, SystemPanelHeight),
        };
        _leftColumn.AddChild(systemPanel);

        var body = new LayoutContainer();
        systemPanel.AddChild(body);

        // Title.
        var caption = HealthMenuStyle.Text(
            Loc.GetString("advanced-health-window-vitals").ToUpperInvariant(), 17, HealthMenuStyle.AmberBright, bold: true);
        Place(body, caption, 16, 6);

        // Left consciousness (brain integrity) gauge: percentage, tall bar, brain underneath.
        // Percentage, bar and brain icon are all centered on the same vertical axis (x≈32).
        _consciousnessLabel = HealthMenuStyle.Text("--%", 14, HealthMenuStyle.Amber, bold: true);
        Place(body, _consciousnessLabel, 14, 30);

        _consciousnessBar = new HealthMenuVerticalBar
        {
            VerticalExpand = false,
            SetSize = new Vector2(32, 220),
            MinSize = new Vector2(32, 220),
        };
        Place(body, _consciousnessBar, 16, 50);

        Place(body, HealthMenuStyle.Icon("mind", 60, HealthMenuStyle.Amber), 2, 280);

        // Section dividers come from the separation.png texture (dashed vertical edge + horizontal
        // lines baked in), placed over the diagnostic column so the lines match the reference.
        var separation = new TextureRect
        {
            Texture = HealthMenuStyle.Tex("separation"),
            Stretch = TextureRect.StretchMode.Scale,
            SetSize = new Vector2(158, 291),
            MinSize = new Vector2(158, 291),
        };
        Place(body, separation, 60, 46);

        // Fluid block.
        var bloodRow = HRow();
        bloodRow.AddChild(HealthMenuStyle.Icon("blood_full", 30));
        _bloodBar = new HealthMenuTexturedBar { HorizontalExpand = false, MinWidth = 92, MinHeight = 20 };
        bloodRow.AddChild(_bloodBar);
        _bloodLabel = HealthMenuStyle.Text("", 13, HealthMenuStyle.Amber, bold: true);
        bloodRow.AddChild(_bloodLabel);
        Place(body, bloodRow, 62, 50);

        _bleedRow = HRow();
        _bleedRow.AddChild(HealthMenuStyle.Icon("blood_null", 30));
        _bleedLabel = HealthMenuStyle.Text("", 13, HealthMenuStyle.AmberDim);
        _bleedRow.AddChild(_bleedLabel);
        Place(body, _bleedRow, 62, 82);

        // Pain and shock: icon + percentage.
        var painRow = HRow();
        painRow.AddChild(HealthMenuStyle.IconOr("pain", "+", 24));
        _painValue = HealthMenuStyle.Text("", 13, HealthMenuStyle.Amber);
        painRow.AddChild(_painValue);
        Place(body, painRow, 66, 116);

        var shockRow = HRow();
        shockRow.AddChild(HealthMenuStyle.IconOr("shock", "×", 24));
        _shockValue = HealthMenuStyle.Text("", 13, HealthMenuStyle.Amber);
        shockRow.AddChild(_shockValue);
        Place(body, shockRow, 66, 140);

        // Immune defense (right of pain/shock, before the thermometer).
        var immunityRow = HRow();
        immunityRow.AddChild(HealthMenuStyle.IconOr("immunity", "⊕", 26));
        _immunityLabel = HealthMenuStyle.Text("", 15, HealthMenuStyle.Amber, bold: true);
        _immunityLabel.ToolTip = Loc.GetString("advanced-health-vital-immunity");
        immunityRow.AddChild(_immunityLabel);
        Place(body, immunityRow, 150, 122);

        // Lungs and radiation: big icons with the value under each.
        var lungsCol = VCol();
        lungsCol.AddChild(HealthMenuStyle.Icon("lungs", 58));
        _respLabel = HealthMenuStyle.Text("", 13, HealthMenuStyle.Amber);
        _respLabel.HorizontalAlignment = HAlignment.Center;
        lungsCol.AddChild(_respLabel);
        Place(body, lungsCol, 62, 168);

        _radRow = VCol();
        _radRow.AddChild(HealthMenuStyle.Icon("rad", 56));
        _radLabel = HealthMenuStyle.Text("", 13, HealthMenuStyle.Amber);
        _radLabel.HorizontalAlignment = HAlignment.Center;
        _radRow.AddChild(_radLabel);
        Place(body, _radRow, 136, 168);

        // Bottom bars: armour/endurance and food/hunger.
        _thirstRow = HRow();
        _thirstRow.AddChild(HealthMenuStyle.Icon("thirst", 32));
        _thirstBar = new HealthMenuTexturedBar { HorizontalExpand = false, MinWidth = 130, MinHeight = 20 };
        _thirstRow.AddChild(_thirstBar);
        _thirstValue = HealthMenuStyle.Text("", 13, HealthMenuStyle.Amber);
        _thirstRow.AddChild(_thirstValue);
        Place(body, _thirstRow, 62, 256);

        _hungerRow = HRow();
        _hungerRow.AddChild(HealthMenuStyle.Icon("hunger", 32));
        _hungerBar = new HealthMenuTexturedBar { HorizontalExpand = false, MinWidth = 130, MinHeight = 20 };
        _hungerRow.AddChild(_hungerBar);
        _hungerValue = HealthMenuStyle.Text("", 13, HealthMenuStyle.Amber);
        _hungerRow.AddChild(_hungerValue);
        Place(body, _hungerRow, 62, 286);

        // Thermometer (upper-right). Positioned fully to the right of the diagnostic column so its
        // baked callout line and the temperature label never cross the separation texture.
        const float thermoSize = 200f;
        _tempColumn = new LayoutContainer
        {
            SetSize = new Vector2(thermoSize, thermoSize),
            MinSize = new Vector2(thermoSize, thermoSize),
        };
        var thermo = HealthMenuStyle.Icon("temperature", thermoSize);
        LayoutContainer.SetPosition(thermo, new Vector2(0, 0));
        _tempColumn.AddChild(thermo);
        _tempLabel = HealthMenuStyle.Text("", 12, HealthMenuStyle.Amber, bold: true);
        // Left of the tube, on the callout tick (~30% down the texture).
        LayoutContainer.SetPosition(_tempLabel, new Vector2(16, thermoSize * 0.30f));
        _tempColumn.AddChild(_tempLabel);
        Place(body, _tempColumn, 240, 68);

        // ECG panel with a dashed inner frame.
        var ecgPanel = new PanelContainer { PanelOverride = HealthMenuStyle.PanelBox() };
        _leftColumn.AddChild(ecgPanel);

        var ecgFrame = new HealthMenuDashedBorder { Margin = new Thickness(8, 6) };
        ecgPanel.AddChild(ecgFrame);

        var ecgBox = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(10, 8),
            SeparationOverride = 4,
        };
        ecgFrame.AddChild(ecgBox);

        var ecgHeader = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
        };
        ecgBox.AddChild(ecgHeader);
        ecgHeader.AddChild(HealthMenuStyle.Icon("Sprite-0003", 26, HealthMenuStyle.Amber));
        _bpLabel = HealthMenuStyle.Text("", 15, HealthMenuStyle.Amber, bold: true);
        ecgHeader.AddChild(_bpLabel);
        ecgHeader.AddChild(new Control { HorizontalExpand = true });
        _o2Label = HealthMenuStyle.Text("", 15, HealthMenuStyle.Amber, bold: true);
        ecgHeader.AddChild(_o2Label);

        _ecg = new HealthMenuEcgControl();
        ecgBox.AddChild(_ecg);

        // Selected zone panel.
        var zonePanel = new PanelContainer
        {
            PanelOverride = HealthMenuStyle.PanelBox(),
            VerticalExpand = true,
        };
        _leftColumn.AddChild(zonePanel);
        // Toolbelt (hands/belt/back/pockets) fills the space the actions panel used to occupy.
        _leftColumn.AddChild(new HealthMenuToolbelt(_equipmentOwner));
        var zone = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(10),
            VerticalExpand = true,
            SeparationOverride = 4,
        };
        zonePanel.AddChild(zone);

        var zoneHeader = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10,
        };
        zone.AddChild(zoneHeader);
        _partName = HealthMenuStyle.Text(
            Loc.GetString("advanced-health-ui-select-part").ToUpperInvariant(), 18, HealthMenuStyle.AmberBright, bold: true);
        zoneHeader.AddChild(_partName);
        zoneHeader.AddChild(new Control { HorizontalExpand = true });
        _partSeverity = HealthMenuStyle.Text("", 14, HealthMenuStyle.AmberDim, bold: true);
        zoneHeader.AddChild(_partSeverity);

        // Two aggregate condition bars (+ internal health, shield = skin) and a small overall gauge.
        _zoneStats = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Visible = false,
            SeparationOverride = 10,
            Margin = new Thickness(0, 4, 0, 0),
        };
        zone.AddChild(_zoneStats);

        var zoneBars = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };
        _zoneStats.AddChild(zoneBars);

        var zoneHealthRow = StatRow(zoneBars);
        _zoneHealthBar = new HealthMenuTexturedBar { Segments = 8 };
        zoneHealthRow.AddChild(_zoneHealthBar);
        _zoneHealthValue = HealthMenuStyle.Text("", 13, HealthMenuStyle.Amber, bold: true);
        _zoneHealthValue.MinWidth = 46;
        zoneHealthRow.AddChild(_zoneHealthValue);
        zoneHealthRow.AddChild(HealthMenuStyle.IconOr("pain", "+", 24));

        var zoneSkinRow = StatRow(zoneBars);
        _zoneSkinBar = new HealthMenuTexturedBar { Segments = 8 };
        zoneSkinRow.AddChild(_zoneSkinBar);
        _zoneSkinValue = HealthMenuStyle.Text("", 13, HealthMenuStyle.Amber, bold: true);
        _zoneSkinValue.MinWidth = 46;
        zoneSkinRow.AddChild(_zoneSkinValue);
        zoneSkinRow.AddChild(HealthMenuStyle.IconOr("shield", "Ω", 24));

        _zoneGauge = new HealthMenuVerticalBar
        {
            MinWidth = 16,
            MinHeight = 52,
            VerticalExpand = false,
            VerticalAlignment = VAlignment.Center,
        };
        _zoneStats.AddChild(_zoneGauge);

        _woundsSeparator = HealthMenuStyle.Separator(4);
        _woundsSeparator.Visible = false;
        zone.AddChild(_woundsSeparator);
        _woundsCaption = HealthMenuStyle.Text(Loc.GetString("advanced-health-menu-wounds") + ":", 13, HealthMenuStyle.Amber);
        _woundsCaption.Visible = false;
        zone.AddChild(_woundsCaption);

        var woundsScroll = new ScrollContainer { VerticalExpand = true, MinHeight = 56 };
        zone.AddChild(woundsScroll);
        _woundsList = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };
        woundsScroll.AddChild(_woundsList);

        // Bottom cells: protection (shield) and bleeding (blood drop) — icon + value, not text.
        _zoneBottom = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            Margin = new Thickness(0, 6, 0, 0),
            Visible = false,
        };
        zone.AddChild(_zoneBottom);
        _zoneBottom.AddChild(MakeZoneCell(HealthMenuStyle.IconOr("shield", "Ω", 34), out _zoneProtection));
        _zoneBottom.AddChild(MakeZoneCell(HealthMenuStyle.Icon("blood_full", 34), out _zoneBleeding));

        // Vertical divider between the left column and the doll area.
        main.AddChild(new PanelContainer
        {
            MinWidth = 2,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat { BackgroundColor = HealthMenuStyle.SeparatorColor.WithAlpha(0.8f) },
        });

        // ---- Right area: legend + body doll ----
        var right = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 5,
            Margin = new Thickness(10, 0, 0, 0),
        };
        main.AddChild(right);

        right.AddChild(HealthMenuStyle.Text(
            Loc.GetString("advanced-health-ui-select-part").ToUpperInvariant(), 16, HealthMenuStyle.AmberBright, bold: true));

        var legendCaption = HealthMenuStyle.Text(Loc.GetString("advanced-health-ui-tissue-header"), 13, HealthMenuStyle.AmberDim);
        legendCaption.Margin = new Thickness(0, 4, 0, 0);
        right.AddChild(legendCaption);

        AddLegendRow(right, HealthMenuStyle.SeverityHealthy, Loc.GetString("advanced-health-legend-healthy"));
        AddLegendRow(right, HealthMenuStyle.SeverityMinor, Loc.GetString("advanced-health-legend-minor"));
        AddLegendRow(right, HealthMenuStyle.SeverityModerate, Loc.GetString("advanced-health-legend-moderate"));
        AddLegendRow(right, HealthMenuStyle.SeveritySevere, Loc.GetString("advanced-health-legend-severe"));
        AddLegendRow(right, HealthMenuStyle.SeverityCritical, Loc.GetString("advanced-health-legend-critical"));

        _dollArea = new Control
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        right.AddChild(_dollArea);
        _bodyDoll = new AdvancedHealthBodyDollControl
        {
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
            SetSize = new Vector2(340, 340),
            MinSize = new Vector2(260, 260),
            HoverSelect = true,
        };
        _bodyDoll.OnPartSelected += OnPartSelected;
        _bodyDoll.OnPartActivated += OnPartActivated;
        _dollArea.AddChild(_bodyDoll);

        // Exit cross in a framed box, top-right corner.
        var closeButton = new TextureButton
        {
            StyleClasses = { DefaultWindow.StyleClassWindowCloseButton },
            Scale = new Vector2(1.6f, 1.6f),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
        };
        closeButton.OnPressed += _ => Close();

        var closeBox = new PanelContainer
        {
            HorizontalAlignment = HAlignment.Right,
            VerticalAlignment = VAlignment.Top,
            Margin = new Thickness(0, 18, 22, 0),
            MinSize = new Vector2(36, 36),
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = HealthMenuStyle.PanelBg,
                BorderColor = HealthMenuStyle.SeparatorColor,
                BorderThickness = new Thickness(2),
                ContentMarginLeftOverride = 5,
                ContentMarginRightOverride = 5,
                ContentMarginTopOverride = 5,
                ContentMarginBottomOverride = 5,
            },
        };
        closeBox.AddChild(closeButton);
        AddChild(closeBox);

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

        Refresh();
    }

    protected override void Opened()
    {
        base.Opened();
        _closing = false;
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
                base.Close();
        });
    }

    private static BoxContainer StatRow(BoxContainer parent)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 8,
        };
        parent.AddChild(row);
        return row;
    }

    // --- Absolute layout helpers for the fixed-size system panel ---

    private const float SystemPanelWidth = 392f;
    private const float SystemPanelHeight = 344f;

    private static void Place(LayoutContainer body, Control child, float x, float y)
    {
        body.AddChild(child);
        LayoutContainer.SetPosition(child, new Vector2(x, y));
    }

    /// <summary>Compact horizontal row (icon + value) with tight spacing.</summary>
    private static BoxContainer HRow() => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Horizontal,
        SeparationOverride = 6,
    };

    /// <summary>Icon-over-value column used by the lungs/radiation block.</summary>
    private static BoxContainer VCol() => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        SeparationOverride = 2,
    };

    private static void AddLegendRow(BoxContainer parent, Color color, string text)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
        };
        parent.AddChild(row);
        row.AddChild(new PanelContainer
        {
            SetSize = new Vector2(14, 14),
            MinSize = new Vector2(14, 14),
            VerticalAlignment = VAlignment.Center,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = color,
                BorderColor = Color.FromHex("#000000aa"),
                BorderThickness = new Thickness(1),
            },
        });
        row.AddChild(HealthMenuStyle.Text(text, 13, HealthMenuStyle.Amber));
    }

    private void OnPartSelected(BodyPartSlot slot)
    {
        _selectedSlot = slot;
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
    private static Control MakeZoneCell(Control icon, out Label value)
    {
        var frame = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = HealthMenuStyle.BarBg,
                BorderColor = HealthMenuStyle.AmberBorder,
                BorderThickness = new Thickness(1),
            },
        };
        var col = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
            Margin = new Thickness(4, 8),
        };
        frame.AddChild(col);
        icon.HorizontalAlignment = HAlignment.Center;
        col.AddChild(icon);
        value = HealthMenuStyle.Text("", 16, HealthMenuStyle.Amber, bold: true);
        value.HorizontalAlignment = HAlignment.Center;
        col.AddChild(value);
        return frame;
    }

    /// <summary>Sums the protection rating of the armour/clothing covering a body part.</summary>
    private float ComputeProtection(BodyPartSlot slot)
    {
        var inv = _entities.System<InventorySystem>();
        var total = 0f;
        foreach (var slotName in ArmorSlotsFor(slot))
        {
            if (inv.TryGetSlotEntity(_target, slotName, out var item) &&
                item is { } e &&
                _entities.TryGetComponent<BodyArmorRatingComponent>(e, out var armor))
            {
                total += armor.Protection;
            }
        }

        return Math.Clamp(total, 0f, 100f);
    }

    private static string[] ArmorSlotsFor(BodyPartSlot slot) => slot switch
    {
        BodyPartSlot.Head or BodyPartSlot.Neck => new[] { "head", "mask" },
        BodyPartSlot.LeftHand or BodyPartSlot.RightHand => new[] { "gloves", "outerClothing" },
        BodyPartSlot.LeftFoot or BodyPartSlot.RightFoot => new[] { "shoes", "outerClothing" },
        _ => new[] { "outerClothing", "jumpsuit" },
    };

    private static bool PartNeedsTreatment(BodyPartState part, AdvancedTreatmentType treatment)
    {
        var bleeding = part.Wounds.Any(w => w.ExternalBleedingRate + w.InternalBleedingRate > 0.01f);
        var openWound = part.Wounds.Any(w => !w.HasForeignBody && !w.IsSutured);
        var foreign = part.ForeignBodyCount > 0 || part.Wounds.Any(w => w.HasForeignBody);
        var fractured = part.Wounds.Any(w => w.Type == WoundType.Fracture);

        return treatment switch
        {
            AdvancedTreatmentType.Bandage or AdvancedTreatmentType.PressureBandage => bleeding,
            AdvancedTreatmentType.Hemostatic => bleeding,
            AdvancedTreatmentType.Tourniquet => part.Slot.IsLimb() && bleeding && !part.HasTourniquet,
            AdvancedTreatmentType.Suture => openWound,
            AdvancedTreatmentType.Splint => fractured && !part.IsSplinted,
            AdvancedTreatmentType.ForeignBodyRemoval => foreign,
            _ => false,
        };
    }

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

        // Heartbeat is audible only here in the status menu, pulsing at the real heart rate.
        if (_entities.TryGetComponent<AdvancedHealthComponent>(_target, out var hb) &&
            !hb.IsHeartStopped && hb.HeartRate > 20f)
        {
            _heartbeatTimer += args.DeltaSeconds;
            var interval = 60f / hb.HeartRate;
            if (_heartbeatTimer >= interval)
            {
                _heartbeatTimer = 0f;
                _entities.System<AudioSystem>().PlayGlobal(
                    HeartbeatSound, _target, AudioParams.Default.WithVolume(-7f));
            }
        }
        else
        {
            _heartbeatTimer = 0f;
        }

        UpdateLayoutSizes();
        UpdateExtractButton();
        _refreshAccumulator += args.DeltaSeconds;
        if (_refreshAccumulator < 0.5f)
            return;
        _refreshAccumulator = 0;
        Refresh();
    }

    /// <summary>Keeps the body doll square and as large as the available area allows.</summary>
    private void UpdateLayoutSizes()
    {
        var avail = _dollArea.Size;
        if (avail.X < 1f || avail.Y < 1f)
            return;

        var side = Math.Clamp(MathF.Min(avail.X, avail.Y) - 8f, 220f, 2000f);
        if (MathF.Abs(_bodyDoll.SetSize.X - side) > 1f)
        {
            _bodyDoll.SetSize = new Vector2(side, side);
            _bodyDoll.MinSize = new Vector2(side, side);
        }
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
        _bodyDoll.SelectedSlot = _selectedSlot;

        RefreshVitals(health);
        RefreshSelectedZone(health);
    }

    private void RefreshVitals(AdvancedHealthComponent health)
    {
        var consciousness = AdvancedHealthHudData.ConsciousnessPercentage(health);
        _consciousnessLabel.Text = $"{consciousness}%";
        _consciousnessBar.Fraction = consciousness / 100f;
        _consciousnessBar.FillColor = consciousness > 40 ? HealthMenuStyle.Amber : HealthMenuStyle.Red;

        var bloodRatio = health.HasBlood ? health.BloodVolume / Math.Max(1f, health.MaxBloodVolume) : 1f;
        _bloodBar.Fraction = bloodRatio;
        _bloodBar.FillTint = bloodRatio > 0.5f ? Color.White : HealthMenuStyle.Red;
        _bloodLabel.Text = Loc.GetString("advanced-health-menu-liters",
            ("value", (health.BloodVolume / 1000f).ToString("0.0", CultureInfo.InvariantCulture)));

        // Body fluid type and oxygen-carrying fraction, on hover.
        var groupText = BloodCompatibility.Format(health.BodyFluid, health.BloodType);
        var carry = (int) MathF.Round(Math.Clamp(health.OxygenCarryingCapacity, 0f, 1f) * 100f);
        _bloodLabel.ToolTip = _bloodBar.ToolTip = Loc.GetString("advanced-health-blood-group-tooltip",
            ("group", groupText), ("carry", carry));

        // Blood loss: hidden when none, blinking red at critical rate (>= 0.3 L/m).
        var bleedPerMinute = health.BodyParts.Values
            .SelectMany(part => part.Wounds)
            .Sum(w => w.ExternalBleedingRate + w.InternalBleedingRate) * 60f / 1000f;
        // The empty-drop icon always stays; only the text is hidden when there is no bleeding.
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

        _painValue.Text = Loc.GetString("advanced-health-vital-percent", ("value", MathF.Round(health.Pain)));
        _painValue.FontColorOverride = health.Pain > 50f ? HealthMenuStyle.Red : HealthMenuStyle.Amber;

        _shockValue.Text = Loc.GetString("advanced-health-vital-percent", ("value", MathF.Round(health.Shock)));
        _shockValue.FontColorOverride = health.Shock > 50f ? HealthMenuStyle.Red : HealthMenuStyle.Amber;

        var immune = (int) MathF.Round(Math.Clamp(health.ImmuneDefense, 0f, 100f));
        _immunityLabel.Text = Loc.GetString("advanced-health-vital-percent", ("value", immune));
        _immunityLabel.FontColorOverride = immune < 40 ? HealthMenuStyle.Red
            : immune < 70 ? Color.FromHex("#e07b26")
            : HealthMenuStyle.Amber;

        var respiration = health.IsHeartStopped
            ? 0
            : (int) MathF.Round(14f + (100f - health.Oxygenation) * 0.25f + health.Pain * 0.06f);
        _respLabel.Text = Loc.GetString("advanced-health-menu-per-minute", ("value", respiration));

        // Radiation is always shown (0.0 when none), matching the reference layout.
        var radValue = 0f;
        if (_entities.HasComponent<DamageableComponent>(_target) &&
            _entities.System<DamageableSystem>().GetAllDamage(_target).DamageDict.TryGetValue("Radiation", out var rad))
        {
            radValue = rad.Float();
        }
        _radLabel.Text = Loc.GetString("advanced-health-menu-rad",
            ("value", radValue.ToString("0.0", CultureInfo.InvariantCulture)));

        if (_entities.TryGetComponent<TemperatureComponent>(_target, out var temp))
        {
            _tempColumn.Visible = true;
            _tempLabel.Text = Loc.GetString("advanced-health-vital-temperature-value",
                ("value", (temp.CurrentTemperature - 273.15f).ToString("0.0", CultureInfo.InvariantCulture)));
        }
        else
        {
            _tempColumn.Visible = false;
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
            _thirstValue.Text = MathF.Round(frac * 100f).ToString("0");
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
            _hungerValue.Text = MathF.Round(frac * 100f).ToString("0");
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
        // Systolic/diastolic split from the mean arterial pressure (MAP ≈ dia + pulsePressure/3).
        var map = health.MeanArterialPressure;
        var pulsePressure = Math.Clamp(map * 0.45f, 0f, 60f);
        var sys = health.IsHeartStopped ? 0 : (int) MathF.Round(map + pulsePressure * 2f / 3f);
        var dia = health.IsHeartStopped ? 0 : (int) MathF.Round(map - pulsePressure / 3f);
        if (health.IsHeartStopped)
            pulse = 0;

        _bpLabel.Text = Loc.GetString("advanced-health-menu-bp", ("sys", sys), ("dia", dia), ("pulse", pulse));

        // Heart rate / pressure out of the normal window blink red; a stopped heart stays red.
        var pulseOut = pulse < 50 || pulse > 110;
        var pressureOut = sys < 90 || sys > 160 || dia < 55 || dia > 95;
        _vitalsCritical = !health.IsHeartStopped && (pulseOut || pressureOut || health.Perfusion < 0.6f);
        if (health.IsHeartStopped)
            _bpLabel.FontColorOverride = HealthMenuStyle.Red;
        else if (!_vitalsCritical)
            _bpLabel.FontColorOverride = HealthMenuStyle.Amber;
        // (critical case is animated in FrameUpdate)

        _o2Label.Text = Loc.GetString("advanced-health-menu-o2", ("value", MathF.Round(health.Oxygenation)));
        _o2Label.FontColorOverride = health.Oxygenation < health.CriticalOxygenation
            ? HealthMenuStyle.Red
            : HealthMenuStyle.Amber;

        _ecg.Beating = !health.IsHeartStopped;
        _ecg.Rate = health.HeartRate;
    }

    private void RefreshSelectedZone(AdvancedHealthComponent health)
    {
        _woundsList.RemoveAllChildren();

        if (_selectedSlot is not { } slot || !health.BodyParts.TryGetValue(slot, out var part))
        {
            _partName.Text = Loc.GetString("advanced-health-ui-select-part").ToUpperInvariant();
            _partSeverity.Text = "";
            _zoneStats.Visible = false;
            _zoneBottom.Visible = false;
            _woundsSeparator.Visible = false;
            _woundsCaption.Visible = false;
            return;
        }

        _partName.Text = Loc.GetString($"advanced-health-part-{slot.ToString().ToLowerInvariant()}").ToUpperInvariant();

        var (severityText, severityColor) = PartSeverity(part);
        _partSeverity.Text = Capitalize(severityText);
        _partSeverity.FontColorOverride = severityColor;

        RefreshZoneBars(health, slot, part);
        _zoneStats.Visible = true;

        // Bottom cells: protection (shield icon) and bleeding (blood-drop icon) as icon + value.
        _zoneBottom.Visible = true;

        var protection = (int) MathF.Round(ComputeProtection(slot));
        _zoneProtection.Text = Loc.GetString("advanced-health-vital-percent", ("value", protection));

        var bleed = part.Wounds.Sum(w => w.ExternalBleedingRate + w.InternalBleedingRate) * 60f / 1000f;
        _zoneBleeding.Text = Loc.GetString("advanced-health-menu-liters-per-minute",
            ("value", bleed.ToString("0.00", CultureInfo.InvariantCulture)));
        _zoneBleeding.FontColorOverride = bleed > 0.005f ? HealthMenuStyle.Red : HealthMenuStyle.Amber;

        _woundsSeparator.Visible = true;
        _woundsCaption.Visible = true;

        var conditions = new List<(string text, Color color)>();

        if (part.IsBleeding || part.Wounds.Any(w => w.ExternalBleedingRate + w.InternalBleedingRate > 0.01f))
            conditions.Add((Loc.GetString("advanced-health-cond-bleeding"), HealthMenuStyle.Red));
        if (part.ForeignBodyCount > 0)
            conditions.Add((Loc.GetString("advanced-health-cond-foreign-count",
                ("count", part.ForeignBodyCount)), Color.FromHex("#e0a24f")));
        else if (part.Wounds.Any(w => w.HasForeignBody))
            conditions.Add((Loc.GetString("advanced-health-cond-foreign"), Color.FromHex("#e0a24f")));
        if (part.Wounds.Any(w => w.Type == WoundType.Fracture))
            conditions.Add((Loc.GetString("advanced-health-cond-fracture"), Color.FromHex("#6fd0e0")));

        foreach (var group in part.Wounds
                     .Where(w => w.Type != WoundType.Fracture)
                     .GroupBy(w => w.Type))
        {
            var name = Loc.GetString($"advanced-health-wound-{group.Key.ToString().ToLowerInvariant()}");
            var count = group.Sum(w => w.StackCount);
            conditions.Add((count > 1 ? $"{name} ×{count}" : name, HealthMenuStyle.Red));
        }

        if (conditions.Count == 0)
        {
            _woundsList.AddChild(HealthMenuStyle.Text(
                Loc.GetString("advanced-health-ui-no-wounds"), 12, HealthMenuStyle.AmberDim));
        }
        else
        {
            foreach (var (text, color) in conditions)
            {
                var label = HealthMenuStyle.Text(text, 13, color);
                label.Margin = new Thickness(0, 1);
                _woundsList.AddChild(label);
            }
        }

        // Applied treatments combined into a single muted line to avoid clutter.
        var treatments = new List<string>();
        if (part.IsBandaged || part.Wounds.Any(w => w.IsBandaged))
            treatments.Add(Loc.GetString("advanced-health-window-status-bandaged"));
        if (part.Wounds.Any(w => w.IsSutured))
            treatments.Add(Loc.GetString("advanced-health-wound-flag-sutured"));
        if (part.IsSplinted)
            treatments.Add(Loc.GetString("advanced-health-window-status-splinted"));
        if (part.HasTourniquet)
            treatments.Add(Loc.GetString("advanced-health-window-status-tourniquet"));
        if (treatments.Count > 0)
        {
            var label = HealthMenuStyle.Text(
                Loc.GetString("advanced-health-cond-treated", ("list", string.Join(", ", treatments))),
                11, HealthMenuStyle.AmberDim);
            label.Margin = new Thickness(0, 3, 0, 0);
            _woundsList.AddChild(label);
        }
    }

    /// <summary>Two aggregate bars: internal condition (muscle/bone/vessels/nerves/organs) and skin cover.</summary>
    private void RefreshZoneBars(AdvancedHealthComponent health, BodyPartSlot slot, BodyPartState part)
    {
        // Top bar = muscle integrity; bottom bar = skin cover (infection risk surface).
        // Shown as a percentage of the part's per-race maximum, so a healthy part reads 100%.
        var muscle = part.MuscleFraction * 100f;
        var skin = part.SkinFraction * 100f;

        SetZoneBar(_zoneHealthBar, _zoneHealthValue, muscle);
        SetZoneBar(_zoneSkinBar, _zoneSkinValue, skin);

        _zoneHealthBar.ToolTip = $"{Loc.GetString("advanced-health-tissue-muscle")} {MathF.Round(muscle):0}";
        _zoneSkinBar.ToolTip = $"{Loc.GetString("advanced-health-tissue-skin")} {MathF.Round(skin):0}";

        var overall = (muscle + skin) / 200f;
        _zoneGauge.Fraction = overall;
        _zoneGauge.FillColor = overall > 0.4f ? HealthMenuStyle.Amber : HealthMenuStyle.Red;
    }

    private static void SetZoneBar(HealthMenuTexturedBar bar, Label value, float integrity)
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
