using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Client.PDA;
using Content.Shared._Eclipse.WallDisplays;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._Eclipse.WallDisplays;

public sealed class EditableWallBoardBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private EditableWallBoardWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<EditableWallBoardWindow>();
        _window.Save += (text, revision, size) => SendMessage(new WallBoardSaveMessage(text, revision, size));
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is WallBoardState board) _window?.Update(board);
    }
}

public sealed class EditableWallBoardWindow : DefaultWindow
{
    public event Action<string, int, int>? Save;
    private readonly OptionButton _fontSize = new();
    private readonly TextEdit _text = new() { VerticalExpand = true, MinHeight = 190 };
    private readonly Label _status = new();
    private readonly Button _save = new() { Text = Loc.GetString("wall-board-save"), MinHeight = 32 };
    private WallBoardState _remote = new("", 0, 1024);
    private int _revision;
    private bool _dirty;
    private string Value => Rope.Collapse(_text.TextRope);

    public EditableWallBoardWindow()
    {
        Title = Loc.GetString("wall-board-title"); SetSize = new Vector2(560, 400); MinSize = new Vector2(420, 310);
        Stylesheet = PdaTheme.Create(IoCManager.Resolve<IUserInterfaceManager>().Stylesheet);
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Margin = new Thickness(10) };
        var help = new RichTextLabel(); help.SetMessage(Loc.GetString("wall-board-help")); column.AddChild(help);
        var fontRow = new BoxContainer { SeparationOverride = 8 };
        fontRow.AddChild(new Label { Text = Loc.GetString("wall-board-font-size") });
        for (var size = EditableWallBoardComponent.MinFontSize; size <= EditableWallBoardComponent.MaxFontSize; size += 2)
            _fontSize.AddItem(size.ToString(), size);
        _fontSize.SelectId(10);
        _fontSize.OnItemSelected += args => { _fontSize.SelectId(args.Id); Changed(); };
        fontRow.AddChild(_fontSize); column.AddChild(fontRow);
        column.AddChild(_text); column.AddChild(_status);
        var row = new BoxContainer { SeparationOverride = 8 };
        var reload = new Button { Text = Loc.GetString("wall-board-reload"), MinHeight = 32 };
        reload.OnPressed += _ => Load();
        _save.OnPressed += _ => Save?.Invoke(Value, _revision, _fontSize.SelectedId);
        _text.OnTextChanged += _ => Changed();
        row.AddChild(_save); row.AddChild(reload); column.AddChild(row); ContentsContainer.AddChild(column);
    }
    public void Update(WallBoardState state)
    {
        _remote = state;
        if (!_dirty || Value == state.Text && _fontSize.SelectedId == state.FontSize) Load();
        else Status();
    }
    private void Load()
    {
        _revision = _remote.Revision;
        _fontSize.SelectId(_remote.FontSize);
        _text.TextRope = Rope.Leaf.Empty; _text.CursorPosition = new TextEdit.CursorPos(); _text.InsertAtCursor(_remote.Text);
        _dirty = false; Status();
    }
    private void Changed()
    {
        _dirty = Value != _remote.Text || _fontSize.SelectedId != _remote.FontSize;
        Status();
    }
    private void Status()
    {
        _status.Text = _dirty && _revision != _remote.Revision ? Loc.GetString("wall-board-conflict")
            : Loc.GetString("wall-board-count", ("count", Value.Length), ("limit", _remote.Limit));
        _save.Disabled = !_dirty || Value.Length > _remote.Limit || _revision != _remote.Revision;
    }
}
