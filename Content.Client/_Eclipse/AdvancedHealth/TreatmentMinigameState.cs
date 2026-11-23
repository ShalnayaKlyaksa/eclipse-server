using System.Numerics;
using Content.Shared._Eclipse.AdvancedHealth;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>Procedure coordinates are independent of window size and UI scale.</summary>
internal sealed partial class TreatmentMinigameState(AdvancedTreatmentType mode, float pain, bool hasTool, int maxSegments = 0)
{
    public static readonly Vector2 Canvas = new(640, 380);
    public static readonly Vector2 Center = new(320, 190);
    public const int StitchCount = 4;
    public const float SegmentAngle = MathF.Tau / 8;
    public readonly AdvancedTreatmentType Mode = mode;
    public bool IsExtraction => Mode == AdvancedTreatmentType.ForeignBodyRemoval;
    public event Action<float>? OnInjury;
    public int ExtractionMistakes { get; private set; }
    public float InjuryFlash { get; private set; }
    public float Pain => pain;
    public void SetPain(float value) => pain = Math.Clamp(value, 0, 1);
    public bool UsesForceps => IsExtraction && hasTool;
    public bool IsWrap => Mode is AdvancedTreatmentType.Bandage or AdvancedTreatmentType.PressureBandage;
    public bool Running { get; private set; } = true;
    public bool Success { get; private set; }
    public bool Held { get; private set; }
    public bool Engaged { get; private set; }
    public float Time { get; private set; }
    public float Progress { get; private set; }
    public float Strain { get; private set; }
    public float Errors { get; private set; }
    public float Quality => Math.Clamp(1f - Errors * 0.55f, 0.4f, 1f);
    public int Stitch { get; private set; }
    public int ClampedVessels { get; private set; }
    private float _clampHold;
    private float _lastIncisionMove;
    public Vector2 IncisionPoint(float progress) => new(180 + 280 * progress, 190 + MathF.Sin(progress * MathF.PI) * 25);
    public Vector2 VesselPoint(int index) => new(210 + 110 * index, 180 + MathF.Sin(index * 2.1f) * 35);
    public Vector2 CurrentVessel => VesselPoint(Math.Min(2, ClampedVessels));
    public int CompletedSegments => Math.Min(maxSegments, (int) (_wrapAngle / SegmentAngle + 0.0001f));
    public Vector2 Cursor { get; private set; }
    public Vector2 BoneOffset { get; private set; } = new(58, -26);
    public float Tension { get; private set; }
    public float TensionTarget => 0.72f;
    public float WindlassAngle { get; private set; }
    public Vector2 WindlassTip => Center + new Vector2(MathF.Cos(WindlassAngle), MathF.Sin(WindlassAngle)) * 74;
    public Vector2 HoldTarget => Mode == AdvancedTreatmentType.Hemostatic
        ? Center + new Vector2(MathF.Sin(Time * 1.7f) * 20, MathF.Sin(Time * 2.1f) * 6) : Center;
    public Vector2 WrapSpoolPosition { get; private set; } = Center - new Vector2(154, 0);
    public Vector2 CompressionPadPosition { get; private set; } = new(500, 190);
    public float Precision => UsesForceps ? 22f : 14f;
    public float ExtractionDistance => UsesForceps ? 72f : 88f;
    public Vector2 ShardPosition { get; private set; } = new(320, 196);
    private Vector2 _extractionGripOffset;
    private float _lastExtractionTime;
    private float _wrapRadiusError;
    private Vector2 _rawCursor;
    private float _wrapAngle;
    private float _wrapTravel;
    private float _lastAngle;
    private Vector2 _itemGripOffset;
    private float _windlassTurn;

    public Vector2 ExtractionPoint(float progress) => new(320, 196 - progress * ExtractionDistance);
    public Vector2 StitchPoint(int index, bool end) => new(224 + index * 64, (end ? 242 : 194) + MathF.Sin(index * 1.7f) * 12);
    public float WrapRadius(float angle) => 154;

