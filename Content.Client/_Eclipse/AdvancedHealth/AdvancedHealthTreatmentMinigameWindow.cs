using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Input;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>
/// Casualties: Unknown-style treatment minigames opened from the health screen.
/// </summary>
public sealed class AdvancedHealthTreatmentMinigameWindow : DefaultWindow
{
    private readonly AdvancedHealthClientSystem _client;
    private readonly EntityUid _target;
    private readonly BodyPartSlot _slot;
    private readonly AdvancedTreatmentType _treatment;
    private readonly EntityUid? _tool;
    private readonly float _pain01;
    private readonly bool _isBandage;
    private readonly TreatmentMinigameControl _game;
    private readonly Label _hint;
    private readonly Label _status;
    private bool _bandageApplied;
    private float _time;

    public AdvancedHealthTreatmentMinigameWindow(
        EntityUid target,
        BodyPartSlot slot,
        AdvancedTreatmentType treatment,
        EntityUid? tool,
        float painPercent)
    {
        var entities = IoCManager.Resolve<IEntityManager>();
        _client = entities.System<AdvancedHealthClientSystem>();
        _target = target;
        _slot = slot;
        _treatment = treatment;
        _tool = tool;
        _pain01 = Math.Clamp(painPercent / 100f, 0f, 1f);
        _isBandage = treatment is AdvancedTreatmentType.Bandage or AdvancedTreatmentType.PressureBandage;

        // Bandaging is a durability pool: each 1% wound on stops 0.01 L/min (0.016 for pressure). The
        // player can wind on up to whatever the roll and the wound's bleeding allow.
        var maxSegments = 0;
        var startDurability = 0f;
        var startBleedLm = 0f;
        var perSegLm = 0.01f;
        if (_isBandage)
        {
            perSegLm = treatment == AdvancedTreatmentType.PressureBandage ? 0.016f : 0.01f;
            if (tool is { } t && entities.TryGetComponent<AdvancedBandageRollComponent>(t, out var roll))
                startDurability = roll.Durability;
            if (entities.TryGetComponent<AdvancedHealthComponent>(target, out var health) &&
                health.BodyParts.TryGetValue(slot, out var part))
                startBleedLm = part.Wounds.Sum(w => w.ExternalBleedingRate) * 60f / 1000f;
            var bleedSegs = (int) MathF.Ceiling(startBleedLm / perSegLm - 0.0001f);
            maxSegments = Math.Max(0, Math.Min((int) MathF.Floor(startDurability), bleedSegs));
        }

        Title = Loc.GetString($"advanced-health-minigame-title-{_treatment.ToString().ToLowerInvariant()}");
        Resizable = false;
        MinSize = SetSize = new Vector2(560, 620);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(16),
            SeparationOverride = 10,
        };
        Contents.AddChild(root);

        _hint = new Label
        {
            Text = Loc.GetString(GetHintLoc()),
            StyleClasses = { StyleClass.LabelSubText },
            HorizontalAlignment = HAlignment.Center,
        };
        root.AddChild(_hint);

        _game = new TreatmentMinigameControl(_treatment, _pain01, tool != null,
            maxSegments, startDurability, startBleedLm, perSegLm)
        {
            MinSize = new Vector2(500, 460),
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        root.AddChild(_game);

        _status = new Label
        {
            HorizontalAlignment = HAlignment.Center,
        };
        root.AddChild(_status);

        var cancel = new Button { Text = Loc.GetString("advanced-health-minigame-cancel") };
        cancel.OnPressed += _ => Close();
        root.AddChild(cancel);

        _game.OnFinished += result =>
        {
            if (_isBandage)
            {
                // Whatever was wound on gets applied; closing early keeps the progress.
                ApplyBandageProgress();
                _status.Text = Loc.GetString("advanced-health-minigame-success");
                _status.FontColorOverride = Color.FromHex("#6fcf6f");
                Timer.Spawn(TimeSpan.FromMilliseconds(450), Close);
                return;
            }

            if (result.Success)
            {
                _client.CompleteTreatment(_target, _slot, _treatment, result.Quality, _tool);
                _status.Text = Loc.GetString("advanced-health-minigame-success");
                _status.FontColorOverride = Color.FromHex("#6fcf6f");
                Timer.Spawn(TimeSpan.FromMilliseconds(450), Close);
            }
            else
            {
                _status.Text = Loc.GetString(result.ReasonLoc);
                _status.FontColorOverride = Color.FromHex("#e0554f");
            }
        };
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _time += args.DeltaSeconds;
        _game.UpdatePainShake(_time);

        if (_game.IsRunning)
            _status.Text = _game.StatusText;
    }

