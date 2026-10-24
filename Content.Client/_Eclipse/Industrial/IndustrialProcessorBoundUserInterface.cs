using Content.Shared._Eclipse.Industrial;

namespace Content.Client._Eclipse.Industrial;

public sealed class IndustrialProcessorBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private IndustrialProcessorWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = new IndustrialProcessorWindow();
        _window.OnSlotPressed += (isInput, index) =>
            SendMessage(new IndustrialProcessorSlotMessage(isInput, index));
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is IndustrialProcessorBoundUserInterfaceState cast)
            _window?.UpdateState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _window?.Orphan();
    }
}
