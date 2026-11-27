using System.Globalization;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Client._Erida.Research.UI;
using Content.Shared._Eclipse.Power;
using Robust.Client.Graphics;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Eclipse.Power;

public sealed class NuclearReactorBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private NuclearReactorWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<NuclearReactorWindow>();
        _window.Command += SendMessage;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is NuclearReactorState reactor)
            _window?.Update(reactor);
    }
}

public sealed class NuclearReactorWindow : ImmersiveFancyWindow
{
    private static readonly Color Alarm = Color.FromHex("#FF9A7C");
    private static readonly Color Ready = Color.FromHex("#A0CEAE");
    private static readonly Color Cyan = Color.FromHex("#82BBC9");
    public event Action<NuclearReactorCommandMessage>? Command;

    private readonly Label _status = new();
    private readonly Label _layout = new();
    private readonly Label _checks = new();
    private readonly Label _detail = new();
    private readonly Label _thermal = new();
    private readonly Label _coolant = new();
    private readonly Label _mox = new();
    private readonly Label _physics = new();
    private readonly Label _groupSummary = new();
    private readonly Label _rods = new();
    private readonly Label _targetReadout = new();
    private readonly ProgressBar _heatBar = MakeBar();
    private readonly ProgressBar _rodBar = MakeBar();
    private readonly GridContainer _map = new() { Columns = 1, HSeparationOverride = 2, VSeparationOverride = 2 };
    private readonly List<Button> _cells = new();
    private readonly BoxContainer _assignment = new() { SeparationOverride = 4 };
    private readonly Button[] _assignmentButtons = new Button[NuclearRodGroupState.GroupCount];
    private readonly OptionButton _group = new() { HorizontalExpand = true };
    private readonly SliderIntInput _target = new() { MinValue = 0, MaxValue = 100, HorizontalExpand = true };
    private readonly Button _apply;
    private readonly Button _reset;
    private readonly Button _scram;
    private NuclearReactorState _state = new();
    private Vector2i _selected;
    private int _selectedGroup;
    private bool _editingTarget;

    public NuclearReactorWindow()
    {
        Title = Loc.GetString("nuclear-ui-title");
        HelpGuidebookIds = new() { "EclipseNuclearReactor" };
        MinSize = new Vector2(775, 560);
        SetSize = new Vector2(1050, 798);
        ((Control) this).Stylesheet = ResearchTerminalTheme.Create(IoCManager.Resolve<IUserInterfaceManager>().Stylesheet);
        var root = Vertical(12);
        root.Margin = new Thickness(14);
        ContentsContainer.AddChild(root);
        _status.AddStyleClass("terminal-heading");
        root.AddChild(_status);

        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true };
        root.AddChild(scroll);
        var columns = new BoxContainer { SeparationOverride = 22, HorizontalExpand = true };
        scroll.AddChild(columns);

        var left = Vertical(9);
        left.MinWidth = 377;
        columns.AddChild(left);
        left.AddChild(Heading("nuclear-ui-core-map"));
        left.AddChild(_layout);
        left.AddChild(_map);
        left.AddChild(_detail);
        left.AddChild(_assignment);
        _assignment.AddChild(new Label { Text = Loc.GetString("nuclear-ui-assign-group"), Margin = new Thickness(0, 0, 6, 0) });
        for (var i = 0; i < _assignmentButtons.Length; i++)
        {
            var group = i;
            var button = new Button { Text = GroupLetter(group), MinWidth = 42 };
            button.OnPressed += _ => Command?.Invoke(new(NuclearCommand.AssignGroup, group: group, cellX: _selected.X, cellY: _selected.Y));
            _assignment.AddChild(button);
            _assignmentButtons[i] = button;
        }
        left.AddChild(Heading("nuclear-ui-checks-heading"));
        left.AddChild(_checks);
        left.AddChild(new Label { Text = Loc.GetString("nuclear-ui-legend"), StyleClasses = { "terminal-muted" } });

