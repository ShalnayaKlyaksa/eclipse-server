using System.Linq;
using System.Numerics;
using Content.Shared.PDA;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.PDA;

public sealed class PdaMessengerControl : BoxContainer
{
    public event Action<BoundUserInterfaceMessage>? Send;
    private PdaMessengerStateMessage _state = new();
    private readonly LineEdit _search = new() { MinHeight = 26 };
    private readonly Label _identity = new() { ClipText = true, StyleClasses = { "terminal-muted" } };
    private readonly BoxContainer _chatList = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
    private readonly BoxContainer _body = new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, VerticalExpand = true, SeparationOverride = 3 };
    private readonly Label _title = new() { HorizontalExpand = true, ClipText = true, StyleClasses = { "pda-caption" } };
    private readonly Label _subtitle = new() { ClipText = true, StyleClasses = { "terminal-muted" } };
    private readonly Label _feedback = new() { ClipText = true, StyleClasses = { "terminal-muted" } };
    private readonly BoxContainer _messages = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4, HorizontalExpand = true };
    private readonly ScrollContainer _scroll = new() { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
    private readonly TextEdit _draft = new() { MinHeight = 38, MaxHeight = 68, HorizontalExpand = true };
    private readonly Button _send = PdaAppUi.Action("→", true);
    private readonly Button _info = PdaAppUi.Action("...");
    private readonly Label _context = new() { HorizontalExpand = true, ClipText = true, StyleClasses = { "terminal-muted" } };
    private readonly Label _count = new() { StyleClasses = { "terminal-muted" } };
    private readonly BoxContainer _contextRow = new() { SeparationOverride = 4 };
    private readonly BoxContainer _thread = new() { Orientation = LayoutOrientation.Vertical, VerticalExpand = true, HorizontalExpand = true, SeparationOverride = 3 };
    private readonly BoxContainer _form = new() { Orientation = LayoutOrientation.Vertical, VerticalExpand = true, HorizontalExpand = true, SeparationOverride = 5, Visible = false };
    private readonly BoxContainer _details = new() { Orientation = LayoutOrientation.Vertical, VerticalExpand = true, HorizontalExpand = true, SeparationOverride = 5, Visible = false };
    private readonly Dictionary<string, (string Text, long Reply, long Edit)> _drafts = new();
    private readonly Dictionary<Guid, (string Chat, string Text, long Edit)> _pending = new();
    private PdaConversationMessage[] _shownMessages = [];
    private PdaConversation[] _shownChats = [];
    private int _filter;
    private int _scrollBottom;
    private long _reply;
    private long _edit;
    private long _lastRead;
    private string _selected = "";
    private bool _initialized;
    private Guid _creating;
    private Popup? _popup;
    public string SelectedChat => _selected;
    private string DraftText => Rope.Collapse(_draft.TextRope);

    public PdaMessengerControl()
    {
        HorizontalExpand = VerticalExpand = true; SeparationOverride = 5;
        var sidebar = new BoxContainer { Orientation = LayoutOrientation.Vertical, SetWidth = 184, SeparationOverride = 4 };
        var brand = new BoxContainer { SeparationOverride = 5, Children =
        {
            new TextureRect { TexturePath = "/Textures/_Eclipse/Interface/PDA/Icons/Field/messenger.png", SetSize = new Vector2(22), CanShrink = true, Stretch = TextureRect.StretchMode.KeepAspectCentered },
            new Label { Text = Loc.GetString("pda-orbit-title"), HorizontalExpand = true, StyleClasses = { "pda-caption" } },
        } };
        var copy = PdaAppUi.Action("ID"); copy.ToolTip = Loc.GetString("pda-max-copy-id"); copy.OnPressed += _ => IoCManager.Resolve<IClipboardManager>().SetText(_state.OwnId);
        brand.AddChild(copy); sidebar.AddChild(brand); sidebar.AddChild(_identity);
        _search.PlaceHolder = Loc.GetString("pda-orbit-search"); _search.OnTextChanged += _ => BuildChats(true); sidebar.AddChild(_search);
        var filters = new BoxContainer { SeparationOverride = 2 };
        var filterGroup = new ButtonGroup();
        for (var i = 0; i < 3; i++)
        {
            var index = i; var button = PdaAppUi.Action(Loc.GetString(new[] { "pda-orbit-all", "pda-orbit-direct", "pda-orbit-groups" }[i]));
            button.ToggleMode = true; button.Group = filterGroup; button.Pressed = i == 0;
            button.OnPressed += _ => { _filter = index; BuildChats(true); }; filters.AddChild(button);
        }
        sidebar.AddChild(filters); sidebar.AddChild(new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, Children = { _chatList } });
        var create = new BoxContainer { SeparationOverride = 3 };
        var direct = PdaAppUi.Action(Loc.GetString("pda-orbit-new-chat")); direct.HorizontalExpand = true; direct.OnPressed += _ => ShowCreate(false);
        var group = PdaAppUi.Action(Loc.GetString("pda-orbit-new-group")); group.HorizontalExpand = true; group.OnPressed += _ => ShowCreate(true);
        create.AddChild(direct); create.AddChild(group); sidebar.AddChild(create); AddChild(sidebar);
        AddChild(new PanelContainer { MinWidth = 1, StyleClasses = { "pda-divider" } });
        var heading = new BoxContainer { SeparationOverride = 5, Children = { new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, Children = { _title, _subtitle } }, _info } };
        _info.ToolTip = Loc.GetString("pda-orbit-info"); _info.OnPressed += _ => ShowDetails(); _body.AddChild(heading);
        _body.AddChild(new PanelContainer { MinHeight = 1, StyleClasses = { "pda-divider" } });
        _scroll.AddChild(_messages); _thread.AddChild(_scroll);
        var cancelContext = PdaAppUi.Action("×"); cancelContext.OnPressed += _ => { _reply = _edit = 0; RefreshComposer(); };
        _contextRow.AddChild(_context); _contextRow.AddChild(cancelContext); _thread.AddChild(_contextRow);
        _draft.OnTextChanged += _ => RefreshComposer();
        _draft.OnKeyBindDown += args => { if (args.Function == EngineKeyFunctions.MultilineTextSubmit) { SendDraft(); args.Handle(); } };
        _send.ToolTip = Loc.GetString("pda-orbit-send-hint"); _send.OnPressed += _ => SendDraft();
        var composer = PdaAppUi.Card(_draft, "#12232B", "#536970", 3); composer.HorizontalExpand = true;
        _thread.AddChild(new BoxContainer { SeparationOverride = 4, Children = { composer, _send } });
        _thread.AddChild(_count); _body.AddChild(_thread); _body.AddChild(_form); _body.AddChild(_details); _body.AddChild(_feedback); AddChild(_body);
        Update(new());
    }

    private void SetDraft(string text) { _draft.TextRope = Rope.Leaf.Empty; _draft.CursorPosition = new TextEdit.CursorPos(); _draft.InsertAtCursor(text); }
    private void Request(PdaMessengerAction action, string text = "", string[]? members = null, long message = 0, string? chat = null)
    {
        var request = Guid.NewGuid();
        if (action is PdaMessengerAction.Send or PdaMessengerAction.Edit) _pending[request] = (_selected, DraftText, _edit);
        if (action is PdaMessengerAction.OpenDirect or PdaMessengerAction.CreateGroup) _creating = request;
        Send?.Invoke(new PdaMessengerActionMessage(action, chat ?? _selected, text, members, message, request));
    }
    private void SendDraft()
    {
        if (_send.Disabled) return;
        if (_edit != 0) Request(PdaMessengerAction.Edit, DraftText, message: _edit);
        else Request(PdaMessengerAction.Send, DraftText, message: _reply);
        _send.Disabled = true;
    }
    private void RefreshComposer()
    {
        var text = DraftText;
        _draft.Editable = _state.Available && !_state.Blocked && _selected.Length > 0;
        _send.Disabled = !_draft.Editable || string.IsNullOrWhiteSpace(text) || text.Length > 1000 || _pending.Values.Any(pending => pending.Chat == _selected);
        _count.Text = text.Length == 0 ? Loc.GetString("pda-orbit-send-hint") : text.Length + "/1000 · " + Loc.GetString("pda-orbit-send-hint");
        _contextRow.Visible = _reply != 0 || _edit != 0;
        var original = _state.Messages.FirstOrDefault(message => message.Id == (_edit != 0 ? _edit : _reply));
        _context.Text = _edit != 0 ? Loc.GetString("pda-orbit-editing") : original == null ? "" : Loc.GetString("pda-orbit-reply-to", ("name", original.Name)) + " · " + original.Text.Replace('\n', ' ');
    }
    public void Update(PdaMessengerStateMessage state)
    {
        if (_state.OwnId.Length > 0 && _state.OwnId != state.OwnId) { _drafts.Clear(); _pending.Clear(); _selected = ""; SetDraft(""); }
        var refreshDetails = _details.Visible && (!_state.Members.SequenceEqual(state.Members) || _state.Title != state.Title || _state.Owner != state.Owner || _state.Blocked != state.Blocked);
        var changed = !_initialized || _selected != state.Selected; _initialized = true;
        if (changed)
        {
            if (_selected.Length > 0) _drafts[_selected] = (DraftText, _reply, _edit);
            _selected = state.Selected; var draft = _drafts.GetValueOrDefault(_selected);
            SetDraft(draft.Text ?? ""); _reply = draft.Reply; _edit = draft.Edit; _lastRead = 0;
            _thread.Visible = true; _form.Visible = _details.Visible = false;
        }
        _state = state;
        _identity.Text = state.Available ? "@" + state.OwnId : Loc.GetString("pda-services-no-account");
        _title.Text = _selected.Length == 0 ? Loc.GetString("pda-orbit-select-chat") : state.Title;
        _subtitle.Text = _selected.Length == 0 ? "" : state.Group ? Loc.GetString("pda-orbit-member-count", ("count", state.Members.Length)) : "@" + state.Selected[2..];
        _info.Disabled = _selected.Length == 0;
        BuildChats(changed);
        if (changed || !_shownMessages.SequenceEqual(state.Messages))
        {
            var follow = changed || _scroll.VScroll >= Math.Max(0, _messages.Height - _scroll.Height) - 24 || _pending.Values.Any(pending => pending.Chat == _selected);
            _shownMessages = state.Messages; BuildMessages(); if (follow) _scrollBottom = 2;
        }
        if (state.Request != Guid.Empty)
        {
            if (_pending.Remove(state.Request, out var pending) && state.Feedback is "pda-services-message-sent" or "pda-orbit-done")
            {
                if (_selected == pending.Chat && DraftText == pending.Text && _edit == pending.Edit) { SetDraft(""); _reply = _edit = 0; }
                if (_drafts.TryGetValue(pending.Chat, out var saved) && saved.Text == pending.Text) _drafts.Remove(pending.Chat);
            }
            if (state.Request == _creating)
            {
                if (state.Feedback == "pda-orbit-done") { _form.Visible = false; _thread.Visible = true; }
                _creating = Guid.Empty;
            }
        }
        if (state.Feedback != null) { _feedback.Text = Loc.GetString(state.Feedback); _feedback.ToolTip = _feedback.Text; }
        RefreshComposer();
        if (refreshDetails && _details.Visible) ShowDetails();
        if (VisibleInTree && _thread.Visible && state.Messages.LastOrDefault() is { } last && last.Id > _lastRead)
        { _lastRead = last.Id; Send?.Invoke(new PdaMessengerRequestMessage(_selected)); }
    }

    private void BuildChats(bool force = false)
    {
        if (!force && _shownChats.SequenceEqual(_state.Chats)) return;
        _shownChats = _state.Chats; _chatList.RemoveAllChildren();
        foreach (var chat in _state.Chats.Where(chat => (_filter == 0 || chat.Group == (_filter == 2)) && (chat.Title.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase) || chat.Preview.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase))))
        {
            var button = new ContainerButton { StyleClasses = { "button" }, ToggleMode = true, Pressed = chat.Key == _selected, HorizontalExpand = true, ToolTip = chat.Title };
            var top = new BoxContainer { SeparationOverride = 3, Children = { new Label { Text = (chat.Group ? "# " : "") + chat.Title, ClipText = true, HorizontalExpand = true, StyleClasses = { "pda-caption" } } } };
            if (chat.Unread > 0) top.AddChild(new Label { Text = chat.Unread.ToString(), StyleClasses = { "pda-positive" } });
            button.AddChild(new BoxContainer { Orientation = LayoutOrientation.Vertical, Margin = new Thickness(3), SeparationOverride = 2, Children = { top, new Label { Text = chat.Preview.Replace('\n', ' '), ClipText = true, StyleClasses = { "terminal-muted" } } } });
            button.OnPressed += _ => Send?.Invoke(new PdaMessengerRequestMessage(chat.Key)); _chatList.AddChild(button);
        }
        if (_chatList.ChildCount == 0) _chatList.AddChild(PdaAppUi.Empty(Loc.GetString("pda-orbit-no-chats"), Loc.GetString("pda-orbit-new-hint")));
    }
    private void BuildMessages()
    {
        _messages.RemoveAllChildren();
        if (_state.Messages.Length == 0) _messages.AddChild(PdaAppUi.Empty(Loc.GetString(_selected.Length == 0 ? "pda-orbit-select-chat" : "pda-app-start-chat"), Loc.GetString("pda-orbit-new-hint")));
        foreach (var message in _state.Messages)
        {
            var content = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 2 };
            if (_state.Group) content.AddChild(new Label { Text = message.Name, ClipText = true, StyleClasses = { "pda-caption" } });
            if (message.ReplyTo != 0)
            {
                var original = _state.Messages.FirstOrDefault(item => item.Id == message.ReplyTo);
                content.AddChild(new Label { Text = "> " + (original?.Text.Replace('\n', ' ') ?? Loc.GetString("pda-orbit-message-missing")), ClipText = true, StyleClasses = { "terminal-muted" } });
            }
            var text = new RichTextLabel { HorizontalExpand = true }; text.SetMessage(FormattedMessage.FromUnformatted(message.Text), PdaTheme.TextColor); content.AddChild(text);
            var metadata = new BoxContainer { SeparationOverride = 4, HorizontalAlignment = HAlignment.Right };
            metadata.AddChild(new Label { Text = TimeSpan.FromSeconds(Math.Max(0, message.Seconds)).ToString(@"hh\:mm") + (message.Edited ? " · " + Loc.GetString("pda-orbit-edited") : ""), StyleClasses = { "terminal-muted" } });
            if (message.Own) metadata.AddChild(new MessageReceipt(message.Read) { MinSize = new Vector2(17, 11), VerticalAlignment = VAlignment.Center });
            content.AddChild(metadata);
            var bubble = PdaAppUi.Card(content, message.Own ? "#294A50" : "#21333E", message.Own ? "#46676C" : "#374E5B", 5);
            bubble.MaxWidth = 380; bubble.HorizontalExpand = true; bubble.HorizontalAlignment = message.Own ? HAlignment.Right : HAlignment.Left; bubble.MouseFilter = MouseFilterMode.Pass;
            bubble.OnKeyBindDown += args => { if (args.Function == EngineKeyFunctions.UIRightClick) { MessageMenu(message, args.PointerLocation.Position / UIScale); args.Handle(); } };
            _messages.AddChild(bubble);
        }
    }
    private void MessageMenu(PdaConversationMessage message, Vector2 position)
    {
        _popup?.Dispose(); _popup = new Popup { Stylesheet = Stylesheet };
        var column = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
        void Item(string label, Action action)
        {
            var button = PdaAppUi.Action(Loc.GetString(label)); button.OnPressed += _ => { _popup?.Close(); action(); }; column.AddChild(button);
        }
        Item("pda-orbit-reply", () => { _reply = message.Id; _edit = 0; RefreshComposer(); _draft.GrabKeyboardFocus(); });
        Item("pda-orbit-copy", () => IoCManager.Resolve<IClipboardManager>().SetText(message.Text));
        if (message.Own) Item("pda-orbit-edit", () => { _edit = message.Id; _reply = 0; SetDraft(message.Text); RefreshComposer(); _draft.GrabKeyboardFocus(); });
        if (message.Own || _state.Owner) Item("pda-orbit-delete", () => Request(PdaMessengerAction.Delete, message: message.Id));
        _popup.AddChild(PdaAppUi.Card(column)); UserInterfaceManager.ModalRoot.AddChild(_popup); _popup.Open(UIBox2.FromDimensions(position, new Vector2(150, 1)));
    }
    private void ShowCreate(bool group)
    {
        _form.RemoveAllChildren(); _thread.Visible = _details.Visible = false; _form.Visible = true;
        _form.AddChild(new Label { Text = Loc.GetString(group ? "pda-orbit-new-group" : "pda-orbit-new-chat"), StyleClasses = { "pda-title" } });
        var name = new LineEdit { MinHeight = 26 }; var ids = new LineEdit { MinHeight = 26 };
        if (group) _form.AddChild(PdaAppUi.Field(Loc.GetString("pda-orbit-group-name"), name));
        _form.AddChild(PdaAppUi.Field(Loc.GetString(group ? "pda-orbit-member-ids" : "pda-services-recipient-id"), ids));
        var hint = new RichTextLabel(); hint.SetMessage(Loc.GetString("pda-orbit-create-hint")); _form.AddChild(hint);
        var known = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 3 };
        foreach (var chat in _state.Chats.Where(chat => !chat.Group))
        {
            var button = PdaAppUi.Action(chat.Title + " · @" + chat.Key[2..]);
            button.OnPressed += _ => ids.Text = group && ids.Text.Length > 0 ? ids.Text + ", " + chat.Key[2..] : chat.Key[2..]; known.AddChild(button);
        }
        _form.AddChild(new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, Children = { known } });
        var create = PdaAppUi.Action(Loc.GetString(group ? "pda-orbit-create-group" : "pda-os-open"), true);
        create.OnPressed += _ => Request(group ? PdaMessengerAction.CreateGroup : PdaMessengerAction.OpenDirect, group ? name.Text : ids.Text, group ? SplitIds(ids.Text) : null);
        var cancel = PdaAppUi.Action(Loc.GetString("pda-orbit-back")); cancel.OnPressed += _ => { _form.Visible = false; _thread.Visible = true; };
        _form.AddChild(new BoxContainer { SeparationOverride = 4, Children = { create, cancel } });
        (group ? name : ids).GrabKeyboardFocus();
    }
    private static string[] SplitIds(string text) => text.Split(new[] { ',', ';', ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private void ShowDetails()
    {
        _details.RemoveAllChildren(); _thread.Visible = _form.Visible = false; _details.Visible = true;
        var back = PdaAppUi.Action(Loc.GetString("pda-orbit-back")); back.OnPressed += _ => { _details.Visible = false; _thread.Visible = true; }; _details.AddChild(back);
        var members = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 3 };
        foreach (var member in _state.Members)
        {
            var row = new BoxContainer { SeparationOverride = 3, Children = { new Label { Text = member.Name + " · @" + member.Id + (member.Owner ? " · " + Loc.GetString("pda-orbit-owner") : ""), ClipText = true, HorizontalExpand = true } } };
            if (_state.Owner && member.Id != _state.OwnId)
            {
                var remove = PdaAppUi.Action("×"); remove.ToolTip = Loc.GetString("pda-orbit-remove-member"); remove.OnPressed += _ => Request(PdaMessengerAction.RemoveMember, member.Id); row.AddChild(remove);
            }
            members.AddChild(row);
        }
        _details.AddChild(new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, Children = { members } });
        if (_state.Group && _state.Owner)
        {
            var name = new LineEdit { Text = _state.Title, HorizontalExpand = true, MinHeight = 26 };
            var rename = PdaAppUi.Action(Loc.GetString("pda-orbit-rename")); rename.OnPressed += _ => Request(PdaMessengerAction.RenameGroup, name.Text);
            _details.AddChild(new BoxContainer { SeparationOverride = 3, Children = { name, rename } });
            var ids = new LineEdit { PlaceHolder = Loc.GetString("pda-orbit-member-ids"), HorizontalExpand = true, MinHeight = 26 };
            var add = PdaAppUi.Action("+"); add.OnPressed += _ => Request(PdaMessengerAction.AddMembers, members: SplitIds(ids.Text));
            _details.AddChild(new BoxContainer { SeparationOverride = 3, Children = { ids, add } });
        }
        var action = PdaAppUi.Action(Loc.GetString(_state.Group ? "pda-orbit-leave" : _state.Blocked ? "pda-max-unblock" : "pda-max-block"));
        action.OnPressed += _ => Request(_state.Group ? PdaMessengerAction.LeaveGroup : PdaMessengerAction.ToggleBlock); _details.AddChild(action);
    }
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args); if (_scrollBottom > 0) { _scroll.VScroll = float.MaxValue; _scrollBottom--; }
    }
    protected override void Dispose(bool disposing) { if (disposing) _popup?.Dispose(); base.Dispose(disposing); }
}


public sealed class MessageReceipt(bool read) : Control
{
    protected override void Draw(Robust.Client.Graphics.DrawingHandleScreen handle)
    {
        var color = Color.FromHex(read ? "#9BD5D5" : "#869CA4");
        Vector2 P(float x, float y) => new(x * UIScale, y * UIScale);
        handle.DrawLine(P(1, 6), P(4, 9), color); handle.DrawLine(P(4, 9), P(11, 2), color);
        if (read) { handle.DrawLine(P(7, 8), P(8, 9), color); handle.DrawLine(P(8, 9), P(15, 2), color); }
    }
}
