using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Eclipse.Beekeeping;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Eclipse.Beekeeping;

public sealed class ApiaryBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private ApiaryWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = new ApiaryWindow();
        _window.OnAction += action => SendMessage(new ApiaryActionMessage(action));
        _window.OnSlot += (output, index) => SendMessage(new ApiarySlotMessage(output, index));
        _window.OnClose += Close;
        _window.OpenCentered();
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is ApiaryState apiary) _window?.Apply(apiary);
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _window?.Dispose();
    }
}

