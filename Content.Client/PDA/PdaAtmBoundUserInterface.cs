using System.Numerics;
using Content.Shared.PDA;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client.PDA;

public sealed class PdaAtmBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private PdaAtmWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<PdaAtmWindow>();
        _window.Operation += (withdraw, amount) => SendMessage(new PdaAtmOperationMessage(withdraw, amount, Guid.NewGuid()));
        SendMessage(new PdaAtmRequestMessage());
    }
    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is PdaAtmStateMessage state) _window?.Update(state);
    }
}

public sealed class PdaAtmWindow : DefaultWindow
{
    public event Action<bool, int>? Operation;
    private readonly Label _owner = new();
    private readonly Label _balance = new() { StyleClasses = { "pda-title" } };
    private readonly Label _feedback = new();
    private readonly LineEdit _amount = new() { Text = "100", HorizontalExpand = true };
    private readonly Button _deposit = new();
    private readonly Button _withdraw = new();
    public PdaAtmWindow()
    {
        Title = Loc.GetString("pda-atm-title");
        SetSize = new Vector2(480, 360);
        MinSize = new Vector2(460, 340);
        Stylesheet = PdaTheme.Create(IoCManager.Resolve<IUserInterfaceManager>().Stylesheet);
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10, Margin = new Thickness(10) };
        column.AddChild(_owner); column.AddChild(_balance);
        var help = new RichTextLabel(); help.SetMessage(Loc.GetString("pda-atm-help")); column.AddChild(help);
        column.AddChild(_amount);
        _deposit.Text = Loc.GetString("pda-atm-deposit"); _withdraw.Text = Loc.GetString("pda-atm-withdraw");
        _deposit.MinHeight = _withdraw.MinHeight = 32;
        _deposit.OnPressed += _ => Submit(false); _withdraw.OnPressed += _ => Submit(true);
        column.AddChild(new BoxContainer { SeparationOverride = 8, Children = { _deposit, _withdraw } });
        column.AddChild(_feedback); Contents.AddChild(column);
    }
    private void Submit(bool withdraw)
    {
        if (!int.TryParse(_amount.Text, out var amount) || amount <= 0 || amount > 1000000) return;
        _deposit.Disabled = _withdraw.Disabled = true;
        Operation?.Invoke(withdraw, amount);
    }
    public void Update(PdaAtmStateMessage state)
    {
        _owner.Text = state.Id.Length == 0 ? Loc.GetString("pda-atm-no-card") : state.Name + " / ID " + state.Id;
        _balance.Text = Loc.GetString("pda-bank-balance", ("amount", state.Balance));
        _feedback.Text = state.Feedback != null ? Loc.GetString(state.Feedback) : "";
        _deposit.Disabled = _withdraw.Disabled = state.Id.Length == 0;
    }
}
