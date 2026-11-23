using System.Numerics;
using Content.Shared._Eclipse.AdvancedHealth;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._Eclipse.AdvancedHealth;

internal sealed class TreatmentMinigameControl : Control
{
    private static readonly Color Background = Color.FromHex("#080909");
    private static readonly Color Steel = Color.FromHex("#e6e4d9");
    private static readonly Color Muted = Color.FromHex("#686c69");
    private static readonly Color Accent = Color.FromHex("#a8c5b2");
    private readonly ForeignBodyKind _foreignBody;
    private float _syringeCapacity;
    private readonly float _startingDose;
    private float _pain;
    private float _skin = 1;
    private readonly bool _hasTool;
    private readonly int _maxSegments;
    private readonly float _durability;
    private float _bleed;
    private readonly float _perSegment;
    private readonly Texture[] _blood;
    private readonly Font _font = HealthMenuStyle.Mono(12);
    private readonly Texture _forceps;
    private readonly Texture _shard;
    private readonly Texture _glass;
    private readonly Texture _bullet;
    private readonly Texture _syringe;
    private readonly Texture _syringePlunger;
    private readonly Vector2[] _lineVertices = new Vector2[6];
    private bool _pointerInside;
    private bool _reported;
    private ICursor? _toolCursor;
    public TreatmentMinigameState State { get; private set; }
    public event Action<MinigameResult>? OnFinished;
    public event Action? OnCancel;
    public event Action<float>? OnInjury;
    public event Action<float>? OnNeedleBroken;
    public bool IsRunning => State.Running;
    public int CompletedSegments => State.CompletedSegments;

    public TreatmentMinigameControl(AdvancedTreatmentType mode, float pain, bool hasTool,
        int maxSegments, float durability, float bleed, float perSegment, ForeignBodyKind foreignBody = ForeignBodyKind.Unknown, float dose = 0,
        float syringeCapacity = 15)
    {
        _foreignBody = foreignBody;
        _syringeCapacity = Math.Max(0.001f, syringeCapacity);
        _startingDose = dose;
        _pain = pain;
        _hasTool = hasTool;
        _maxSegments = maxSegments;
        _durability = durability;
        _bleed = bleed;
        _perSegment = perSegment;
        State = new TreatmentMinigameState(mode, pain, hasTool, maxSegments);
        State.OnInjury += severity => OnInjury?.Invoke(severity);
        State.OnNeedleBroken += depth => OnNeedleBroken?.Invoke(depth);
        State.SetInjectionContents(0, _startingDose, _syringeCapacity);
        var cache = IoCManager.Resolve<IResourceCache>();
        const string directory = "/Textures/_Eclipse/Interface/AdvancedHealth/Treatment";
        _blood = new[] { HealthMenuStyle.Tex("blood_s"), HealthMenuStyle.Tex("blood"), HealthMenuStyle.Tex("blood_b") };
        _forceps = cache.GetResource<TextureResource>($"{directory}/forceps.png").Texture;
        _shard = cache.GetResource<TextureResource>($"{directory}/shard.png").Texture;
        _glass = cache.GetResource<TextureResource>($"{directory}/glass.png").Texture;
        _bullet = cache.GetResource<TextureResource>($"{directory}/bullet.png").Texture;
        _syringe = cache.GetResource<TextureResource>($"{directory}/syringe.png").Texture;
        _syringePlunger = cache.GetResource<TextureResource>($"{directory}/syringe-plunger.png").Texture;
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
    }

    public void Restart()
    {
        State = new TreatmentMinigameState(State.Mode, _pain, _hasTool, _maxSegments);
        State.OnInjury += severity => OnInjury?.Invoke(severity);
        State.OnNeedleBroken += depth => OnNeedleBroken?.Invoke(depth);
        State.SetInjectionContents(0, _startingDose, _syringeCapacity);
    }

