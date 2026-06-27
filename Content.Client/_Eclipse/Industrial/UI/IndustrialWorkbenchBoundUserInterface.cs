using Content.Shared._Eclipse.Industrial;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Eclipse.Industrial.UI;

[UsedImplicitly]
public sealed class IndustrialWorkbenchBoundUserInterface(EntityUid owner, Enum uiKey)
    : BoundUserInterface(owner, uiKey)
{
    private WorkbenchCraftMenuPresenter? _presenter;

    protected override void Open()
    {
        base.Open();

        _presenter = new WorkbenchCraftMenuPresenter(this, Owner);
        _presenter.Open();
    }

    public void SendCraftRequest(string recipeId, int amount)
    {
        SendMessage(new IndustrialWorkbenchCraftBuiMessage(recipeId, amount));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        _presenter?.Dispose();
        _presenter = null;
    }
}