    // Closing the window mid-wrap still applies whatever was wound on — using the whole roll is optional.
    public override void Close()
    {
        if (_isBandage)
            ApplyBandageProgress();
        base.Close();
    }

    private void ApplyBandageProgress()
    {
        if (_bandageApplied)
            return;
        _bandageApplied = true;

        var segments = _game.CompletedSegments;
        if (segments > 0)
            _client.CompleteTreatment(_target, _slot, _treatment, 1f, _tool, segments);
    }

    private string GetHintLoc() => _treatment switch
    {
        AdvancedTreatmentType.ForeignBodyRemoval => _tool != null
            ? "advanced-health-minigame-hint-extraction-tool"
            : "advanced-health-minigame-hint-extraction-hand",
        AdvancedTreatmentType.Suture => "advanced-health-minigame-hint-suture",
        AdvancedTreatmentType.Splint => "advanced-health-minigame-hint-splint",
        AdvancedTreatmentType.Tourniquet => "advanced-health-minigame-hint-tourniquet",
        AdvancedTreatmentType.Bandage or AdvancedTreatmentType.PressureBandage => "advanced-health-minigame-hint-wrap",
        _ => "advanced-health-minigame-hint-steady",
    };
}

sealed class TreatmentMinigameControl : Control
{
    public event Action<MinigameResult>? OnFinished;
    public bool IsRunning { get; private set; } = true;
    public string StatusText { get; private set; } = "";

    // Amber terminal palette, matching the health status menu.
    private static readonly Color Bg = Color.FromHex("#120d05");
    private static readonly Color Frame = Color.FromHex("#e0a030");
    private static readonly Color FrameDim = Color.FromHex("#5a4415");
    private static readonly Color Wound = Color.FromHex("#3a1512");
    private static readonly Color WoundEdge = Color.FromHex("#803026");
    private static readonly Color Amber = Color.FromHex("#f0b840");
    private static readonly Color Steel = Color.FromHex("#c8ccd0");
    private static readonly Color SteelDim = Color.FromHex("#6a6e72");
    private static readonly Color Good = Color.FromHex("#6fcf6f");

    private readonly AdvancedTreatmentType _mode;
    private readonly float _pain01;
    private readonly bool _hasTool;
    private float _time;
    private float _shakeX;
    private float _shakeY;
    private Vector2 _mousePos;

    // Extraction — four shards pulled out one at a time.
    private const int ShardCount = 4;
    private int _shardIndex;
    private float _shardProgress;
    private bool _pulling;
    private Vector2 _lastPull;
    private const float PullDistance = 150f;
    private const float LateralSlipLimit = 55f;

    // Steady hand / tourniquet
    private float _steadyTime;
    private bool _holding;

    // Suture
    private int _sutureIndex;
    private readonly Vector2[] _suturePoints =
    [
        new(120, 300), new(240, 240), new(360, 300), new(240, 360),
    ];

    // Splint
    private float _boneOffset = 40f;
    private bool _draggingBone;

    // Bandage wrap (orbit the wound). Each SegmentAngle wound on = 1% durability = one segment.
    private readonly int _maxSegments;
    private readonly float _startDurability;
    private readonly float _startBleedLm;
    private readonly float _perSegLm;
    private float _wrapAngleAccum;
    private float _lastWrapAngle;
    private bool _wrapTracking;
    private const int SegmentsPerRevolution = 8;
    private static readonly float SegmentAngle = MathF.PI * 2f / SegmentsPerRevolution;

    private const float WrapInnerRadius = 62f;
    private const float WrapOuterRadius = 132f;
    private const float SteadyRequired = 2.4f;
    private const float SteadyRadius = 44f;
    private const float TourniquetRequired = 2f;
    private const float TourniquetRadius = 54f;
    private const float SplintTarget = 0f;
    private const float SplintTolerance = 10f;