    public void SetMedicalState(float operatorPain, float bleeding, float skin)
    {
        _pain = Math.Clamp(operatorPain, 0, 1);
        _bleed = Math.Max(0, bleeding);
        _skin = Math.Clamp(skin, 0, 1);
        State.SetPain(_pain);
    }

    public void SetInjectionAmount(float injected, float remaining, float? capacity = null)
    {
        if (capacity is > 0 && float.IsFinite(capacity.Value)) _syringeCapacity = capacity.Value;
        State.SetInjectionContents(injected, remaining, _syringeCapacity);
    }

    private float StageScale => Math.Min(Size.X / TreatmentMinigameState.Canvas.X, Size.Y / TreatmentMinigameState.Canvas.Y);
    private Vector2 StageOrigin => (Size - TreatmentMinigameState.Canvas * StageScale) / 2;
    private Vector2 ToStage(Vector2 point) => (point - StageOrigin) / Math.Max(0.001f, StageScale);

    protected override void EnteredTree()
    {
        base.EnteredTree();
        // The hand/tool is the pointer; keep the OS arrow out of the manipulation area.
        using var blank = new SixLabors.ImageSharp.Image<Rgba32>(8, 8);
        _toolCursor = IoCManager.Resolve<IClyde>().CreateCursor(blank, Vector2i.Zero);
        CustomCursorShape = _toolCursor;
    }

