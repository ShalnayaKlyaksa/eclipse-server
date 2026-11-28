using System.Numerics;

namespace Content.Client._Eclipse.AdvancedHealth;

internal enum StitchPhase : byte { Position, Pass, Tension, Knot }

internal sealed partial class TreatmentMinigameState
{
    public StitchPhase SuturePhase { get; private set; }
    public Vector2 NeedlePosition { get; private set; } = new(505, 170);
    public float NeedleAngle { get; private set; }
    public float NeedlePass { get; private set; }
    public float ThreadTension { get; private set; }
    public float KnotProgress => Math.Clamp(_knotTurn / (MathF.Tau * 0.92f), 0, 1);
    public bool KnotTied { get; private set; }
    public Vector2 KnotCenter => StitchPoint(Math.Min(Stitch, StitchCount - 1), true) + new Vector2(25, 25);
    public float WoundClosure => SuturePhase >= StitchPhase.Tension ? Math.Clamp(ThreadTension, 0, 1) : 0;
    private Vector2 _sutureGripOffset;
    private float _lastSutureMove;
    private float _threadHold;
    private float _knotTurn;
    private float _knotAngle;

    public Vector2 NeedlePath(int index, float fraction)
        => Vector2.Lerp(StitchPoint(index, false), StitchPoint(index, true), fraction)
            + new Vector2(MathF.Sin(fraction * MathF.PI) * 24, 0);

    private void MoveSuture()
    {
        var point = Vector2.Clamp(Cursor + _sutureGripOffset, new Vector2(110, 110), new Vector2(560, 345));
        var previous = NeedlePosition;
        var elapsed = Math.Clamp(Time - _lastSutureMove, 1 / 120f, 0.1f);
        _lastSutureMove = Time;
        var delta = point - previous;
        if (delta.LengthSquared() > 0.1f) NeedleAngle = MathF.Atan2(delta.Y, delta.X);
        NeedlePosition = point;
        var entry = StitchPoint(Stitch, false);
        var exit = StitchPoint(Stitch, true);
        switch (SuturePhase)
        {
            case StitchPhase.Position:
                if (Vector2.Distance(point, entry) <= 14)
                {
                    SuturePhase = StitchPhase.Pass;
                    NeedlePass = 0;
                    NeedlePosition = entry;
                    _sutureGripOffset = entry - Cursor;
                }
                break;
            case StitchPhase.Pass:
                var along = Math.Clamp((point.Y - entry.Y) / (exit.Y - entry.Y), 0, 1);
                if (Vector2.Distance(point, NeedlePath(Stitch, along)) > 22)
                {
                    SutureSlip();
                    return;
                }
                NeedlePass = Math.Max(NeedlePass, along);
                if (along >= 0.98f)
                {
                    SuturePhase = StitchPhase.Tension;
                    NeedlePosition = exit;
                    _sutureGripOffset = exit - Cursor;
                }
                break;
            case StitchPhase.Tension:
                ThreadTension = Vector2.Distance(point, exit) / 88;
                if (ThreadTension > 1.7f) SutureSlip();
                break;
            case StitchPhase.Knot:
                var radius = Vector2.Distance(point, KnotCenter);
                var angle = MathF.Atan2(point.Y - KnotCenter.Y, point.X - KnotCenter.X);
                var turn = MathF.IEEERemainder(angle - _knotAngle, MathF.Tau);
                _knotAngle = angle;
                if (radius is >= 14 and <= 65 && MathF.Abs(turn) <= 0.65f)
                    _knotTurn = Math.Max(0, _knotTurn + turn);
                if (KnotProgress >= 1 && radius >= 60) KnotTied = true;
                if (delta.Length() > Math.Max(70, elapsed * 700)) SutureSlip();
                break;
        }
    }

    private void UpdateSuture(float dt)
    {
        if (Engaged) MoveSuture();
        if (SuturePhase != StitchPhase.Tension) return;
        if (Engaged && ThreadTension is >= 0.88f and <= 1.12f)
        {
            _threadHold += dt;
            if (_threadHold >= 0.4f)
            {
                SuturePhase = StitchPhase.Knot;
                _knotTurn = 0;
                _knotAngle = MathF.Atan2(NeedlePosition.Y - KnotCenter.Y, NeedlePosition.X - KnotCenter.X);
            }
        }
        else _threadHold = Math.Max(0, _threadHold - dt);
    }

    private void ReleaseSuture()
    {
        if (!KnotTied) return;
        Stitch++;
        Progress = (float) Stitch / StitchCount;
        KnotTied = false;
        ThreadTension = NeedlePass = _threadHold = _knotTurn = 0;
        SuturePhase = StitchPhase.Position;
        if (Stitch >= StitchCount) Complete();
    }

    private void SutureSlip()
    {
        Errors = Math.Min(1, Errors + 0.15f);
        InjuryFlash = 1;
        NeedlePass = ThreadTension = _threadHold = _knotTurn = 0;
        KnotTied = false;
        SuturePhase = StitchPhase.Position;
        CancelGrip();
        OnInjury?.Invoke(0.7f);
    }
}