    public void Move(Vector2 point)
    {
        _rawCursor = point;
        Cursor = point + HandShake();
        if (Engaged && IsExtraction)
            MoveShard();
        if (Engaged && Mode == AdvancedTreatmentType.Splint)
            BoneOffset = Vector2.Clamp(Cursor + _itemGripOffset - Center, new Vector2(-85, -55), new Vector2(85, 55));
        if (Engaged && Mode == AdvancedTreatmentType.Suture)
            MoveSuture();
        if (Engaged && Mode == AdvancedTreatmentType.Injection)
            MoveSyringe();
        if (Engaged && Mode == AdvancedTreatmentType.SurgicalAccess)
            MoveIncision();
        MoveHeldSupply();
    }

    private Vector2 HandShake()
    {
        var steadiness = UsesForceps ? 0.7f : 1f;
        var amplitude = pain * (3 + 17 * pain) * steadiness;
        return new Vector2(MathF.Sin(Time * 11) + MathF.Sin(Time * 23) * 0.3f,
            MathF.Sin(Time * 13) * 0.7f + MathF.Sin(Time * 19) * 0.2f) * amplitude;
    }

    public void Press(Vector2 point)
    {
        if (!Running || Held)
            return;
        Move(point);
        Held = true;
        switch (Mode)
        {
            case AdvancedTreatmentType.SurgicalAccess:
                Engaged = Vector2.Distance(Cursor, IncisionPoint(Progress)) <= 14;
                _lastIncisionMove = Time;
                break;
            case AdvancedTreatmentType.InternalHemostasis:
                Engaged = Vector2.Distance(Cursor, CurrentVessel) <= 14;
                _clampHold = 0;
                break;
            case AdvancedTreatmentType.ForeignBodyRemoval:
                Engaged = Vector2.Distance(Cursor, ShardPosition) <= Precision;
                _extractionGripOffset = ShardPosition - Cursor;
                _lastExtractionTime = Time;
                break;
            case AdvancedTreatmentType.Injection:
                GrabSyringe();
                break;
            case AdvancedTreatmentType.Suture:
                Engaged = Vector2.Distance(Cursor, NeedlePosition) <= 20;
                _sutureGripOffset = NeedlePosition - Cursor;
                _lastSutureMove = Time;
                break;
            case AdvancedTreatmentType.Splint:
                Engaged = Vector2.Distance(Cursor, Center + BoneOffset) <= 32;
                _itemGripOffset = Center + BoneOffset - Cursor;
                break;
            case AdvancedTreatmentType.Bandage:
            case AdvancedTreatmentType.PressureBandage:
                Engaged = Vector2.Distance(Cursor, WrapSpoolPosition) <= 64;
                _itemGripOffset = WrapSpoolPosition - Cursor;
                _wrapTravel = _wrapAngle;
                _lastAngle = MathF.Atan2(WrapSpoolPosition.Y - Center.Y, WrapSpoolPosition.X - Center.X);
                break;
            case AdvancedTreatmentType.Tourniquet:
                Engaged = Vector2.Distance(Cursor, WindlassTip) <= 26;
                _lastAngle = MathF.Atan2(Cursor.Y - Center.Y, Cursor.X - Center.X);
                break;
            case AdvancedTreatmentType.Hemostatic:
                Engaged = Vector2.Distance(Cursor, CompressionPadPosition) <= 38;
                _itemGripOffset = CompressionPadPosition - Cursor;
                break;
            default:
                Engaged = true;
                break;
        }
        if (!Engaged && !IsExtraction)
            Errors += 0.035f;
    }

    public void Release()
    {
        if (!Running)
            return;
        if (IsExtraction && Engaged && Progress >= 1)
            Complete();
        if (Mode == AdvancedTreatmentType.SurgicalAccess && Engaged && Progress >= 0.99f)
            Complete();
        if (Mode == AdvancedTreatmentType.InternalHemostasis && Engaged)
        {
            if (_clampHold >= 1)
            {
                ClampedVessels++;
                if (ClampedVessels == 3) Complete();
            }
            _clampHold = 0;
            Progress = ClampedVessels / 3f;
        }
        if (Mode == AdvancedTreatmentType.Tourniquet && Engaged && Progress >= 1
            && MathF.Abs(Tension - TensionTarget) <= 0.045f)
            Complete();
        if (Mode == AdvancedTreatmentType.Suture && Engaged) ReleaseSuture();
        if (Mode == AdvancedTreatmentType.Splint && Engaged)
        {
            if (Progress >= 1 && BoneOffset.Length() <= 4)
                Complete();
            else
                Progress = 0;
        }
        if (Strain >= 1) TreatmentSlip();
        Held = Engaged = PlungerHeld = false;
    }

