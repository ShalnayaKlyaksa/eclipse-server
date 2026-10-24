using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Input;
using Robust.Client.Player;
using Robust.Shared.Input;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>A modifier tap is a gait toggle only when it was not used for another command.</summary>
public sealed class AdvancedExertionInputSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    private bool _held;
    private bool _used;
    private EntityUid? _pressedEntity;

    public bool Handle(BoundKeyFunction function, BoundKeyState state)
    {
        if (function != ContentKeyFunctions.AdvancedCalmWalk)
        {
            if (_held && state == BoundKeyState.Down) _used = true;
            return false;
        }
        if (state == BoundKeyState.Down)
        {
            _held = true;
            _used = false;
            _pressedEntity = _player.LocalEntity;
        }
        else
        {
            if (_held && !_used && _pressedEntity == _player.LocalEntity
                && HasComp<AdvancedExertionComponent>(_player.LocalEntity))
                RaisePredictiveEvent(new ToggleCalmWalkingEvent());
            _held = false;
            _pressedEntity = null;
        }
        return true;
    }
}
