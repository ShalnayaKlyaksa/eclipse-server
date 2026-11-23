using System.Linq;
using System.Numerics;
using Content.Shared.PDA;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.PDA;

public sealed class PdaBankControl : BoxContainer
{
    public event Action<BoundUserInterfaceMessage>? Send;
    private readonly Label _identity = new() { ClipText = true, StyleClasses = { "terminal-muted" } };
    private readonly Label _balance = new() { StyleClasses = { "pda-balance" } };
    private readonly Label _salary = new() { StyleClasses = { "terminal-muted" } };
    private readonly Label _salaryTime = new() { StyleClasses = { "terminal-muted" } };
    private readonly LineEdit _recipient = new() { HorizontalExpand = true, MinHeight = 26 };
    private readonly LineEdit _amount = new() { PlaceHolder = "0", HorizontalExpand = true, MinHeight = 26 };
    private readonly LineEdit _note = new() { HorizontalExpand = true, MinHeight = 26 };
    private readonly Label _recipientName = new() { ClipText = true, StyleClasses = { "pda-positive" } };
    private readonly Label _feedback = new() { ClipText = true };
    private readonly Button _transfer = PdaAppUi.Action(Loc.GetString("pda-bank-confirm"), true);
    private readonly BoxContainer _history = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };
    private PdaBankEntry[]? _transactions;
    private string _validated = "";
    private bool _pending;
    private bool _available;
    private int _salaryAmount;
    private TimeSpan _salaryAt;
    private readonly Robust.Shared.Timing.IGameTiming _timing = IoCManager.Resolve<Robust.Shared.Timing.IGameTiming>();

    public PdaBankControl()
    {
        Orientation = LayoutOrientation.Vertical; SeparationOverride = 5; VerticalExpand = true;
        var account = new BoxContainer { SeparationOverride = 10, Children =
        {
            new TextureRect { TexturePath = "/Textures/_Eclipse/Interface/PDA/Icons/Field/bank.png", SetSize = new Vector2(28), CanShrink = true, Stretch = TextureRect.StretchMode.KeepAspectCentered },
            new BoxContainer { Orientation = LayoutOrientation.Vertical, MinWidth = 140, Children = { new Label { Text = Loc.GetString("pda-app-available-balance"), StyleClasses = { "terminal-muted" } }, _balance } },
            new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, Children = { _identity, _salary, _salaryTime } },
        } };
        AddChild(PdaAppUi.Card(account, "#23383C", "#777562", 4));
        var body = new BoxContainer { SeparationOverride = 5, VerticalExpand = true };
        var history = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4, HorizontalExpand = true, VerticalExpand = true };
        history.AddChild(new Label { Text = Loc.GetString("pda-os-bank-history"), StyleClasses = { "pda-caption" } });
        history.AddChild(new PanelContainer { MinHeight = 1, StyleClasses = { "pda-divider" } });
        history.AddChild(new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, Children = { _history } });
        var historyCard = PdaAppUi.Card(history); historyCard.HorizontalExpand = true; body.AddChild(historyCard);
        body.AddChild(new PanelContainer { MinWidth = 1, StyleClasses = { "pda-divider" } });
        var form = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4, SetWidth = 265 };
        form.AddChild(new Label { Text = Loc.GetString("pda-app-transfer-title"), StyleClasses = { "pda-caption" } });
        _recipient.PlaceHolder = Loc.GetString("pda-services-recipient-id");
        var lookup = PdaAppUi.Action(Loc.GetString("pda-bank-lookup"));
        lookup.OnPressed += _ => Send?.Invoke(new PdaServicesRequestMessage(_recipient.Text));
        _recipient.OnTextEntered += _ => Send?.Invoke(new PdaServicesRequestMessage(_recipient.Text));
        _recipient.OnTextChanged += _ => { _validated = ""; _recipientName.Text = ""; RefreshButton(); };
        form.AddChild(PdaAppUi.Field(Loc.GetString("pda-services-recipient-id"), new BoxContainer { SeparationOverride = 6, Children = { _recipient, lookup } }));
        form.AddChild(_recipientName);
        form.AddChild(PdaAppUi.Field(Loc.GetString("pda-bank-amount"), _amount));
        _note.PlaceHolder = Loc.GetString("pda-app-transfer-note-placeholder");
        form.AddChild(PdaAppUi.Field(Loc.GetString("pda-bank-note"), _note));
        _amount.OnTextChanged += _ => RefreshButton(); _note.OnTextChanged += _ => RefreshButton();
        _transfer.OnPressed += _ =>
        {
            if (_transfer.Disabled || !int.TryParse(_amount.Text, out var amount)) return;
            _pending = true; RefreshButton();
            Send?.Invoke(new PdaServicesActionMessage(PdaServiceAction.Transfer, _validated, _note.Text, amount, Guid.NewGuid()));
        };
        form.AddChild(_transfer); form.AddChild(_feedback);
        var transferCard = PdaAppUi.Card(form); transferCard.VerticalExpand = true; body.AddChild(transferCard);
        AddChild(body); RefreshButton();
    }

    private void RefreshButton() => _transfer.Disabled = !_available || _pending || _validated.Length == 0 || !int.TryParse(_amount.Text, out var amount) || amount <= 0 || amount > 1000000 || _note.Text.Length > 80;

    public void Update(PdaServicesStateMessage state)
    {
        _available = state.Available;
        _identity.Text = state.Available ? state.Name + " · @" + state.Id : Loc.GetString("pda-services-no-account");
        _identity.ToolTip = _identity.Text;
        _balance.Text = state.Available ? state.Balance.ToString("N0") + " ₡" : "—";
        _salaryAmount = state.Salary; _salaryAt = _timing.CurTime + TimeSpan.FromSeconds(state.SecondsToSalary);
        if (state.Peer == _recipient.Text.Trim().ToLowerInvariant() && state.PeerName.Length > 0 && state.Peer != state.Id)
        { _validated = state.Peer; _recipientName.Text = state.PeerName + " · @" + state.Peer; }
        if (state.Feedback != null)
        {
            _pending = false; _feedback.Text = Loc.GetString(state.Feedback); _feedback.ToolTip = _feedback.Text;
            if (state.Feedback == "pda-services-transferred") { _amount.Text = ""; _note.Text = ""; }
        }
        RefreshButton();
        if (_transactions != null && _transactions.SequenceEqual(state.Transactions)) return;
        _transactions = state.Transactions; _history.RemoveAllChildren();
        if (state.Transactions.Length == 0) _history.AddChild(PdaAppUi.Empty(Loc.GetString("pda-app-no-transactions"), Loc.GetString("pda-app-transactions-hint")));
        foreach (var entry in state.Transactions)
        {
            var amount = new Label { Text = (entry.Amount > 0 ? "+" : "") + entry.Amount.ToString("N0") + " ₡" };
            if (entry.Amount > 0) amount.StyleClasses.Add("pda-positive");
            var details = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, Children =
            {
                new Label { Text = entry.Counterparty, ClipText = true },
                new Label { Text = entry.Note, ClipText = true, ToolTip = entry.Note, StyleClasses = { "terminal-muted" } },
            } };
            _history.AddChild(new BoxContainer { SeparationOverride = 7, Children = { details, amount } });
        }
    }
    protected override void FrameUpdate(Robust.Shared.Timing.FrameEventArgs args)
    {
        base.FrameUpdate(args);
        var remaining = _salaryAt - _timing.CurTime;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        _salary.Text = _available ? Loc.GetString("pda-app-salary", ("amount", _salaryAmount)) : "";
        _salaryTime.Text = _available ? Loc.GetString("pda-app-salary-time", ("time", remaining.ToString(@"mm\:ss"))) : "";
    }
}