    // Losing mouse focus never completes a stitch or a splint.
    public void CancelGrip()
    {
        Held = Engaged = false;
        PlungerHeld = false;
    }

    public void Update(float delta)
    {
        if (!Running)
            return;
        var dt = Math.Clamp(delta, 0, 0.05f);
        Time += dt;
        InjuryFlash = Math.Max(0, InjuryFlash - dt);
        Cursor = _rawCursor + HandShake();
        MoveHeldSupply();
        var stress = 0f;
        switch (Mode)
        {
            case AdvancedTreatmentType.SurgicalAccess:
                // The blade must actually follow the incision; holding still cannot make a cut.
                break;
            case AdvancedTreatmentType.InternalHemostasis:
                if (Engaged)
                {
                    if (Vector2.Distance(Cursor, CurrentVessel) > 25)
                        SurgicalSlip();
                    else
                        _clampHold = Math.Min(1, _clampHold + dt / 1.2f);
                }
                Progress = (ClampedVessels + _clampHold) / 3f;
                break;
            case AdvancedTreatmentType.ForeignBodyRemoval:
                if (Engaged)
                {
                    MoveShard();
                }
                break;
            case AdvancedTreatmentType.Suture:
                UpdateSuture(dt);
                break;
            case AdvancedTreatmentType.Splint:
                if (Engaged)
                {
                    BoneOffset += new Vector2(MathF.Sin(Time * 2.2f), MathF.Cos(Time * 1.8f)) * dt * pain * (hasTool ? 1 : 5);
                    if (BoneOffset.Length() <= 4)
                        Progress = Math.Min(1, Progress + dt / 1.8f);
                    else
                    {
                        Progress = Math.Max(0, Progress - dt * 0.5f);
                        stress = 0.4f;
                    }
                }
                break;
            case AdvancedTreatmentType.Bandage:
            case AdvancedTreatmentType.PressureBandage:
                if (Engaged)
                {
                    var vector = WrapSpoolPosition - Center;
                    var angle = MathF.Atan2(vector.Y, vector.X);
                    var turn = MathF.IEEERemainder(angle - _lastAngle, MathF.Tau);
                    _lastAngle = angle;
                    // Real turns count; reversing the roll or teleporting across the limb cannot create bandage.
                    if (MathF.Abs(turn) <= 0.45f)
                    {
                        _wrapTravel += turn;
                        _wrapAngle = Math.Max(_wrapAngle, _wrapTravel);
                    }
                    else if (MathF.Abs(turn) > 0.001f)
                        stress = 2;
                    if (_wrapRadiusError > 28) stress = 1.6f;
                    if (MathF.Abs(turn) > dt * 5) stress = Math.Max(stress, 1.1f);
                }
                Progress = maxSegments > 0 ? (float) CompletedSegments / maxSegments : 0;
                if (maxSegments <= 0 || CompletedSegments >= maxSegments)
                    Complete();
                break;
            case AdvancedTreatmentType.Tourniquet:
                if (Engaged && Vector2.Distance(Cursor, Center) > 35)
                {
                    var angle = MathF.Atan2(Cursor.Y - Center.Y, Cursor.X - Center.X);
                    var turn = MathF.IEEERemainder(angle - _lastAngle, MathF.Tau);
                    _lastAngle = angle;
                    if (MathF.Abs(turn) <= 0.3f)
                    {
                        WindlassAngle += turn;
                        _windlassTurn = Math.Clamp(_windlassTurn + turn, 0, MathF.Tau * 0.65f);
                    }
                }
                Tension = _windlassTurn / (MathF.Tau * 0.65f);
                var inZone = MathF.Abs(Tension - TensionTarget) <= 0.045f;
                Progress = Math.Clamp(Progress + dt * (Engaged && inZone ? 1 / 1.0f : -0.5f), 0, 1);
                if (Tension > 0.88f)
                    stress = 2;
                break;
            case AdvancedTreatmentType.Hemostatic:
                var onWound = Vector2.Distance(CompressionPadPosition, HoldTarget) <= 14;
                Progress = Math.Clamp(Progress + dt * (Engaged && onWound ? 1 / 3.0f : -0.5f), 0, 1);
                if (Engaged && !onWound) stress = 1;
                if (Progress >= 1) Complete();
                break;
            case AdvancedTreatmentType.Injection:
                UpdateSyringe(dt);
                break;
            default:
                var inside = Vector2.Distance(Cursor, HoldTarget) <= 22;
                Progress = Math.Clamp(Progress + dt * (Held && inside ? 1 / 3.6f : -0.18f), 0, 1);
                if (Held && !inside)
                    stress = 0.35f;
                if (Progress >= 1)
                    Complete();
                break;
        }
        Strain = Math.Clamp(Strain + dt * (stress > 0 ? stress : -0.45f), 0, 1);
        Errors += stress * dt * 0.14f;
        if (Strain >= 1) TreatmentSlip();
    }

