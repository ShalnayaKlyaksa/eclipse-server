using Content.Shared._Eclipse.Industrial;

namespace Content.Client._Eclipse.Industrial;

public sealed class IndustrialProcessorBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private IndustrialProcessorWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = new IndustrialProcessorWindow(Owner);
        _window.OnSlotPressed += (isInput, index, item) =>
            SendMessage(new IndustrialProcessorSlotMessage(isInput, index, item));
        _window.OnRecipeSelected += recipe => SendMessage(new IndustrialProcessorRecipeMessage(recipe));
        _window.OnToggle += () => SendMessage(new IndustrialProcessorToggleMessage());
        _window.OnManualOperate += () => SendMessage(new IndustrialManualOperateMessage());
        _window.OnPortChanged += (face, mode) => SendMessage(new IndustrialProcessorPortMessage(face, mode));
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
