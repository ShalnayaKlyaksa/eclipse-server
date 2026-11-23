using System.Linq;
using Content.Client.PDA;
using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Shared.Research.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;
namespace Content.Client.CartridgeLoader.Cartridges;

public sealed partial class ResearchMailUi : UIFragment
{
    private BoxContainer _root = default!;
    private BoxContainer _messages = default!;
    private Label _recipient = default!;
    private Label _count = default!;
    private BoxContainer _reader = default!;
    private BoxContainer _readingPane = default!;
    private BoxContainer _compose = default!;
    private LineEdit _to = default!;
    private LineEdit _subject = default!;
    private TextEdit _body = default!;
    private Label _feedback = default!;
    private Button _send = default!;
    private Button _inboxButton = default!;
    private Button _sentButton = default!;
    private Label _composeCount = default!;
    private bool _pending;
    private BoundUserInterface _interface = default!;
    private ResearchMailUiState _state = new("", []);
    private Guid? _selected;
    private (Guid Id, string Subject, string Body, string Sender, string Recipient)? _shown;
    private bool _sent;
    public override Control GetUIFragmentRoot() => _root;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        if (_root is { Disposed: false }) return;
        _interface = userInterface;
        _root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, SeparationOverride = 4 };
        _messages = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 6 };
        _recipient = new Label { ClipText = true, HorizontalExpand = true, StyleClasses = { "terminal-muted" } };
        _count = new Label { StyleClasses = { "terminal-muted" } };
        var header = PdaAppUi.Header("mail", Loc.GetString("research-mail-program"), Loc.GetString("pda-app-mail-subtitle"));
        var refresh = PdaAppUi.Action(Loc.GetString("research-mail-refresh"));
        refresh.OnPressed += _ => userInterface.SendMessage(new CartridgeUiMessage(new ResearchMailRefreshMessage()));
        var compose = PdaAppUi.Action(Loc.GetString("pda-mail-compose"), true);
        compose.OnPressed += _ => Compose(!_compose.Visible);
        header.AddChild(refresh); header.AddChild(compose); _root.AddChild(header);
        _root.AddChild(new BoxContainer { SeparationOverride = 4, Children = { _recipient, _count } });
        _inboxButton = PdaAppUi.Action(Loc.GetString("pda-mail-inbox")); _inboxButton.ToggleMode = true;
        _sentButton = PdaAppUi.Action(Loc.GetString("pda-mail-outbox")); _sentButton.ToggleMode = true;
        _inboxButton.OnPressed += _ => SelectFolder(false); _sentButton.OnPressed += _ => SelectFolder(true);
        var list = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4, SetWidth = 190, Children =
        {
            new BoxContainer { SeparationOverride = 6, Children = { _inboxButton, _sentButton } },
            new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, Children = { _messages } },
        } };
        _reader = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6, HorizontalExpand = true };
        var reader = PdaAppUi.Card(new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true, Children = { _reader } }, "#14232A"); reader.HorizontalExpand = true;
        _readingPane = new BoxContainer { SeparationOverride = 4, HorizontalExpand = true, VerticalExpand = true, Children = { PdaAppUi.Card(list), new PanelContainer { MinWidth = 1, StyleClasses = { "pda-divider" } }, reader } };
        _root.AddChild(_readingPane);
        _compose = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, SeparationOverride = 4, Visible = false };
        _to = new LineEdit { PlaceHolder = Loc.GetString("pda-services-recipient-id"), MinHeight = 26, HorizontalExpand = true };
        _subject = new LineEdit { PlaceHolder = Loc.GetString("pda-mail-subject"), MinHeight = 26, HorizontalExpand = true };
        _body = new TextEdit { MinHeight = 90, HorizontalExpand = true, VerticalExpand = true };
        _send = PdaAppUi.Action(Loc.GetString("pda-mail-send"), true);
        _send.OnPressed += _ =>
        {
            if (_send.Disabled) return;
            _pending = true; RefreshSendButton();
            Send(new ResearchMailActionMessage { Action = ResearchMailAction.Send, Recipient = _to.Text, Subject = _subject.Text, Body = Rope.Collapse(_body.TextRope) });
        };
        _to.OnTextChanged += _ => RefreshSendButton();
        _subject.OnTextChanged += _ => RefreshSendButton();
        _body.OnTextChanged += _ => RefreshSendButton();
        _to.OnTextEntered += _ => _subject.GrabKeyboardFocus();
        _subject.OnTextEntered += _ => _body.GrabKeyboardFocus();
        _compose.AddChild(new BoxContainer { SeparationOverride = 4, Children =
        {
            PdaAppUi.Field(Loc.GetString("pda-services-recipient-id"), _to),
            PdaAppUi.Field(Loc.GetString("pda-mail-subject"), _subject),
        } });
        var editor = PdaAppUi.Card(_body, "#111F26", "#425962", 8); editor.VerticalExpand = true; _compose.AddChild(editor);
        var cancel = PdaAppUi.Action(Loc.GetString("pda-app-back-to-mail")); cancel.OnPressed += _ => Compose(false);
        _composeCount = new Label { StyleClasses = { "terminal-muted" }, HorizontalExpand = true, HorizontalAlignment = Control.HAlignment.Right };
        _compose.AddChild(new BoxContainer { SeparationOverride = 4, Children = { _send, cancel, _composeCount } });
        _root.AddChild(_compose); _feedback = new Label { ClipText = true }; _root.AddChild(_feedback);
        SelectFolder(false);
    }
    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not ResearchMailUiState mail) return;
        _state = mail; _recipient.Text = "@" + mail.Recipient; _recipient.ToolTip = mail.Recipient;
        _count.Text = Loc.GetString("pda-mail-count", ("total", mail.Messages.Length), ("unread", mail.Messages.Count(item => !item.Read)));
        if (mail.Feedback != null)
        {
            _pending = false; _feedback.Text = Loc.GetString(mail.Feedback); _feedback.ToolTip = _feedback.Text;
            if (mail.Feedback == "pda-mail-sent") { _subject.Text = ""; _body.TextRope = new Rope.Leaf(""); Compose(false); }
        }
        BuildList();
        var selected = (_sent ? mail.Sent : mail.Messages).FirstOrDefault(message => message.Id == _selected);
        if (selected != null) Show(selected);
        else if (_selected != null) EmptyReader();
        RefreshSendButton();
    }
    private void Send(ResearchMailActionMessage message) => _interface.SendMessage(new CartridgeUiMessage(message));
    private void Compose(bool compose)
    {
        _compose.Visible = compose; _readingPane.Visible = !compose; RefreshSendButton();
        if (compose && _root.VisibleInTree) _to.GrabKeyboardFocus();
    }
    private void RefreshSendButton()
    {
        var body = Rope.Collapse(_body.TextRope);
        _send.Disabled = _pending || string.IsNullOrWhiteSpace(_state.Recipient) ||
            !Content.Shared.PDA.PdaPublicId.Valid(_to.Text.Trim().ToLowerInvariant()) ||
            string.IsNullOrWhiteSpace(_subject.Text) || _subject.Text.Length > 80 || string.IsNullOrWhiteSpace(body) || body.Length > 4000;
        _composeCount.Text = Loc.GetString("pda-app-mail-limits", ("subject", _subject.Text.Length), ("body", body.Length));
    }
    private void SelectFolder(bool sent)
    {
        _sent = sent; _inboxButton.Pressed = !sent; _sentButton.Pressed = sent;
        Compose(false); EmptyReader();
        BuildList();
    }
    private void EmptyReader()
    {
        _selected = null; _shown = null; _reader.RemoveAllChildren();
        _reader.AddChild(PdaAppUi.Empty(Loc.GetString("pda-app-select-letter"), Loc.GetString("pda-app-select-letter-hint")));
    }
    private void BuildList()
    {
        _messages.RemoveAllChildren();
        var folder = _sent ? _state.Sent : _state.Messages;
        if (folder.Length == 0) _messages.AddChild(PdaAppUi.Empty(Loc.GetString("research-mail-empty"), Loc.GetString("pda-app-mail-empty-hint")));
        foreach (var message in folder.Reverse())
        {
            var select = new ContainerButton { StyleClasses = { "button" }, ToggleMode = true, Pressed = _selected == message.Id, HorizontalExpand = true, ToolTip = message.Subject };
            select.AddChild(new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 4, Margin = new Thickness(5), Children =
            {
                new Label { Text = (message.Read ? "" : "● ") + message.Subject, ClipText = true, StyleClasses = { "pda-caption" } },
                new Label { Text = _sent ? message.RecipientId : message.Sender, ClipText = true, StyleClasses = { "terminal-muted" } },
            } });
            select.OnPressed += _ =>
            {
                Show(message); BuildList();
                if (!message.Read) Send(new ResearchMailActionMessage { Action = ResearchMailAction.Read, Id = message.Id, SentFolder = _sent });
            };
            _messages.AddChild(select);
        }
    }
    private void Show(ResearchMailMessage message)
    {
        _selected = message.Id;
        var signature = (message.Id, message.Subject, message.Body, message.Sender, message.RecipientId);
        if (_shown == signature) return;
        _shown = signature; _reader.RemoveAllChildren();
        var subject = new RichTextLabel { HorizontalExpand = true, StyleClasses = { "terminal-heading" } };
        subject.SetMessage(FormattedMessage.FromUnformatted(message.Subject), PdaTheme.TextColor); _reader.AddChild(subject);
        _reader.AddChild(new Label { Text = _sent ? message.RecipientId : message.Sender, ClipText = true, ToolTip = message.Sender, StyleClasses = { "terminal-muted" } });
        var body = new RichTextLabel { HorizontalExpand = true }; body.SetMessage(FormattedMessage.FromUnformatted(message.Body), PdaTheme.TextColor);
        _reader.AddChild(body);
        var copy = PdaAppUi.Action(Loc.GetString("research-mail-copy")); copy.OnPressed += _ => IoCManager.Resolve<IClipboardManager>().SetText(message.Body);
        var reply = PdaAppUi.Action(Loc.GetString("pda-mail-reply"), true); reply.Disabled = _sent || message.SenderId.Length == 0;
        reply.OnPressed += _ => { Compose(true); _to.Text = message.SenderId; _subject.Text = "Re: " + message.Subject[..Math.Min(76, message.Subject.Length)]; };
        var remove = PdaAppUi.Action(Loc.GetString("pda-mail-delete"));
        remove.OnPressed += _ => { Send(new ResearchMailActionMessage { Action = ResearchMailAction.Delete, Id = message.Id, SentFolder = _sent }); SelectFolder(_sent); };
        _reader.AddChild(new BoxContainer { SeparationOverride = 7, Children = { reply, copy, remove } });
    }
}

