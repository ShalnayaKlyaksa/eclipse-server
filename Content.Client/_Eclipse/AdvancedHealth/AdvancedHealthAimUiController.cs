using System.Linq;
using Content.Client.Gameplay;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Input;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Timing;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>
/// Hold AdvancedHealthAimTarget (F / А) to pick a body aim zone; release to confirm. Quick tap = auto aim.
/// The 1s switch cooldown (and its "too fast" popup) is enforced authoritatively on the server, so a
/// single press produces a single feedback popup — the client only sends one event per press.
/// </summary>
public sealed class AdvancedHealthAimUiController : UIController, IOnStateEntered<GameplayState>
{
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private AdvancedHealthAimSelectorPopup? _popup;
    private bool _wasDown;
    private float _downTime;
    private bool _menuOpen;

    private const float HoldThreshold = 0.18f;

    public void OnStateEntered(GameplayState state)
    {
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_player.LocalEntity is not { } player ||
            !EntityManager.HasComponent<AimTargetComponent>(player))
        {
            CloseMenu();
            _wasDown = false;
            return;
        }

        if (IsTypingInUi())
        {
            CloseMenu();
            _wasDown = false;
            return;
        }

        var down = _input.DownKeyFunctions.Contains(ContentKeyFunctions.AdvancedHealthAimTarget);

        if (down && !_wasDown)
            _downTime = (float) _timing.RealTime.TotalSeconds;

        // Holding past the threshold opens the zone selector.
        if (down && !_menuOpen && (float) _timing.RealTime.TotalSeconds - _downTime >= HoldThreshold)
            OpenMenu();

        // Releasing confirms exactly once: the selected zone if the menu was open, otherwise a quick
        // tap picks auto-aim. The server decides whether the switch is accepted or on cooldown.
        if (!down && _wasDown)
        {
            if (_menuOpen && _popup != null)
                SetAim(_popup.Selected);
            else if ((float) _timing.RealTime.TotalSeconds - _downTime < HoldThreshold)
                SetAim(BodyPartTarget.Auto);

            CloseMenu();
        }

        _wasDown = down;
    }

    private bool IsTypingInUi() => UIManager.KeyboardFocused != null;

    private void SetAim(BodyPartTarget target)
    {
        EntityManager.System<AdvancedHealthClientSystem>().SetAimTarget(target);
    }

    private void OpenMenu()
    {
        if (_menuOpen)
            return;

        BodyPartTarget initial = BodyPartTarget.Chest;
        if (_player.LocalEntity is { } player &&
            EntityManager.TryGetComponent<AimTargetComponent>(player, out var aim) &&
            aim.CurrentTarget != BodyPartTarget.Auto)
        {
            initial = aim.CurrentTarget;
        }

        _popup = new AdvancedHealthAimSelectorPopup(initial);
        _popup.OpenAtCursor(UIManager.MousePositionScaled.Position);
        _menuOpen = true;
    }

    private void CloseMenu()
    {
        _popup?.Close();
        _popup = null;
        _menuOpen = false;
    }
}
