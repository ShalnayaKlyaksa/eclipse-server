using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Research.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._Eclipse.Industrial;

public sealed class IndustrialDiskWriterBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private DiskWriterWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<DiskWriterWindow>();
        _window.OnWrite += selection => SendMessage(new IndustrialWriteDiskMessage(selection));
        if (State is IndustrialDiskWriterState state)
            _window.SetTechnologies(state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is IndustrialDiskWriterState disk)
            _window?.SetTechnologies(disk);
    }

    private sealed class DiskWriterWindow : FancyWindow
    {
        [Dependency] private readonly IPrototypeManager _prototypes = default!;

        private readonly LineEdit _search = new() { PlaceHolder = Loc.GetString("industrial-disk-writer-search") };
        private readonly BoxContainer _list = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        private readonly Label _count = new() { HorizontalExpand = true };
        private readonly Button _write = new() { Text = Loc.GetString("industrial-disk-writer-title"), Disabled = true };
        private readonly HashSet<ProtoId<TechnologyPrototype>> _selected = new();
        private readonly Dictionary<ProtoId<TechnologyPrototype>, CheckBox> _choices = new();

        public event Action<ProtoId<TechnologyPrototype>[]>? OnWrite;

        public DiskWriterWindow()
        {
            IoCManager.InjectDependencies(this);
            Title = Loc.GetString("industrial-disk-writer-title");
            MinSize = new Vector2(440, 300);
            SetSize = new Vector2(500, 420);

            var root = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                Margin = new Thickness(12),
                SeparationOverride = 8,
            };
            root.AddChild(new Label { Text = Loc.GetString("industrial-disk-writer-replace"), ClipText = true });
            root.AddChild(_search);
            var scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true };
            scroll.AddChild(_list);
            root.AddChild(scroll);
            var footer = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            footer.AddChild(_count);
            footer.AddChild(_write);
            root.AddChild(footer);
            ContentsContainer.AddChild(root);

            _search.OnTextChanged += _ =>
            {
                ApplySearch();
                scroll.SetScrollValue(Vector2.Zero);
            };
            _write.OnPressed += _ => OnWrite?.Invoke(_selected.ToArray());
            UpdateSelection();
        }

        public void SetTechnologies(IndustrialDiskWriterState state)
        {
            _list.RemoveAllChildren();
            _choices.Clear();
            _selected.Clear();
            if (state.Recorded.Length <= IndustrialDataDiskComponent.Capacity)
                _selected.UnionWith(state.Recorded.Intersect(state.Available));

            foreach (var (id, name) in state.Available.Select(id => (Id: id, Name: TechnologyName(id)))
                         .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var choice = new CheckBox { Text = name, ToolTip = name, Pressed = _selected.Contains(id), ClipText = true };
                choice.OnToggled += args =>
                {
                    if (args.Pressed)
                        _selected.Add(id);
                    else
                        _selected.Remove(id);
                    UpdateSelection();
                };
                _choices.Add(id, choice);
                _list.AddChild(choice);
            }

            if (_choices.Count == 0)
                _list.AddChild(new Label { Text = Loc.GetString("industrial-disk-writer-no-research") });

            ApplySearch();
            UpdateSelection();
        }

        private string TechnologyName(ProtoId<TechnologyPrototype> id)
            => _prototypes.TryIndex(id, out var technology) ? Loc.GetString(technology.Name) : id.Id;

        private void ApplySearch()
        {
            var search = _search.Text.Trim();
            foreach (var choice in _choices.Values)
                choice.Visible = choice.Text?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true;
        }

        private void UpdateSelection()
        {
            var full = _selected.Count >= IndustrialDataDiskComponent.Capacity;
            foreach (var (id, choice) in _choices)
                choice.Disabled = full && !_selected.Contains(id);
            _write.Disabled = _selected.Count == 0;
            _count.Text = Loc.GetString("industrial-disk-writer-count", ("count", _selected.Count),
                ("max", IndustrialDataDiskComponent.Capacity));
        }
    }
}
