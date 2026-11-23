using System.Numerics;
using Content.Shared._Eclipse.AdvancedHealth;

namespace Content.Client._Eclipse.AdvancedHealth;

internal sealed partial class TreatmentMinigameState
{
    public const float TissueSurface = 232;
    public const float SyringeHeight = 224;
    public const float SyringeTipY = 222;
    public const float ChamberTop = 104;
    public const float ChamberBottom = 158;
    public const float PistonStroke = ChamberBottom - ChamberTop;
    public Vector2 SyringeTip { get; private set; } = new(430, 212);
    public Vector2 SyringePivot => NeedleInserted ? new Vector2(SyringeTip.X, TissueSurface) : SyringeTip - new Vector2(0, 100);
    public Vector2 SyringeGrip => SyringePoint(SyringeTip - new Vector2(0, 100));
    public Vector2 PlungerGrip => SyringePoint(SyringeTip - new Vector2(0, 200 - PistonTravel));
    private Vector2 SyringePoint(Vector2 point) => Vector2.Transform(point - SyringePivot,
        Matrix3x2.CreateRotation(SyringeAngle)) + SyringePivot;
    public float RemainingVolume { get; private set; } = 15;
    public float SyringeCapacity { get; private set; } = 15;
    public float InjectedVolume { get; private set; }
    public float FillFraction => Math.Clamp(RemainingVolume / SyringeCapacity, 0, 1);
    public float PistonTravel => (1 - FillFraction) * PistonStroke;
    public float PlungerProgress => InjectedVolume / Math.Max(0.001f, InjectedVolume + RemainingVolume);
    public bool NeedleInserted { get; private set; }
    public bool NeedleBroken { get; private set; }
    public Vector2 BrokenNeedlePosition { get; private set; }
    public bool PlungerHeld { get; private set; }
    public bool PressingPlunger => PlungerHeld && NeedleInserted && !NeedleBroken && RemainingVolume > 0
        && _requestedPiston > PistonTravel + 0.2f;
    public bool SyringePrimed { get; private set; }
    public float NeedleKick { get; private set; }
    public float SyringeAngle { get; private set; }
    public event Action<float>? OnNeedleBroken;
    private float _syringeAngularVelocity;
    private float _lastSyringeMoveTime;
    private Vector2 _lastSyringePoint;
    private float _needleEntryX;
    private float _requestedPiston;
    private float _plungerGrabY;
    private float _pistonAtGrab;

    public void SetInjectionContents(float injected, float remaining, float capacity)
    {
        if (!float.IsFinite(injected) || !float.IsFinite(remaining) || !float.IsFinite(capacity) || capacity <= 0) return;
        InjectedVolume = Math.Max(0, injected);
        SyringeCapacity = capacity;
        RemainingVolume = Math.Clamp(remaining, 0, capacity);
        Progress = Success ? 1 : (NeedleInserted ? 0.2f : 0) + PlungerProgress * 0.7f;
    }

    public void SetInjectionProgress(float fraction)
    {
        if (!float.IsFinite(fraction)) return;
        fraction = Math.Clamp(fraction, 0, 1);
        SetInjectionContents(15 * fraction, 15 * (1 - fraction), 15);
    }

    private void GrabSyringe()
    {
        PlungerHeld = NeedleInserted && !NeedleBroken && Vector2.Distance(Cursor, PlungerGrip) <= 18;
        Engaged = PlungerHeld || Vector2.Distance(Cursor, SyringeGrip) <= 28;
        _itemGripOffset = SyringeTip - Cursor;
        _lastSyringeMoveTime = Time;
        _lastSyringePoint = SyringeTip;
        _plungerGrabY = Cursor.Y;
        _pistonAtGrab = _requestedPiston = PistonTravel;
        SyringePrimed = SyringeTip.Y <= TissueSurface - 38;
    }

