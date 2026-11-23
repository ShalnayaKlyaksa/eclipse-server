using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.PDA;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client.PDA;

public sealed class PaymentTerminalBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private PaymentTerminalWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<PaymentTerminalWindow>();
        _window.Submit += SendMessage;
        SendMessage(new PaymentTerminalMessage(PaymentTerminalAction.Refresh));
    }
    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is PaymentTerminalState state) _window?.Update(state);
        if (message is PaymentTerminalFeedback feedback) _window?.Feedback(feedback.Text);
    }
}

public sealed class PaymentTerminalWindow : DefaultWindow
{
    public event Action<PaymentTerminalMessage>? Submit;
    private PaymentTerminalState _state = new();
    private readonly RichTextLabel _invoice = new();
    private readonly Label _feedback = new();
    private readonly Button _pay = new() { MinHeight = 36 };
    private readonly Button _claim = new();
    private readonly BoxContainer _manage = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
    private readonly BoxContainer _actions = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6, Margin = new Thickness(12, 4, 12, 12) };
    private readonly OptionButton _mode = new();
    private readonly OptionButton _department = new();
    private readonly OptionButton _commissionDepartment = new();
    private readonly LineEdit _recipient = new() { PlaceHolder = "ID" };
    private readonly LineEdit _description = new();
    private readonly LineEdit _price = new() { Text = "30" };
    private readonly LineEdit _commission = new() { Text = "5" };

    public PaymentTerminalWindow()
    {
        Title = Loc.GetString("commerce-title");
        SetSize = new Vector2(530, 660); MinSize = new Vector2(480, 420);
        Stylesheet = PdaTheme.Create(IoCManager.Resolve<IUserInterfaceManager>().Stylesheet);
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10, Margin = new Thickness(12) };
        var scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true };
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        scroll.AddChild(column); root.AddChild(scroll); root.AddChild(_actions); Contents.AddChild(root);
        column.AddChild(new Label { Text = Loc.GetString("commerce-title"), StyleClasses = { "pda-title" } });
        column.AddChild(_invoice);
        _pay.OnPressed += _ => { _pay.Disabled = true; Send(PaymentTerminalAction.Pay); };
        column.AddChild(_pay);
        var refresh = new Button { Text = Loc.GetString("commerce-refresh") };
        refresh.OnPressed += _ => Send(PaymentTerminalAction.Refresh); column.AddChild(refresh);
        _claim.Text = Loc.GetString("commerce-claim");
        _claim.OnPressed += _ => Send(PaymentTerminalAction.Claim); column.AddChild(_claim);
        column.AddChild(_feedback);
        column.AddChild(_manage);
        _manage.AddChild(new Label { Text = Loc.GetString("commerce-management"), StyleClasses = { "pda-title" } });
        var help = new RichTextLabel(); help.SetMessage(Loc.GetString("commerce-help")); _manage.AddChild(help);
        Row("commerce-description", _description);
        Row("commerce-amount", _price);
        _mode.AddItem(Loc.GetString("commerce-department"), 0); _mode.AddItem(Loc.GetString("commerce-personal"), 1);
        _mode.OnItemSelected += args => { _mode.SelectId(args.Id); DestinationVisibility(); };
        _department.OnItemSelected += args => _department.SelectId(args.Id);
        _commissionDepartment.OnItemSelected += args => _commissionDepartment.SelectId(args.Id);
        Row("commerce-recipient", _mode); Row("commerce-personal-id", _recipient); Row("commerce-department", _department);
        Row("commerce-commission", _commission); Row("commerce-fee-department", _commissionDepartment);
        var issue = new Button { Text = Loc.GetString("commerce-issue"), MinHeight = 32 };
        issue.OnPressed += _ => Configure();
        var goods = new BoxContainer { SeparationOverride = 8 };
        var load = new Button { Text = Loc.GetString("commerce-load") };
        var unload = new Button { Text = Loc.GetString("commerce-return") };
        load.OnPressed += _ => Send(PaymentTerminalAction.LoadItem);
        unload.OnPressed += _ => Send(PaymentTerminalAction.ReturnItem);
        goods.AddChild(load); goods.AddChild(unload); _actions.AddChild(goods); _actions.AddChild(issue);
    }

    private void Row(string label, Control control)
    {
        var row = new BoxContainer { SeparationOverride = 8 };
        row.AddChild(new Label { Text = Loc.GetString(label), MinWidth = 174 });
        control.HorizontalExpand = true; row.AddChild(control); _manage.AddChild(row);
    }
    private void Send(PaymentTerminalAction action) => Submit?.Invoke(new PaymentTerminalMessage(action, _state.Version));
    private void Configure()
    {
        if (!int.TryParse(_price.Text, out var price) || !int.TryParse(_commission.Text, out var commission) ||
            _department.SelectedId < 0 || _commissionDepartment.SelectedId < 0 || _state.Departments.Length == 0) return;
        Submit?.Invoke(new PaymentTerminalMessage(PaymentTerminalAction.Configure, _state.Version)
        {
            Personal = _mode.SelectedId == 1, Recipient = _recipient.Text, Description = _description.Text,
            Department = _state.Departments[_department.SelectedId], Price = price, Commission = commission,
            CommissionDepartment = _state.Departments[_commissionDepartment.SelectedId],
        });
    }
    public void Feedback(string key) => _feedback.Text = Loc.GetString(key);
    private static string DepartmentName(string id) => IoCManager.Resolve<IPrototypeManager>().TryIndex<CargoAccountPrototype>(id, out var proto)
        ? Loc.GetString(proto.Name) : id;

    public void Update(PaymentTerminalState state)
    {
        _state = state;
        var fee = (int) ((long) state.Price * state.Commission / 100);
        var receiver = state.Personal ? "ID " + state.Recipient : DepartmentName(state.Department);
        _invoice.SetMessage(Loc.GetString("commerce-invoice", ("owner", state.Owner.Length > 0 ? state.Owner : "—"),
            ("description", state.Description), ("item", state.Item.Length > 0 ? state.Item : Loc.GetString("commerce-service")),
            ("receiver", receiver), ("price", state.Price), ("net", state.Price - fee), ("fee", fee),
            ("percent", state.Commission), ("department", DepartmentName(state.CommissionDepartment))));
        _pay.Text = Loc.GetString(state.BillOpen ? "commerce-pay" : "commerce-no-invoice", ("price", state.Price));
        _pay.Disabled = !state.BillOpen;
        _claim.Visible = state.CanClaim;
        _manage.Visible = state.CanManage;
        _actions.Visible = state.CanManage;
        MinSize = new Vector2(480, state.CanManage ? 420 : 300);
        SetSize = new Vector2(530, state.CanManage ? 660 : 330);
        _mode.SelectId(state.Personal ? 1 : 0);
        _recipient.Text = state.Recipient; _description.Text = state.Description;
        _price.Text = state.Price.ToString(); _commission.Text = state.Commission.ToString();
        _department.Clear(); _commissionDepartment.Clear();
        for (var i = 0; i < state.Departments.Length; i++)
        {
            var id = state.Departments[i];
            _department.AddItem(DepartmentName(id), i); _commissionDepartment.AddItem(DepartmentName(id), i);
            if (id == state.Department) _department.SelectId(i);
            if (id == state.CommissionDepartment) _commissionDepartment.SelectId(i);
        }
        DestinationVisibility();
    }
    private void DestinationVisibility()
    {
        _recipient.Editable = _mode.SelectedId == 1;
        _department.Disabled = _mode.SelectedId == 1;
    }
}