    public int CompletedSegments => _maxSegments <= 0
        ? 0
        : Math.Min(_maxSegments, (int) (_wrapAngleAccum / SegmentAngle));

    public TreatmentMinigameControl(AdvancedTreatmentType mode, float pain01, bool hasTool,
        int maxSegments = 0, float startDurability = 0f, float startBleedLm = 0f, float perSegLm = 0.01f)
    {
        _mode = mode;
        _pain01 = pain01;
        _hasTool = hasTool;
        _maxSegments = maxSegments;
        _startDurability = startDurability;
        _startBleedLm = startBleedLm;
        _perSegLm = perSegLm;
        MouseFilter = MouseFilterMode.Stop;
    }

    public void UpdatePainShake(float time)
    {
        _time = time;
        // Forceps steady the hand; bare-handed extraction trembles harder with pain.
        var amp = _pain01 * (_mode == AdvancedTreatmentType.ForeignBodyRemoval && !_hasTool ? 11f : 7f);
        _shakeX = MathF.Sin(time * 17f) * amp + MathF.Sin(time * 29f) * amp * 0.45f;
        _shakeY = MathF.Sin(time * 21f) * amp * 0.35f;
    }

    /// <summary>Base (unshaken) position of shard <paramref name="i"/> in the wound, in control space.</summary>
    private Vector2 ShardPos(int i)
    {
        var w = Size.X;
        var h = Size.Y;
        var woundY = h * 0.60f;
        var startX = w * 0.26f;
        var spacing = ShardCount > 1 ? w * 0.48f / (ShardCount - 1) : 0f;
        var jitterY = (i % 4) switch { 0 => 0f, 1 => -16f, 2 => 12f, _ => -8f };
        return new Vector2(startX + spacing * i, woundY + jitterY);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (!IsRunning || args.Function != EngineKeyFunctions.UIClick)
            return;

        var pos = args.RelativePosition;
        _mousePos = pos;

        switch (_mode)
        {
            case AdvancedTreatmentType.ForeignBodyRemoval:
                // Latch onto the current shard from wherever the cursor is; re-grabbing after a
                // release simply continues from the progress already made.
                if (_shardIndex < ShardCount)
                {
                    _pulling = true;
                    _lastPull = pos;
                }
                break;
            case AdvancedTreatmentType.Suture:
                TrySutureClick(pos);
                break;
            case AdvancedTreatmentType.Splint:
                if (Math.Abs(pos.Y - (Size.Y * 0.55f)) < 34f && Math.Abs(pos.X - (Size.X * 0.5f + _boneOffset)) < 48f)
                    _draggingBone = true;
                break;
            case AdvancedTreatmentType.Bandage:
            case AdvancedTreatmentType.PressureBandage:
                BeginWrapTracking(pos);
                break;
            default:
                _holding = true;
                break;
        }
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        if (_mode == AdvancedTreatmentType.ForeignBodyRemoval)
            _pulling = false; // progress on the current shard is kept for the next grab
        if (_mode == AdvancedTreatmentType.Splint)
        {
            _draggingBone = false;
            if (Math.Abs(_boneOffset - SplintTarget) <= SplintTolerance)
                Finish(true, quality: 1f - Math.Abs(_boneOffset - SplintTarget) / SplintTolerance * 0.4f);
        }
        if (_mode is AdvancedTreatmentType.Bandage or AdvancedTreatmentType.PressureBandage)
            _wrapTracking = false; // wound-on angle is kept, so you can pause and continue
        _holding = false;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _mousePos = args.RelativePosition;
        if (_draggingBone)
            _boneOffset = Math.Clamp(args.RelativePosition.X - Size.X * 0.5f, -90f, 90f);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!IsRunning)
            return;

        var dt = args.DeltaSeconds;
        var pos = _mousePos + new Vector2(_shakeX, _shakeY);

