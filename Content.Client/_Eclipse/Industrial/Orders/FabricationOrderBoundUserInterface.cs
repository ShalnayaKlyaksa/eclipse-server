using Content.Shared._Eclipse.Industrial.Orders;
using Robust.Client.UserInterface;

namespace Content.Client._Eclipse.Industrial.Orders;

public sealed class FabricationOrderBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private FabricationOrderWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<FabricationOrderWindow>();
        _window.OnSubmit += (product, quantity, reason, urgent) => SendMessage(new FabricationOrderSubmitMessage(product, quantity, reason, urgent));
        _window.OnAction += (id, revision, action, priority, response) => SendMessage(new FabricationOrderActionMessage(id, revision, action, priority, response));
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is FabricationOrderState orders) _window?.Update(orders);
    }
}
