using Content.Shared._Eclipse.RoundsShield;
using Content.Shared._Eclipse.RoundsShield.Components;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._Eclipse.RoundsShield;

public sealed class RoundsShieldAimSystem : EntitySystem
{
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private Angle? _lastSentAngle;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted || !_input.MouseScreenPosition.IsValid)
            return;

        if (_player.LocalEntity is not { } player ||
            !TryComp<RoundsShieldComponent>(player, out var shield) ||
            (!shield.Raising && !shield.Active))
        {
            _lastSentAngle = null;
            return;
        }

        var mapPosition = _eye.PixelToMap(_input.MouseScreenPosition);
        if (mapPosition.MapId == MapId.Nullspace)
            return;

        var xform = Transform(player);
        var direction = mapPosition.Position - _transform.GetMapCoordinates(player, xform: xform).Position;
        if (direction.LengthSquared() < 0.001f)
            return;

        var angle = Angle.FromWorldVec(direction).GetCardinalDir().ToAngle();
        if (_lastSentAngle != null &&
            Math.Abs(Angle.ShortestDistance(_lastSentAngle.Value, angle).Degrees) < 0.01f)
        {
            return;
        }

        _lastSentAngle = angle;
        RaisePredictiveEvent(new RoundsShieldAimEvent
        {
            User = GetNetEntity(player),
            AimAngle = angle,
        });
    }
}
