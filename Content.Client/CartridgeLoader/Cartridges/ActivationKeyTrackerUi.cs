using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader.Cartridges;
using Robust.Client.UserInterface;

namespace Content.Client.CartridgeLoader.Cartridges;

public sealed partial class ActivationKeyTrackerUi : UIFragment
{
    private ActivationKeyTrackerUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new ActivationKeyTrackerUiFragment();
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is ActivationKeyTrackerUiState trackerState)
            _fragment?.UpdateState(trackerState);
    }
}