        var right = Vertical(8);
        right.HorizontalExpand = true;
        right.MinWidth = 420;
        columns.AddChild(right);
        right.AddChild(Heading("nuclear-ui-instruments"));
        right.AddChild(_thermal);
        right.AddChild(_heatBar);
        right.AddChild(Heading("nuclear-ui-rods-heading"));
        right.AddChild(_groupSummary);
        right.AddChild(_group);
        for (var i = 0; i < NuclearRodGroupState.GroupCount; i++)
            _group.AddItem(Loc.GetString("nuclear-ui-group-name", ("group", GroupLetter(i))), i);
        _group.SelectId(0);
        _group.OnItemSelected += args =>
        {
            _group.SelectId(args.Id);
            _selectedGroup = args.Id;
            _editingTarget = false;
            UpdateGroupReadings();
        };
        right.AddChild(_rods);
        right.AddChild(_rodBar);
        right.AddChild(_targetReadout);
        right.AddChild(_target);
        _apply = new Button { Text = Loc.GetString("nuclear-ui-apply-depth"), MinHeight = 32 };
        _apply.OnPressed += _ =>
        {
            if (!_apply.Disabled)
                Command?.Invoke(new(NuclearCommand.Rods, _target.Value / 100f, _selectedGroup));
        };
        right.AddChild(_apply);
        _target.OnValueChanged += _ =>
        {
            _editingTarget = true;
            UpdateTargetReadout();
        };
        right.AddChild(Heading("nuclear-ui-cooling-heading"));
        right.AddChild(_coolant);
        right.AddChild(_mox);
        right.AddChild(_physics);