    private void MoveSyringe()
    {
        var point = Cursor + _itemGripOffset;
        var delta = point - _lastSyringePoint;
        if (delta.LengthSquared() < 0.0001f) return;
        var elapsed = Math.Clamp(Time - _lastSyringeMoveTime, 1 / 120f, 0.1f);
        var downSpeed = delta.Y / elapsed;
        _lastSyringeMoveTime = Time;
        _lastSyringePoint = point;
        _syringeAngularVelocity += Math.Clamp(-delta.X * 0.045f + delta.Y * MathF.Sin(Time * 9 + 0.7f) * 0.008f, -1.8f, 1.8f);
        if (NeedleInserted && !NeedleBroken)
        {
            var lateral = MathF.Abs(point.X - _needleEntryX);
            // A controlled axial withdrawal is safe; a sideways jerk loads the embedded needle.
            if (lateral > 22 && (MathF.Abs(delta.X) > 20 || MathF.Abs(delta.X) / elapsed > 350))
            {
                if (SyringeTip.Y - TissueSurface >= 12) BreakNeedle();
                else
                {
                    NeedleInserted = SyringePrimed = false;
                    SyringeTip = new Vector2(Math.Clamp(point.X, 100, 540), TissueSurface - 2);
                    if (InjectedVolume > 0) Complete();
                }
                return;
            }
            if (PlungerHeld)
            {
                _requestedPiston = Math.Clamp(_pistonAtGrab + Cursor.Y - _plungerGrabY, 0, PistonStroke);
                return;
            }
            SyringeTip = new Vector2(_needleEntryX, Math.Clamp(point.Y, TissueSurface - 90, TissueSurface + 44));
            if (SyringeTip.Y < TissueSurface)
            {
                NeedleInserted = SyringePrimed = false;
                if (InjectedVolume > 0) Complete();
            }
            return;
        }
        if (NeedleBroken)
        {
            SyringeTip = Vector2.Clamp(point, new Vector2(100, 120), new Vector2(540, 330));
            if (SyringeTip.Y < TissueSurface - 20) Complete();
            return;
        }
        if (point.Y <= TissueSurface - 38) SyringePrimed = true;
        var y = Math.Clamp(point.Y, TissueSurface - 100, TissueSurface - 1);
        if (point.Y >= TissueSurface)
        {
            if (SyringePrimed && downSpeed >= 240)
            {
                NeedleInserted = true;
                NeedleKick = 1;
                _needleEntryX = Math.Clamp(point.X, 160, 480);
                y = TissueSurface + Math.Clamp(downSpeed * 0.025f, 28, 44);
                // The puncture stops the hand; subsequent movement starts at the actual tip.
                _itemGripOffset = new Vector2(_needleEntryX, y) - Cursor;
                _lastSyringePoint = new Vector2(_needleEntryX, y);
            }
            else SyringePrimed = false;
        }
        SyringeTip = new Vector2(Math.Clamp(point.X, 160, 480), y);
    }

    public void BreakNeedle(bool report = true)
    {
        if (NeedleBroken || !NeedleInserted) return;
        var depth = SyringeTip.Y - TissueSurface;
        BrokenNeedlePosition = SyringeTip;
        NeedleBroken = true;
        NeedleInserted = PlungerHeld = false;
        InjuryFlash = 1.5f;
        _itemGripOffset = SyringeTip - Cursor;
        if (report) OnNeedleBroken?.Invoke(depth);
    }

    private void UpdateSyringe(float dt)
    {
        // Pointer movement supplies its own timestamp; idle frames must not shorten the next motion interval.
        NeedleKick = Math.Max(0, NeedleKick - dt * 6);
        var angularTarget = MathF.Sin(Time * 11) * pain * 0.04f;
        _syringeAngularVelocity += (angularTarget - SyringeAngle) * dt * 35;
        _syringeAngularVelocity *= MathF.Exp(-dt * 7);
        SyringeAngle = Math.Clamp(SyringeAngle + _syringeAngularVelocity * dt, -0.24f, 0.24f);
        Progress = Success ? 1 : (NeedleInserted ? 0.2f : 0) + PlungerProgress * 0.7f;
    }
}