    protected override void ExitedTree()
    {
        CustomCursorShape = null;
        _toolCursor?.Dispose();
        _toolCursor = null;
        State.CancelGrip();
        base.ExitedTree();
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function == EngineKeyFunctions.UIRightClick)
        {
            State.CancelGrip();
            OnCancel?.Invoke();
            args.Handle();
            return;
        }
        if (args.Function != EngineKeyFunctions.UIClick) return;
        _pointerInside = true;
        State.Press(ToStage(args.RelativePosition));
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Function != EngineKeyFunctions.UIClick) return;
        State.Move(ToStage(args.RelativePosition));
        State.Release();
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _pointerInside = true;
        State.Move(ToStage(args.RelativePosition));
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _pointerInside = false;
        State.CancelGrip();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (State.Running)
        {
            _reported = false;
            State.Update(args.DeltaSeconds);
        }
        if (!State.Running && !_reported)
        {
            _reported = true;
            OnFinished?.Invoke(new MinigameResult(State.Success, State.Quality));
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(PixelSizeBox, Background);
        var previous = handle.GetTransform();
        var scale = StageScale * UIScale;
        handle.SetTransform(Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(StageOrigin * UIScale) * previous);
        try
        {
            switch (State.Mode)
            {
                case AdvancedTreatmentType.ForeignBodyRemoval: DrawExtraction(handle); break;
                case AdvancedTreatmentType.Injection: DrawSyringe(handle); break;
                case AdvancedTreatmentType.Suture: DrawSuture(handle); break;
                case AdvancedTreatmentType.SurgicalAccess: DrawIncision(handle); break;
                case AdvancedTreatmentType.InternalHemostasis: DrawVessels(handle); break;
                case AdvancedTreatmentType.Splint: DrawSplint(handle); break;
                case AdvancedTreatmentType.Bandage:
                case AdvancedTreatmentType.PressureBandage: DrawWrap(handle); break;
                case AdvancedTreatmentType.Tourniquet: DrawTourniquet(handle); break;
                default: DrawCompression(handle); break;
            }
        }
        finally { handle.SetTransform(previous); }
    }

    private void Line(DrawingHandleScreen h, Vector2 a, Vector2 b, Color color, float width = 1)
    {
        var delta = b - a;
        if (delta.LengthSquared() < 0.001f) return;
        var normal = Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * width / 2;
        _lineVertices[0] = a + normal;
        _lineVertices[1] = a - normal;
        _lineVertices[2] = _lineVertices[3] = b + normal;
        _lineVertices[4] = a - normal;
        _lineVertices[5] = b - normal;
        h.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _lineVertices, color);
    }

    private static readonly Vector2[] BoneOutline =
    {
        new(11, -20), new(30, -12), new(118, -12), new(129, -25), new(143, -27),
        new(156, -18), new(160, -5), new(155, 3), new(160, 15), new(150, 26),
        new(136, 27), new(122, 14), new(31, 12), new(13, 20), new(21, 7),
        new(7, -2), new(20, -8),
    };
    private static readonly Vector2[] OpenHand =
    {
        new(-6, 14), new(-6, 0), new(0, -7), new(8, -4), new(10, 39),
        new(20, 28), new(30, 29), new(35, 42), new(46, 46), new(53, 64),
        new(47, 87), new(34, 108), new(38, 204), new(-12, 204), new(-10, 113),
        new(-26, 89), new(-41, 68), new(-41, 55), new(-33, 50), new(-14, 67), new(-9, 62),
    };
    private static readonly Vector2[] GrippingHand =
    {
        new(-26, 17), new(-30, 4), new(-25, -5), new(-14, -10), new(0, -5),
        new(13, -7), new(26, -2), new(31, 11), new(43, 16), new(48, 32),
        new(45, 64), new(34, 96), new(38, 204), new(-12, 204), new(-10, 105),
        new(-28, 86), new(-43, 61), new(-45, 40), new(-36, 26), new(-25, 30),
    };

    private void Path(DrawingHandleScreen h, Vector2 origin, Vector2[] points, Color color, float width = 2, bool mirror = false)
    {
        Vector2 Point(Vector2 p) => origin + new Vector2(mirror ? -p.X : p.X, p.Y);
        for (var i = 0; i < points.Length; i++)
            Line(h, Point(points[i]), Point(points[(i + 1) % points.Length]), color, width);
    }

    private void Ring(DrawingHandleScreen h, Vector2 center, float radius, Color color, float width = 2)
    {
        var previous = center + new Vector2(radius, 0);
        for (var i = 1; i <= 80; i++)
        {
            var angle = i * MathF.Tau / 80;
            var next = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            Line(h, previous, next, color, width);
            previous = next;
        }
    }

    private void DrawHand(DrawingHandleScreen h, Vector2 tip, bool gripping)
    {
        var color = Color.InterpolateBetween(Color.FromHex("#ddc3bc"), Color.FromHex("#d94d53"), _pain);
        Path(h, tip, gripping ? GrippingHand : OpenHand, color, 2.5f);

    }

    private void TissueLine(DrawingHandleScreen h, Vector2 start, Vector2 end, Color color, float width = 1)
    {
        const int segments = 96;
        for (var i = 0; i < segments; i++)
        {
            var t = (i + 0.5f) / segments;
            var fade = Math.Clamp(Math.Min(t, 1 - t) / 0.2f, 0, 1);
            fade *= fade * (3 - 2 * fade);
            Line(h, Vector2.Lerp(start, end, (float) i / segments),
                Vector2.Lerp(start, end, (float) (i + 1) / segments), color.WithAlpha(color.A * fade), width);
        }
    }

    private void DrawTissue(DrawingHandleScreen h)
    {
        // The limb is a simple strip; its wound and tools provide the interaction detail.
        TissueLine(h, new Vector2(90, 180), new Vector2(550, 180), Steel, 2);
        TissueLine(h, new Vector2(90, 260), new Vector2(550, 260), Muted.WithAlpha(0.45f));
    }

    private void DrawIncision(DrawingHandleScreen h)
    {
        DrawTissue(h);
        for (var i = 0; i < 56; i++)
        {
            var t = i / 56f;
            Line(h, State.IncisionPoint(t), State.IncisionPoint((i + 1) / 56f),
                t < State.Progress ? Color.FromHex("#a96b61") : Accent.WithAlpha(0.3f), t < State.Progress ? 5 : 1);
            if (t < State.Progress)
                Line(h, State.IncisionPoint(t), State.IncisionPoint((i + 1) / 56f), Background, 1.5f);
        }
        var next = State.IncisionPoint(State.Progress);
        Ring(h, next, 14, Accent.WithAlpha(0.6f));
        DrawBleeding(h, next + new Vector2(-18, 24));
        var tip = State.Engaged ? State.Cursor : _pointerInside ? State.Cursor : next + new Vector2(72, -35);
        var previous = h.GetTransform();
        h.SetTransform(Matrix3x2.CreateTranslation(-tip) * Matrix3x2.CreateRotation(-0.45f)
            * Matrix3x2.CreateTranslation(tip) * previous);
        // Large procedural instrument shares the existing extraction game's outline, steel and hand style.
        Path(h, tip, ScalpelBlade, Steel, 2);
        Line(h, tip + new Vector2(8, 24), tip + new Vector2(8, 56), Muted, 2);
        Line(h, tip + new Vector2(8, 55), tip + new Vector2(15, 147), Muted, 14);
        Line(h, tip + new Vector2(4, 56), tip + new Vector2(11, 145), Steel, 2);
        Line(h, tip + new Vector2(12, 56), tip + new Vector2(19, 145), Steel, 2);
        for (var y = 65; y <= 112; y += 6)
            Line(h, tip + new Vector2(5 + (y - 55) * 0.07f, y), tip + new Vector2(12 + (y - 55) * 0.07f, y - 2), Steel.WithAlpha(0.6f));
        DrawHand(h, tip + new Vector2(18, 91), true);
        h.SetTransform(previous);
    }

    private static readonly Vector2[] ScalpelBlade =
    {
        new(0, 0), new(1, 23), new(4, 44), new(9, 55), new(14, 50), new(13, 31), new(8, 10),
    };

    private void DrawVessels(DrawingHandleScreen h)
    {
        DrawTissue(h);
        // A retracted wound uses the same restrained tissue contours as extraction and suturing.
        for (var i = 0; i < 40; i++)
        {
            var a = i * MathF.Tau / 40;
            var b = (i + 1) * MathF.Tau / 40;
            Line(h, new Vector2(320 + MathF.Cos(a) * 166, 200 + MathF.Sin(a) * 68),
                new Vector2(320 + MathF.Cos(b) * 166, 200 + MathF.Sin(b) * 68), Color.FromHex("#99675d"), 2);
        }
        for (var i = 0; i < 3; i++)
        {
            var point = State.VesselPoint(i);
            var color = i < State.ClampedVessels ? Accent.WithAlpha(0.7f) : Color.FromHex("#b4776e");
            Line(h, point - new Vector2(25, 30), point - new Vector2(3, 5), color, 5);
            Line(h, point + new Vector2(3, 5), point + new Vector2(24, 43), color, 5);
            Line(h, point - new Vector2(25, 30), point - new Vector2(3, 5), Background, 1.5f);
            Line(h, point + new Vector2(3, 5), point + new Vector2(24, 43), Background, 1.5f);
            Line(h, point + new Vector2(14, 22), point + new Vector2(38, 14), color.WithAlpha(0.6f), 2);
            if (i == State.ClampedVessels)
            {
                Ring(h, point, 14, Accent.WithAlpha(0.55f));
                var pulse = 12 + MathF.Sin(State.Time * 6) * 2;
                h.DrawTextureRect(_blood[0], UIBox2.FromDimensions(point + new Vector2(-pulse, 8), new Vector2(pulse * 2)));
            }
            if (i < State.ClampedVessels)
                Line(h, point - new Vector2(10, 5), point + new Vector2(10, 5), Steel, 3);
        }
        var tip = State.Engaged ? State.CurrentVessel : _pointerInside ? State.Cursor : State.CurrentVessel + new Vector2(76, -40);
        DrawForceps(h, tip, State.Engaged);
    }

    private void DrawPuncture(DrawingHandleScreen h, Vector2 point)
    {
        var damage = Math.Clamp((1 - _skin) * 2 + State.InjuryFlash * 0.4f, 0, 1);
        h.DrawCircle(point, 7 + damage * 10, Color.FromHex("#653c37").WithAlpha(0.4f + damage * 0.3f));
        Line(h, point - new Vector2(8 + damage * 15, damage * 3), point + new Vector2(8 + damage * 15, damage * 3), Color.FromHex("#ad4b45"), 2 + damage * 2);
        Line(h, point - new Vector2(8, 0), point + new Vector2(8, 0), Color.FromHex("#bd8073"), 2);
    }

    private void DrawBleeding(DrawingHandleScreen h, Vector2 center)
    {
        var remaining = Math.Max(0, _bleed - CompletedSegments * _perSegment);
        var level = BodyPartState.GetBleedingLevel(remaining / 0.06f);
        if (level == BleedingLevel.None) return;
        h.DrawTextureRect(_blood[(int) level - 1],
            UIBox2.FromDimensions(center - new Vector2(48), new Vector2(96)));
    }

    private void DrawExtraction(DrawingHandleScreen h)
    {
        DrawTissue(h);
        DrawPuncture(h, new Vector2(320, 196));
        DrawBleeding(h, new Vector2(276, 230));
        var shard = State.ShardPosition;
        if (!State.Success)
        {
            if (_foreignBody == ForeignBodyKind.Needle)
            {
                Line(h, shard - new Vector2(0, 22), shard + new Vector2(0, 20), Steel, 2);
                Line(h, shard - new Vector2(0, 22), shard - new Vector2(4, 18), Muted, 2);
            }
            else
            {
                var texture = _foreignBody switch { ForeignBodyKind.Glass => _glass, ForeignBodyKind.Bullet => _bullet, _ => _shard };
                h.DrawTextureRect(texture, UIBox2.FromDimensions(shard - new Vector2(17, 22), new Vector2(34, 44)));
            }
            if (!State.Engaged)
            {
                Line(h, shard - new Vector2(25, 8), shard - new Vector2(25, 20), Accent, 2);
                Line(h, shard - new Vector2(25, 20), shard - new Vector2(13, 20), Accent, 2);
                Line(h, shard + new Vector2(25, 8), shard + new Vector2(25, 20), Accent, 2);
                Line(h, shard + new Vector2(25, 20), shard + new Vector2(13, 20), Accent, 2);
            }
        }
        var tip = State.Engaged ? shard : _pointerInside ? State.Cursor : shard + new Vector2(76, -40);
        if (State.UsesForceps)
            DrawForceps(h, tip, State.Engaged);
        else
            DrawHand(h, tip, State.Engaged);
    }

    private void DrawForceps(DrawingHandleScreen h, Vector2 tip, bool closed)
    {
        var old = h.GetTransform();
        h.SetTransform(Matrix3x2.CreateTranslation(-tip) * Matrix3x2.CreateRotation(-0.28f) * Matrix3x2.CreateTranslation(tip) * old);
        var frame = closed ? 48 : 0;
        h.DrawTextureRectRegion(_forceps, new UIBox2(tip.X - 36, tip.Y - 6, tip.X + 36, tip.Y + 162),
            new UIBox2(frame, 0, frame + 48, 112));
        DrawHand(h, tip + new Vector2(12, 91), true);
        h.SetTransform(old);
    }

    private void DrawSyringe(DrawingHandleScreen h)
    {
        TissueLine(h, new Vector2(45, TreatmentMinigameState.TissueSurface), new Vector2(595, TreatmentMinigameState.TissueSurface), Steel, 2.5f);
        TissueLine(h, new Vector2(45, TreatmentMinigameState.TissueSurface + 28), new Vector2(595, TreatmentMinigameState.TissueSurface + 28), Muted.WithAlpha(0.3f));
        if (State.NeedleInserted || State.NeedleBroken)
            DrawPuncture(h, new Vector2(State.NeedleBroken ? State.BrokenNeedlePosition.X : State.SyringeTip.X, TreatmentMinigameState.TissueSurface));
        if (State.NeedleBroken)
        {
            var embedded = State.BrokenNeedlePosition;
            Line(h, new Vector2(embedded.X, TreatmentMinigameState.TissueSurface - 3), embedded, Steel, 2);
            Line(h, new Vector2(embedded.X - 3, TreatmentMinigameState.TissueSurface - 5), new Vector2(embedded.X + 2, TreatmentMinigameState.TissueSurface - 2), Steel, 2);
        }
        var tip = State.SyringeTip + new Vector2(0, -State.NeedleKick * 2);
        var pivot = State.SyringePivot;
        var before = h.GetTransform();
        h.SetTransform(Matrix3x2.CreateTranslation(-pivot) * Matrix3x2.CreateRotation(State.SyringeAngle) * Matrix3x2.CreateTranslation(pivot) * before);
        var origin = tip - new Vector2(32, TreatmentMinigameState.SyringeTipY);
        var size = new Vector2(64, TreatmentMinigameState.SyringeHeight);
        h.DrawTextureRect(_syringePlunger, UIBox2.FromDimensions(origin + new Vector2(0, State.PistonTravel), size));
        if (State.RemainingVolume > 0)
            h.DrawRect(new UIBox2(origin.X + 22, origin.Y + TreatmentMinigameState.ChamberTop + State.PistonTravel,
                origin.X + 42, origin.Y + TreatmentMinigameState.ChamberBottom), Color.FromHex("#9a8951").WithAlpha(0.7f));
        if (State.NeedleBroken)
            h.DrawTextureRectRegion(_syringe, UIBox2.FromDimensions(origin, new Vector2(64, 180)), new UIBox2(0, 0, 64, 180));
        else h.DrawTextureRect(_syringe, UIBox2.FromDimensions(origin, size));
        var hand = State.PlungerHeld ? State.PlungerGrip + new Vector2(12, 8)
            : State.Engaged ? State.SyringeGrip + new Vector2(12, 0)
            : _pointerInside ? State.Cursor : State.SyringeGrip + new Vector2(12, 0);
        h.SetTransform(Matrix3x2.CreateTranslation(-hand) * Matrix3x2.CreateScale(1.35f) * Matrix3x2.CreateTranslation(hand) * before);
        DrawHand(h, hand, State.Engaged || !_pointerInside);
        h.SetTransform(before);
    }

    private void DrawSuture(DrawingHandleScreen h)
    {
        DrawTissue(h);
        DrawBleeding(h, new Vector2(155, 240));
        var torn = Math.Clamp(1 - _skin + State.InjuryFlash * 0.2f, 0, 1);
        var thread = Color.FromHex("#c5b8a8");
        // Each tightened stitch brings only its own wound edges together.
        for (var i = 0; i < TreatmentMinigameState.StitchCount; i++)
        {
            var closure = i < State.Stitch ? 1 : i == State.Stitch ? State.WoundClosure : 0;
            var x = 198 + i * 64;
            var gap = (6 + torn * 8) * (1 - closure);
            Line(h, new Vector2(x, 222 - gap), new Vector2(x + 64, 222 - gap), Color.FromHex("#a7665b"), 2);
            Line(h, new Vector2(x, 222 + gap), new Vector2(x + 64, 222 + gap), Color.FromHex("#83473f"), 2);
            var a = State.StitchPoint(i, false);
            var b = State.StitchPoint(i, true);
            if (i < State.Stitch)
            {
                Line(h, a, b, thread, 2);
                Ring(h, (a + b) / 2, 3, thread, 2);
            }
            else
            {
                h.DrawCircle(a, 2.5f, Color.FromHex("#80443e"));
                h.DrawCircle(b, 2.5f, Color.FromHex("#80443e"));
            }
        }
        var index = Math.Min(State.Stitch, TreatmentMinigameState.StitchCount - 1);
        var exit = State.StitchPoint(index, true);
        var tip = State.NeedlePosition;
        if (State.SuturePhase != StitchPhase.Position)
        {
            var start = State.SuturePhase == StitchPhase.Pass ? State.StitchPoint(index, false) : exit;
            var slack = State.SuturePhase == StitchPhase.Pass ? 22 : Math.Max(0, 25 * (1 - State.ThreadTension));
            var color = State.ThreadTension > 1.25f ? Color.FromHex("#e85b50") : thread;
            var last = start;
            for (var i = 1; i <= 24; i++)
            {
                var t = i / 24f;
                var point = Vector2.Lerp(start, tip, t) + new Vector2(0, MathF.Sin(t * MathF.PI) * slack);
                Line(h, last, point, color, 1.5f);
                last = point;
            }
        }
        if (State.SuturePhase == StitchPhase.Knot)
        {
            var centre = State.KnotCenter;
            var radius = State.KnotTied ? 3 : 18;
            for (var i = 0; i < 36; i++)
            {
                var a = i * MathF.Tau / 36;
                var b = (i + 1) * MathF.Tau / 36;
                Line(h, centre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius,
                    centre + new Vector2(MathF.Cos(b), MathF.Sin(b)) * radius,
                    i / 36f <= State.KnotProgress ? thread : thread.WithAlpha(0.2f), 1.5f);
            }
        }
        var angle = State.NeedleAngle;
        var rotation = Matrix3x2.CreateRotation(angle);
        var centreNeedle = tip + Vector2.Transform(new Vector2(-17, 0), rotation);
        for (var i = 0; i < 16; i++)
        {
            var a = angle + i * 0.15f;
            var b = a + 0.15f;
            Line(h, centreNeedle + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 17,
                centreNeedle + new Vector2(MathF.Cos(b), MathF.Sin(b)) * 17, Steel, 2);
        }
        DrawHand(h, tip + new Vector2(24, 18), State.Engaged);
    }

    private void DrawSplint(DrawingHandleScreen h)
    {
        var c = TreatmentMinigameState.Center;
        Path(h, c, BoneOutline, Steel, 3, mirror: true);
        Path(h, c, BoneOutline, Muted.WithAlpha(0.5f), 2);
        Path(h, c + State.BoneOffset, BoneOutline, State.Progress >= 1 ? Accent : Steel, 3);
        Line(h, c + new Vector2(-140, 0), c - new Vector2(28, 0), Muted, 2);
        Line(h, c + State.BoneOffset + new Vector2(30, 0), c + State.BoneOffset + new Vector2(139, 0), Muted, 2);
        if (!State.Engaged)
            Ring(h, c + State.BoneOffset, 22, Muted);
        var cursor = _pointerInside ? State.Cursor : c + State.BoneOffset + new Vector2(32, 32);
        DrawHand(h, cursor, State.Engaged);
    }

    private void DrawWrap(DrawingHandleScreen h)
    {
        var c = TreatmentMinigameState.Center;
        Ring(h, c, 116, Steel, 3);
        Ring(h, c, 108, Muted.WithAlpha(0.3f));
        h.DrawCircle(c, 16, Muted);
        DrawBleeding(h, c + new Vector2(0, 49));
        var wrapped = Math.Min(1f, CompletedSegments / 8f);
        for (var i = 0; i < (int) (80 * wrapped); i++)
        {
            var a = i * MathF.Tau / 80 - MathF.PI;
            var b = a + MathF.Tau / 80;
            Line(h, c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 113,
                c + new Vector2(MathF.Cos(b), MathF.Sin(b)) * 113, Color.FromHex("#9bb0c1"), 7);
        }
        var spool = State.WrapSpoolPosition;
        var direction = (spool - c).LengthSquared() > 1 ? Vector2.Normalize(spool - c) : Vector2.UnitX;
        var tangent = new Vector2(-direction.Y, direction.X);
        Line(h, c + direction * 114 + tangent * 8, spool + tangent * 8, Steel, 2);
        Line(h, c + direction * 114 - tangent * 8, spool - tangent * 8, Steel, 2);
        var radius = 42 + Math.Clamp((_durability - CompletedSegments) / 100, 0, 1) * 18;
        var rollColor = State.Mode == AdvancedTreatmentType.PressureBandage
            ? Color.FromHex("#738c9e") : Color.FromHex("#8b9088");
        h.DrawCircle(spool, radius, rollColor);
        Ring(h, spool, radius, Steel);
        for (var r = 8; r < radius - 2; r += 6)
            Ring(h, spool, r, Background.WithAlpha(0.35f));
        h.DrawCircle(spool, 5, Background);
        h.DrawString(_font, spool + new Vector2(-34, -radius - 25),
            $"{Math.Max(0, (int) _durability - CompletedSegments)}%", 1, Steel);
        DrawHand(h, State.Engaged ? spool : _pointerInside ? State.Cursor : spool - new Vector2(4, 18), State.Engaged);
    }

    private void DrawTourniquet(DrawingHandleScreen h)
    {
        var c = TreatmentMinigameState.Center;
        Ring(h, c, 116, Steel, 3);
        Line(h, c - new Vector2(111, 20), c + new Vector2(111, 20), Color.FromHex("#52635e"), 30);
        Line(h, c - new Vector2(111, 34), c + new Vector2(111, 6), Steel, 2);
        Line(h, c - new Vector2(111, 6), c + new Vector2(111, 34), Steel, 2);
        var target = State.TensionTarget * MathF.Tau * 0.65f;
        var targetDirection = new Vector2(MathF.Cos(target), MathF.Sin(target));
        Line(h, c - targetDirection * 74, c + targetDirection * 74, Accent.WithAlpha(0.25f), 7);
        var direction = new Vector2(MathF.Cos(State.WindlassAngle), MathF.Sin(State.WindlassAngle));
        Line(h, c - direction * 74, c + direction * 74, Steel, 9);
        Ring(h, c, 15, Steel, 2);
        h.DrawCircle(c, 11, Background);
        h.DrawCircle(State.WindlassTip, 9, Accent);
        DrawHand(h, State.Engaged ? State.WindlassTip : _pointerInside ? State.Cursor : State.WindlassTip + new Vector2(10, -14), State.Engaged);
    }

    private void DrawCompression(DrawingHandleScreen h)
    {
        DrawTissue(h);
        DrawPuncture(h, State.HoldTarget);
        var pad = State.Mode == AdvancedTreatmentType.Hemostatic ? State.CompressionPadPosition
            : _pointerInside ? State.Cursor : new Vector2(470, 182);
        h.DrawRect(UIBox2.FromDimensions(pad - new Vector2(34, 27), new Vector2(68, 54)), Color.FromHex("#b8bbae"));
        h.DrawRect(UIBox2.FromDimensions(pad - new Vector2(34, 27), new Vector2(68, 54)), Steel, false);
        for (var x = -26; x <= 26; x += 6)
            Line(h, pad + new Vector2(x, -23), pad + new Vector2(x, 23), Background.WithAlpha(0.25f));
        for (var y = -21; y <= 21; y += 6)
            Line(h, pad + new Vector2(-30, y), pad + new Vector2(30, y), Background.WithAlpha(0.25f));
        if (State.Progress > 0)
            h.DrawCircle(pad, 4 + State.Progress * 12, Color.FromHex("#93675e").WithAlpha(0.6f));
        DrawHand(h, State.Engaged ? pad - new Vector2(0, 10) : _pointerInside ? State.Cursor : pad - new Vector2(0, 12), State.Engaged);
    }
}

internal readonly record struct MinigameResult(bool Success, float Quality);
