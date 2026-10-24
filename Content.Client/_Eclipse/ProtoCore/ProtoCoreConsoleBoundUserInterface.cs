using Content.Shared._Eclipse.ProtoCore;
namespace Content.Client._Eclipse.ProtoCore;

public sealed class ProtoCoreConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private ProtoCoreConsoleWindow? _window;

    protected override void Open()
    {
        base.Open();

        _window = new ProtoCoreConsoleWindow();
        _window.OnAction += action => SendMessage(new ProtoCoreConsoleActionMessage(action));
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is ProtoCoreConsoleBoundUserInterfaceState cast)
            _window?.UpdateState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _window?.Orphan();
    }
}
