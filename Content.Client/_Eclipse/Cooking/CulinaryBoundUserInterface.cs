using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Eclipse.Cooking;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Eclipse.Cooking;

public sealed class CulinaryBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private CulinaryWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = new CulinaryWindow();
        _window.OnMethod += method => SendMessage(new CulinaryMethodMessage(method));
        _window.OnAction += action => SendMessage(new CulinaryActionMessage(action));
        _window.OnSlot += index => SendMessage(new CulinarySlotMessage(index));
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is CulinaryState cooking) _window?.Apply(cooking);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _window?.Dispose();
    }
}

