using Content.Client.Hands.Systems;
using Content.Shared._Eclipse.Industrial;
using Robust.Client.Graphics;
using Robust.Client.Player;

namespace Content.Client._Eclipse.Industrial;

public sealed class IndustrialPortOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly HandsSystem _hands = default!;

    private IndustrialProcessorSystem? _processor;
    private bool _overlayActive;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var shouldShow = IsConfiguratorHeld();

        if (shouldShow && !_overlayActive)
        {
            _overlayManager.AddOverlay(new IndustrialPortOverlay());
            _overlayActive = true;
        }
        else if (!shouldShow && _overlayActive)
        {
            _overlayManager.RemoveOverlay<IndustrialPortOverlay>();
            _overlayActive = false;
        }
    }

    private bool IsConfiguratorHeld()
    {
        if (_player.LocalEntity is not { } player)
            return false;

        _processor ??= EntityManager.System<IndustrialProcessorSystem>();
        var held = _hands.GetActiveItem(player);
        return held != null && _processor.IsPortConfigurator(held.Value);
    }
}