        var actions = new BoxContainer { SeparationOverride = 12 };
        root.AddChild(actions);
        _scram = new Button { Text = Loc.GetString("nuclear-ui-scram"), HorizontalExpand = true, MinHeight = 42, Modulate = Alarm };
        _scram.OnPressed += _ => Command?.Invoke(new(NuclearCommand.Scram));
        actions.AddChild(_scram);
        _reset = new Button { Text = Loc.GetString("nuclear-ui-reset"), HorizontalExpand = true };
        _reset.OnPressed += _ => Command?.Invoke(new(NuclearCommand.Reset));
        actions.AddChild(_reset);
        root.AddChild(new Label { Text = Loc.GetString("nuclear-ui-heat-warning"), StyleClasses = { "terminal-muted" } });
        ResearchTerminalTheme.StyleButtons(root);
    }

    public void Update(NuclearReactorState state)
    {
        var rebuild = state.Width != _state.Width || state.Height != _state.Height;
        _state = state;
        if (rebuild)
            BuildMap();

        _status.Text = Loc.GetString(state.Status);
        _status.FontColorOverride = state.Width == 0 || !state.Complete || !state.Powered || state.MoxHeat >= .98f
            ? Alarm : state.Scrammed ? null : Ready;
        _layout.Text = state.Width > 0
            ? Loc.GetString("nuclear-ui-layout-size", ("width", state.Width), ("height", state.Height), ("faults", state.Faults))
            : Loc.GetString("nuclear-ui-awaiting-layout");
        _checks.Text = Loc.GetString("nuclear-ui-checks", ("hull", Check(state.HullClosed)), ("ports", Check(state.PortsReady)),
            ("fuel", state.FuelHatches), ("graphite", state.GraphiteSections), ("rods", state.ControlSections), ("live", state.LiveCells));
        _thermal.Text = Loc.GetString("nuclear-ui-thermal", ("temperature", MathF.Round(state.Temperature)),
            ("power", MathF.Round(state.Power / 1000)), ("decay", MathF.Round(state.Decay / 1000)),
            ("heavyWater", MathF.Round(state.HeavyWaterLevel)), ("requiredHeavyWater", state.RequiredHeavyWater));
        _thermal.Text += "\n" + Loc.GetString("nuclear-ui-heat-limit", ("percent", Percent(state.HeatFraction)));
        SetBar(_heatBar, state.HeatFraction, state.HeatFraction >= .95f ? Alarm : Cyan);
        var flow = state.Flow.ToString("0.0", CultureInfo.CurrentCulture);
        var cooling = MathF.Round(state.Cooling / 1000);
        _coolant.Text = state.SteamCooling
            ? Loc.GetString("nuclear-ui-steam-coolant", ("pressure", MathF.Round(state.Pressure)),
                ("steam", state.SteamFlow.ToString("0.0", CultureInfo.CurrentCulture)),
                ("level", MathF.Round(state.SteamWaterLevel)), ("cooling", cooling))
            : state.WaterCooling
                ? Loc.GetString("nuclear-ui-water-coolant", ("inlet", MathF.Round(state.InletTemperature)),
                    ("outlet", MathF.Round(state.OutletTemperature)), ("flow", flow), ("cooling", cooling))
                : Loc.GetString("nuclear-ui-coolant", ("pressure", MathF.Round(state.Pressure)), ("flow", flow), ("cooling", cooling));
        _mox.Visible = state.MoxCells > 0 || state.Conditioner;
        _mox.Text = Loc.GetString("nuclear-ui-mox", ("cells", state.MoxCells), ("heat", Percent(state.MoxHeat)),
            ("mode", Loc.GetString(state.MoxBoost ? "nuclear-mox-boost" :
                state.Conditioner ? "nuclear-mox-standby" : "nuclear-mox-no-conditioner")));
        if (state.Conditioner)
            _mox.Text += "\n" + Loc.GetString("nuclear-ui-conditioner", ("cooling", MathF.Round(state.AuxiliaryCooling / 1000)),
                ("contacts", state.CoolingContacts), ("oxidizer", state.Oxidizer.ToString("0.#", CultureInfo.CurrentCulture)),
                ("solvent", state.Solvent.ToString("0.#", CultureInfo.CurrentCulture)));
        _physics.Text = Loc.GetString("nuclear-ui-physics", ("reactivity", (state.Reactivity * 100).ToString("0.00", CultureInfo.CurrentCulture)),
            ("xenon", state.Xenon.ToString("0.00", CultureInfo.CurrentCulture)));
        _scram.Disabled = state.Width == 0;
        _reset.Disabled = !state.CanReset;
        UpdateMap();
        UpdateGroupReadings();
    }

    private void BuildMap()
    {
        _map.RemoveAllChildren();
        _cells.Clear();
        _map.Columns = Math.Max(1, _state.Width);
        var size = Math.Clamp(374 / Math.Max(1, Math.Max(_state.Width, _state.Height)) - 2, 23, 46);
        for (var i = 0; i < _state.Cells.Length; i++)
        {
            var index = i;
            var button = new Button { MinSize = new Vector2(size), StyleClasses = { "terminal-control" } };
            button.OnPressed += _ =>
            {
                _selected = GridPosition(index);
                UpdateMap();
            };
            _cells.Add(button);
            _map.AddChild(button);
        }
    }

    private Vector2i GridPosition(int index) => new(_state.Left + index % _state.Width, _state.Top - index / _state.Width);

    private void UpdateMap()
    {
        NuclearCellState? selected = null;
        for (var i = 0; i < _cells.Count; i++)
        {
            var cell = _state.Cells[i];
            var button = _cells[i];
            var position = GridPosition(i);
            if (cell.Kind == NuclearPartKind.Outside)
            {
                button.Text = string.Empty;
                button.ToolTip = null;
                button.Modulate = Color.Transparent;
                button.Disabled = true;
                continue;
            }

            if (position == _selected)
                selected = cell;
            button.Disabled = false;
            button.Text = cell.RodGroup >= 0 ? GroupLetter(cell.RodGroup) : cell.FuelKind switch
            {
                NuclearFuelKind.Mox => "M",
                NuclearFuelKind.Thorium => "T",
                NuclearFuelKind.Pulse => "P",
                NuclearFuelKind.None when cell.Kind == NuclearPartKind.Fuel => "·",
                _ => Symbol(cell.Kind),
            };
            button.Modulate = !cell.Valid ? Alarm : position == _selected ? Ready : cell.FuelKind switch
            {
                NuclearFuelKind.Mox => Color.FromHex("#E6A273"),
                NuclearFuelKind.Thorium => Color.FromHex("#8CBDB2"),
                NuclearFuelKind.Pulse => Color.FromHex("#CFADDE"),
                NuclearFuelKind.Uranium => Color.FromHex("#D5CC8A"),
                _ when cell.Kind is NuclearPartKind.Control or NuclearPartKind.HeatSink => Cyan,
                _ => Color.White,
            };
            button.ToolTip = Loc.GetString($"nuclear-part-{cell.Kind.ToString().ToLowerInvariant()}");
        }

        _assignment.Visible = selected is { RodGroup: >= 0 };
        _detail.Text = selected == null ? Loc.GetString("nuclear-ui-select-tile") :
            $"[{_selected.X}, {_selected.Y}] " + Loc.GetString($"nuclear-part-{selected.Kind.ToString().ToLowerInvariant()}");
        if (selected?.Kind == NuclearPartKind.Fuel)
        {
            _detail.Text += "\n" + Loc.GetString($"nuclear-cell-{selected.FuelKind.ToString().ToLowerInvariant()}");
            if (selected.FuelKind != NuclearFuelKind.None)
                _detail.Text += " · " + Loc.GetString("nuclear-fuel-examine", ("percent", MathF.Round(selected.Fuel * 100)));
            if (selected.FuelKind == NuclearFuelKind.Pulse)
                _detail.Text += "\n" + Loc.GetString("nuclear-pulse-neighbours", ("count", selected.FuelNeighbours));
        }
        if (selected is { RodGroup: >= 0 } rod)
            for (var i = 0; i < _assignmentButtons.Length; i++)
                _assignmentButtons[i].Disabled = !_state.CanAssignGroups || rod.RodGroup == i;
    }

    private void UpdateGroupReadings()
    {
        var group = _selectedGroup < _state.RodGroups.Length ? _state.RodGroups[_selectedGroup] : default;
        var groups = new List<string>();
        for (var i = 0; i < _state.RodGroups.Length; i++)
        {
            var bank = _state.RodGroups[i];
            groups.Add($"{GroupLetter(i)}  {(bank.Count > 0 ? MathF.Round(100 * bank.Position) + "%" : "—")}");
        }
        _groupSummary.Text = string.Join("    ", groups);
        var target = (int) MathF.Round(group.Target * 100);
        if (!_editingTarget || _target.Value == target || group.Count == 0 || _state.Scrammed)
        {
            _target.Value = target;
            _editingTarget = false;
        }
        _rods.Text = group.Count > 0
            ? Loc.GetString("nuclear-ui-group-position", ("count", group.Count), ("actual", MathF.Round(100 * group.Position)), ("target", target))
            : Loc.GetString("nuclear-ui-group-empty");
        SetBar(_rodBar, group.Position, Cyan);
        UpdateTargetReadout();
    }

    private void UpdateTargetReadout()
    {
        var canMove = _selectedGroup < _state.RodGroups.Length && _state.RodGroups[_selectedGroup].Count > 0 &&
            _state.Powered && _state.Complete && !_state.Scrammed;
        _target.Visible = canMove;
        _targetReadout.Visible = canMove;
        _apply.Visible = canMove;
        _apply.Disabled = !canMove || !_editingTarget;
        _targetReadout.Text = Loc.GetString("nuclear-ui-proposed-depth", ("percent", _target.Value));
    }

    private static BoxContainer Vertical(int spacing) => new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = spacing };
    private static Label Heading(string id) => new() { Text = Loc.GetString(id), StyleClasses = { "terminal-heading" } };
    private static ProgressBar MakeBar() => new()
    {
        MinValue = 0,
        MaxValue = 1,
        MinHeight = 8,
        HorizontalExpand = true,
        BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#373C40") },
    };
    private static void SetBar(ProgressBar bar, float value, Color tint)
    {
        bar.Value = Math.Clamp(value, 0, 1);
        if (bar.ForegroundStyleBoxOverride is not StyleBoxFlat current || current.BackgroundColor != tint)
            bar.ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = tint };
    }
    private static string Check(bool ready) => Loc.GetString(ready ? "nuclear-ui-check-ready" : "nuclear-ui-check-fault");
    private static string Percent(float fraction) => (100 * MathF.Max(0, fraction)).ToString("0.0", CultureInfo.CurrentCulture);
    private static string GroupLetter(int index) => ((char) ('A' + index)).ToString();

    private static string Symbol(NuclearPartKind kind) => kind switch
    {
        NuclearPartKind.Shield => "#", NuclearPartKind.Fuel => "F", NuclearPartKind.Graphite => "G", NuclearPartKind.HeatSink => "H",
        NuclearPartKind.Control => "R", NuclearPartKind.Inlet => "‹", NuclearPartKind.Outlet => "›",
        NuclearPartKind.Core => "O", NuclearPartKind.Conflict => "!", _ => "+",
    };
}