        switch (_mode)
        {
            case AdvancedTreatmentType.ForeignBodyRemoval:
                UpdateExtraction(pos);
                break;
            case AdvancedTreatmentType.Splint:
                UpdateSplint();
                break;
            case AdvancedTreatmentType.Bandage:
            case AdvancedTreatmentType.PressureBandage:
                UpdateBandageWrap(pos);
                break;
            case AdvancedTreatmentType.Suture:
                StatusText = Loc.GetString("advanced-health-minigame-status-suture",
                    ("step", _sutureIndex + 1), ("total", _suturePoints.Length));
                break;
            case AdvancedTreatmentType.Tourniquet:
                UpdateHoldZone(pos, dt, TourniquetRequired, TourniquetRadius, "advanced-health-minigame-status-tourniquet");
                break;
            default:
                UpdateHoldZone(pos, dt, SteadyRequired, SteadyRadius, "advanced-health-minigame-status-steady");
                break;
        }
    }

    private void UpdateExtraction(Vector2 pos)
    {
        if (_pulling && _shardIndex < ShardCount)
        {
            // Progress accrues from upward drag and accumulates across separate grabs.
            var dy = _lastPull.Y - pos.Y;
            if (dy > 0)
                _shardProgress += dy / PullDistance;

            // Straying sideways lets the shard slip back a little — no hard failure, just recover.
            var lateral = MathF.Abs(pos.X - ShardPos(_shardIndex).X);
            if (lateral > LateralSlipLimit)
                _shardProgress -= (lateral - LateralSlipLimit) / LateralSlipLimit * 0.012f;

            _shardProgress = Math.Clamp(_shardProgress, 0f, 1f);
            _lastPull = pos;

            if (_shardProgress >= 1f)
            {
                _shardIndex++;
                _shardProgress = 0f;
                _pulling = false;

                if (_shardIndex >= ShardCount)
                {
                    Finish(true, quality: 0.9f);
                    return;
                }
            }
        }

        StatusText = Loc.GetString("advanced-health-minigame-status-extraction",
            ("shard", Math.Min(_shardIndex + 1, ShardCount)),
            ("total", ShardCount),
            ("percent", (int) (_shardProgress * 100f)));
    }

    private void UpdateHoldZone(Vector2 pos, float dt, float required, float radius, string statusLoc)
    {
        var center = Size / 2f;
        var inside = (pos - center).Length() <= radius;
        if (_holding && inside)
            _steadyTime += dt;

        StatusText = Loc.GetString(statusLoc, ("seconds", MathF.Max(0f, required - _steadyTime).ToString("0.0")));

        if (_steadyTime >= required)
            Finish(true, quality: Math.Clamp(0.55f + _steadyTime / (required * 2f), 0.55f, 1f));
    }

    private void BeginWrapTracking(Vector2 pos)
    {
        var center = Size / 2f;
        var dist = (pos - center).Length();
        if (dist < WrapInnerRadius || dist > WrapOuterRadius)
            return;
        _wrapTracking = true;
        _lastWrapAngle = MathF.Atan2(pos.Y - center.Y, pos.X - center.X);
    }

    private void UpdateBandageWrap(Vector2 pos)
    {
        var center = Size / 2f + new Vector2(_shakeX, _shakeY);
        var dist = (pos - center).Length();

        var done = CompletedSegments;
        var durabilityLeft = Math.Max(0, (int) MathF.Round(_startDurability) - done);
        var bleedLeft = MathF.Max(0f, _startBleedLm - done * _perSegLm);
        StatusText = Loc.GetString("advanced-health-minigame-status-wrap",
            ("percent", durabilityLeft),
            ("bleed", bleedLeft.ToString("0.00", CultureInfo.InvariantCulture)));

        if (_wrapTracking && dist >= WrapInnerRadius && dist <= WrapOuterRadius)
        {
            var angle = MathF.Atan2(pos.Y - center.Y, pos.X - center.X);
            var delta = angle - _lastWrapAngle;
            while (delta > MathF.PI) delta -= MathF.PI * 2f;
            while (delta < -MathF.PI) delta += MathF.PI * 2f;
            _wrapAngleAccum += MathF.Abs(delta);
            _lastWrapAngle = angle;
        }

        if (_maxSegments > 0 && CompletedSegments >= _maxSegments)
            Finish(true, quality: 1f);
    }

    private void UpdateSplint()
    {
        StatusText = Loc.GetString("advanced-health-minigame-status-splint");
    }

    private void TrySutureClick(Vector2 pos)
    {
        if (_sutureIndex >= _suturePoints.Length)
            return;

        var target = _suturePoints[_sutureIndex];
        if ((pos - target).Length() > 32f)
        {
            Finish(false, "advanced-health-minigame-fail-suture");
            return;
        }

        _sutureIndex++;
        if (_sutureIndex >= _suturePoints.Length)
            Finish(true, quality: 0.95f);
    }

    private void Finish(bool success, string reasonLoc = "", float quality = 0f)
    {
        IsRunning = false;
        OnFinished?.Invoke(new MinigameResult(success, reasonLoc, quality));
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        handle.DrawRect(box, Bg);
        // Framed terminal border.
        handle.DrawRect(new UIBox2(box.Left + 2, box.Top + 2, box.Right - 2, box.Bottom - 2), FrameDim, false);

        switch (_mode)
        {
            case AdvancedTreatmentType.ForeignBodyRemoval:
                DrawExtraction(handle, box);
                break;
            case AdvancedTreatmentType.Suture:
                DrawSuture(handle, box);
                break;
            case AdvancedTreatmentType.Splint:
                DrawSplint(handle, box);
                break;
            case AdvancedTreatmentType.Bandage:
            case AdvancedTreatmentType.PressureBandage:
                DrawBandageWrap(handle, box);
                break;
            default:
                DrawHoldZone(handle, box, _mode == AdvancedTreatmentType.Tourniquet ? TourniquetRadius : SteadyRadius);
                break;
        }
    }

    private void DrawExtraction(DrawingHandleScreen handle, UIBox2 box)
    {
        var s = UIScale;
        // Wound bed.
        var bed = new UIBox2(box.Left + 28 * s, box.Top + box.Height * 0.42f,
            box.Right - 28 * s, box.Top + box.Height * 0.78f);
        handle.DrawRect(bed, Wound);
        handle.DrawRect(bed, WoundEdge, false);

        // Shard-count pips across the top.
        for (var i = 0; i < ShardCount; i++)
        {
            var pip = new Vector2(box.Left + (24 + i * 20) * s, box.Top + 20 * s);
            var done = i < _shardIndex;
            handle.DrawCircle(pip, 6f * s, done ? Good : (i == _shardIndex ? Amber : SteelDim), true);
        }

        var shake = new Vector2(_shakeX, _shakeY) * s;

        for (var i = 0; i < ShardCount; i++)
        {
            if (i < _shardIndex)
                continue; // already removed

            var basePos = box.TopLeft + ShardPos(i) * s;
            var isCurrent = i == _shardIndex;
            var lift = isCurrent ? _shardProgress * PullDistance * s : 0f;
            var p = new Vector2(basePos.X, basePos.Y - lift);
            var color = isCurrent ? Steel : SteelDim;

            // Metallic sliver.
            handle.DrawRect(new UIBox2(p.X - 4 * s, p.Y - 16 * s, p.X + 4 * s, p.Y + 10 * s), color);
            handle.DrawRect(new UIBox2(p.X - 4 * s, p.Y - 16 * s, p.X + 4 * s, p.Y + 10 * s),
                Color.FromHex("#20242a"), false);

            if (isCurrent)
            {
                // Lateral guide rails around the active shard.
                var railL = basePos.X - LateralSlipLimit * s;
                var railR = basePos.X + LateralSlipLimit * s;
                handle.DrawLine(new Vector2(railL, bed.Top), new Vector2(railL, bed.Bottom), FrameDim);
                handle.DrawLine(new Vector2(railR, bed.Top), new Vector2(railR, bed.Bottom), FrameDim);
            }
        }

        // Grip crosshair follows the (shaking) hand.
        var grip = box.TopLeft + (_mousePos * s) + shake;
        handle.DrawLine(grip - new Vector2(9 * s, 0), grip + new Vector2(9 * s, 0), Amber);
        handle.DrawLine(grip - new Vector2(0, 9 * s), grip + new Vector2(0, 9 * s), Amber);

        // Progress bar for the active shard.
        var barY = box.Bottom - 30 * s;
        var barL = box.Left + 28 * s;
        var barR = box.Right - 28 * s;
        handle.DrawRect(new UIBox2(barL, barY, barR, barY + 12 * s), FrameDim);
        handle.DrawRect(new UIBox2(barL, barY, barL + (barR - barL) * _shardProgress, barY + 12 * s), Amber);
    }

    private void DrawBandageWrap(DrawingHandleScreen handle, UIBox2 box)
    {
        var s = UIScale;
        var center = box.Center + new Vector2(_shakeX, _shakeY) * s;
        var outer = WrapOuterRadius * s;
        var inner = WrapInnerRadius * s;
        var mid = (WrapInnerRadius + WrapOuterRadius) * 0.5f * s;

        // Limb cross-section: two rings.
        handle.DrawCircle(center, outer, FrameDim, false);
        handle.DrawCircle(center, inner, FrameDim, false);

        // Segment tick marks around the ring.
        for (var i = 0; i < SegmentsPerRevolution; i++)
        {
            var a = i * SegmentAngle - MathF.PI / 2f;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            handle.DrawLine(center + dir * (inner - 4 * s), center + dir * (inner + 4 * s), FrameDim);
        }

        // Wound in the middle, shrinking as bleeding is closed off.
        var bleedFrac = _startBleedLm > 0.0001f
            ? Math.Clamp((_startBleedLm - CompletedSegments * _perSegLm) / _startBleedLm, 0f, 1f)
            : 0f;
        handle.DrawCircle(center, (10f + 20f * bleedFrac) * s, WoundEdge, true);

        // Progress arc — amber dots for the fraction of the roll wound on.
        var frac = _maxSegments > 0 ? (float) CompletedSegments / _maxSegments : 0f;
        var dots = (int) (72 * frac);
        for (var i = 0; i < dots; i++)
        {
            var a = i / 72f * MathF.PI * 2f - MathF.PI / 2f;
            var p = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * mid;
            handle.DrawCircle(p, 3.5f * s, Amber, true);
        }

        // Direction arrow above the wound: wind this way.
        var ay = center.Y - (WrapOuterRadius + 22f) * s;
        var ax0 = center.X - 26f * s;
        var ax1 = center.X + 26f * s;
        handle.DrawLine(new Vector2(ax0, ay), new Vector2(ax1, ay), Amber);
        handle.DrawLine(new Vector2(ax1, ay), new Vector2(ax1 - 12f * s, ay - 9f * s), Amber);
        handle.DrawLine(new Vector2(ax1, ay), new Vector2(ax1 - 12f * s, ay + 9f * s), Amber);
    }

    private void DrawHoldZone(DrawingHandleScreen handle, UIBox2 box, float radius)
    {
        var s = UIScale;
        var center = box.Center + new Vector2(_shakeX, _shakeY) * s;
        handle.DrawCircle(center, radius * s, FrameDim, false);
        handle.DrawCircle(center, 7f * s, Good, true);
    }

    private void DrawSuture(DrawingHandleScreen handle, UIBox2 box)
    {
        var s = UIScale;
        var bed = new UIBox2(box.Left + 40 * s, box.Top + 60 * s, box.Right - 40 * s, box.Bottom - 60 * s);
        handle.DrawRect(bed, Wound);
        handle.DrawRect(bed, WoundEdge, false);
        for (var i = 0; i < _suturePoints.Length; i++)
        {
            var p = box.TopLeft + (_suturePoints[i] + new Vector2(_shakeX, _shakeY)) * s;
            var color = i < _sutureIndex ? Good
                : i == _sutureIndex ? Amber : SteelDim;
            handle.DrawCircle(p, 12f * s, color, true);
            handle.DrawCircle(p, 12f * s, Color.FromHex("#20242a"), false);
        }
    }

    private void DrawSplint(DrawingHandleScreen handle, UIBox2 box)
    {
        var s = UIScale;
        var cy = box.Top + box.Height * 0.55f;
        var cx = box.Left + box.Width / 2f;
        handle.DrawRect(new UIBox2(cx - 120 * s, cy - 10 * s, cx + 120 * s, cy + 10 * s), Color.FromHex("#6a4a2a"));
        handle.DrawRect(new UIBox2(cx + _boneOffset * s - 46 * s, cy - 20 * s, cx + _boneOffset * s + 46 * s, cy + 20 * s),
            Steel);
        handle.DrawRect(new UIBox2(cx - 46 * s, cy - 20 * s, cx + 46 * s, cy + 20 * s), Color.FromHex("#88888888"));
    }
}

readonly record struct MinigameResult(bool Success, string ReasonLoc, float Quality);
