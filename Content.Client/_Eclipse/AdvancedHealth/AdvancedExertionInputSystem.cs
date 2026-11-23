using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Input;
using Robust.Client.GameObjects;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>A modifier tap is a gait toggle only when it was not used for another command.</summary>
public sealed class AdvancedExertionInputSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
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
                && TryComp<AdvancedExertionComponent>(_player.LocalEntity, out var exertion))
            {
                // Use the same timestamped input history as movement. A separate toggle
                // event can be replayed after later direction/Shift commands on rollback.
                var message = new ClientFullInputCmdMessage(_timing.CurTick, _timing.TickFraction,
                    _input.NetworkBindMap.KeyFunctionID(ContentKeyFunctions.AdvancedCalmWalk))
                {
                    State = exertion.CalmWalking ? BoundKeyState.Up : BoundKeyState.Down,
                };
                EntityManager.System<InputSystem>().HandleInputCommand(_player.LocalSession,
                    ContentKeyFunctions.AdvancedCalmWalk, message, replay: true);
            }
            _held = false;
            _pressedEntity = null;
        }
        return true;
    }
}
