using System.Linq;
using Content.Client.PDA;
using Content.Shared.CartridgeLoader.Cartridges;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Utility;

namespace Content.Client.CartridgeLoader.Cartridges;

/// <summary>Inline editor. Each task keeps its draft when the user selects another task.</summary>
public sealed class NanoTaskItemEditor : BoxContainer
{
    private readonly TextEdit _description = new() { MinHeight = 80, HorizontalExpand = true, VerticalExpand = true, Margin = new Thickness(3) };
    private readonly LineEdit _requester = new() { MinHeight = 26 };
    private readonly CheckBox _done = new() { Text = Loc.GetString("nano-task-ui-done") };
    private readonly ButtonGroup _priorities = new();
    private readonly Button[] _priorityButtons;
    private readonly Button _save = PdaAppUi.Action(Loc.GetString("nano-task-ui-save"), true);
    private readonly Button _delete = PdaAppUi.Action(Loc.GetString("nano-task-ui-delete"));
    private readonly Button _print = PdaAppUi.Action(Loc.GetString("nano-task-ui-print"));
    private readonly Label _status = new() { ClipText = true, StyleClasses = { "terminal-muted" } };
    private readonly Dictionary<int, NanoTaskItem> _drafts = new();
    private int? _id;
    private bool _confirmDelete;
    private NanoTaskItem? _creating;
    public Action<int?, NanoTaskItem>? Saved;
    public Action<int>? Deleted;
    public Action<NanoTaskItem>? Printed;
    private int DraftKey => _id ?? -1;
    public int? EditingId => _id;

    public NanoTaskItemEditor()
    {
        Orientation = LayoutOrientation.Vertical; SeparationOverride = 4; HorizontalExpand = VerticalExpand = true;
        AddChild(new Label { Text = Loc.GetString("nano-task-ui-description-label"), StyleClasses = { "pda-caption" } });
        AddChild(new PanelContainer { StyleClasses = { "pda-editor" }, HorizontalExpand = true, VerticalExpand = true, Children = { _description } });
        AddChild(PdaAppUi.Field(Loc.GetString("nano-task-ui-requester-label"), _requester));
        var priorityRow = new BoxContainer { SeparationOverride = 3 };
        _priorityButtons = new[] { "high", "medium", "low" }.Select(key =>
        {
            var button = PdaAppUi.Action(Loc.GetString("nano-task-ui-priority-" + key));
            button.Group = _priorities; button.ToggleMode = true; button.HorizontalExpand = true;
            button.OnPressed += _ => Changed(); priorityRow.AddChild(button); return button;
        }).ToArray();
        AddChild(priorityRow); AddChild(_done); AddChild(_status);
        AddChild(new PanelContainer { MinHeight = 1, StyleClasses = { "pda-divider" } });
        AddChild(new BoxContainer { SeparationOverride = 4, Children = { _save, _print, _delete } });
        _save.OnPressed += _ => Save();
        _print.OnPressed += _ => { if (!_print.Disabled) Printed?.Invoke(MakeItem()); };
        _delete.OnPressed += _ =>
        {
            if (_id is not { } id) return;
            if (!_confirmDelete) { _confirmDelete = true; _delete.Text = Loc.GetString("pda-notes-confirm-delete"); return; }
            _drafts.Remove(id); Deleted?.Invoke(id); _id = null; Load(null);
        };
        _description.OnTextChanged += _ => Changed(); _requester.OnTextChanged += _ => Changed(); _done.OnPressed += _ => Changed();
        _description.OnKeyBindDown += args => { if (args.Function == EngineKeyFunctions.MultilineTextSubmit) { Save(); args.Handle(); } };
        Load(null);
    }
    public NanoTaskItem MakeItem() => new(Rope.Collapse(_description.TextRope), _requester.Text, _done.Pressed,
        (NanoTaskPriority) Math.Max(0, Array.FindIndex(_priorityButtons, button => button.Pressed)));
    private void Changed()
    {
        var item = MakeItem();
        _print.Disabled = string.IsNullOrWhiteSpace(item.Description) || !item.Validate();
        _save.Disabled = _print.Disabled || (_id == null && _creating != null);
        _status.Text = Loc.GetString("pda-tasks-editor-hint", ("count", item.Description.Length), ("max", NanoTaskItem.MaximumStringLength));
        _confirmDelete = false; _delete.Text = Loc.GetString("nano-task-ui-delete");
    }
    public void Select(int? id, NanoTaskItem? item)
    {
        if (_id == id && id != null) return;
        _drafts[DraftKey] = MakeItem(); _id = id;
        Load(_drafts.GetValueOrDefault(DraftKey) ?? item);
    }
    private void Load(NanoTaskItem? item)
    {
        _description.TextRope = Rope.Leaf.Empty; _description.CursorPosition = new TextEdit.CursorPos();
        _description.InsertAtCursor(item?.Description ?? ""); _requester.Text = item?.TaskIsFor ?? ""; _done.Pressed = item?.IsTaskDone ?? false;
        _priorityButtons[(int) (item?.Priority ?? NanoTaskPriority.Medium)].Pressed = true;
        _delete.Visible = _id != null; Changed();
    }
    public void UpdateCompletion(int id, bool done) { if (_id == id) _done.Pressed = done; }
    public void ObserveTasks(List<NanoTaskItemAndId> tasks, HashSet<int> previousIds)
    {
        if (_creating == null) return;
        var created = tasks.FirstOrDefault(task => !previousIds.Contains(task.Id) && task.Data.Description == _creating.Description &&
            task.Data.TaskIsFor == _creating.TaskIsFor && task.Data.Priority == _creating.Priority && task.Data.IsTaskDone == _creating.IsTaskDone);
        if (created != null)
        {
            _drafts.Remove(-1); _drafts[created.Id] = _creating;
            if (_id == null) { _id = created.Id; _delete.Visible = true; }
        }
        _creating = null; Changed();
    }
    private void Save()
    {
        if (_save.Disabled) return;
        var item = MakeItem(); _drafts[DraftKey] = item;
        if (_id == null) _creating = item;
        Changed(); Saved?.Invoke(_id, item);
    }
}