    private void TreatmentSlip()
    {
        Strain = 0.25f;
        Errors = Math.Min(1, Errors + 0.15f);
        InjuryFlash = 1;
        CancelGrip();
        OnInjury?.Invoke(0.6f);
    }

    private void MoveHeldSupply()
    {
        if (!Engaged) return;
        if (IsWrap)
        {
            var offset = Cursor + _itemGripOffset - Center;
            _wrapRadiusError = MathF.Abs(offset.Length() - 154);
            if (offset.LengthSquared() >= 35 * 35)
                WrapSpoolPosition = Center + Vector2.Normalize(offset) * 154;
        }
        else if (Mode == AdvancedTreatmentType.Hemostatic)
            CompressionPadPosition = Vector2.Clamp(Cursor + _itemGripOffset, new Vector2(42, 32), new Vector2(598, 348));
    }

    private void MoveIncision()
    {
        var next = Math.Clamp((Cursor.X - 180) / 280, 0, 1);
        var elapsed = Math.Clamp(Time - _lastIncisionMove, 1 / 120f, 0.1f);
        _lastIncisionMove = Time;
        if (Vector2.Distance(Cursor, IncisionPoint(next)) > 14
            || MathF.Abs(next - Progress) * 280 > Math.Max(18, elapsed * 180))
        {
            SurgicalSlip();
            return;
        }
        Progress = Math.Max(Progress, next);
    }

    private void SurgicalSlip()
    {
        Errors += 0.18f;
        Strain = Math.Min(1, Strain + 0.2f);
        InjuryFlash = 0.6f;
        Engaged = false;
        _clampHold = 0;
        OnInjury?.Invoke(0.5f);
    }

    private void MoveShard()
    {
        // Forceps make this a forgiving direct manipulation: grip, pull clear, then release.
        // The shard stays where it was left, so a paused drag can always be regripped.
        var point = Cursor + _extractionGripOffset;
        var lateral = UsesForceps ? 100f : 42f;
        var next = new Vector2(Math.Clamp(point.X, 320 - lateral, 320 + lateral), Math.Clamp(point.Y, 80, 196));
        var elapsed = Math.Max(Time - _lastExtractionTime, 1 / 120f);
        _lastExtractionTime = Time;
        var pull = Math.Max(0, ShardPosition.Y - next.Y);
        var tooFast = !UsesForceps && pull > Math.Max(1.5f, 70 * elapsed);
        var safeSide = UsesForceps ? 18 : 10;
        var tooSideways = MathF.Abs(next.X - 320) > safeSide;
        if (tooFast || tooSideways)
        {
            var severity = Math.Clamp((tooFast ? pull / 24 : 0) + Math.Max(0, MathF.Abs(next.X - 320) - safeSide) / 25, 0.5f, 2);
            ExtractionMistakes++;
            InjuryFlash = 1.6f;
            Errors += severity * 0.2f;
            Strain = Math.Min(1, Strain + severity * 0.35f);
            ShardPosition = new Vector2(320, 196);
            Progress = 0;
            Engaged = false;
            OnInjury?.Invoke(severity);
            return;
        }
        ShardPosition = next;
        Progress = Math.Clamp((196 - ShardPosition.Y) / ExtractionDistance, 0, 1);
    }

    private void Complete()
    {
        Running = false;
        Success = true;
    }
}
