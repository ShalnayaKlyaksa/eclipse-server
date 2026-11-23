using System.Numerics;
using System.Globalization;
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
        _window.Command += (command, value) => SendMessage(new NuclearReactorCommandMessage(command, value));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is NuclearReactorState reactor) _window?.Update(reactor);
    }
}

public sealed class NuclearReactorWindow : ImmersiveFancyWindow
{
    public event Action<NuclearCommand, float>? Command;
    private readonly Label _status = new();
    private readonly Label _thermal = new();
    private readonly Label _coolant = new();
    private readonly Label _rods = new();
    private readonly Label _physics = new();
    private readonly Label _detail = new();
    private readonly Button[] _cells = new Button[49];
    private readonly Button _reset;
    private readonly List<Button> _rodButtons = new();
    private NuclearReactorState _state = new();
    private int _selected = 24;

    public NuclearReactorWindow()
    {
        Title = Loc.GetString("nuclear-ui-title");
        HelpGuidebookIds = new() { "EclipseNuclearReactor" };
        MinSize = new Vector2(760, 630);
        SetSize = new Vector2(860, 650);
        ((Control) this).Stylesheet = ResearchTerminalTheme.Create(IoCManager.Resolve<IUserInterfaceManager>().Stylesheet);
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10, Margin = new Thickness(12) };
        ContentsContainer.AddChild(root);
        _status.AddStyleClass("terminal-heading");
        root.AddChild(_status);
        var columns = new BoxContainer { SeparationOverride = 18, VerticalExpand = true };
        root.AddChild(columns);
        var left = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        columns.AddChild(left);
        left.AddChild(new Label { Text = Loc.GetString("nuclear-ui-core-map"), StyleClasses = { "terminal-muted" } });
        var grid = new GridContainer { Columns = 7, HSeparationOverride = 3, VSeparationOverride = 3 };
        left.AddChild(grid);
        for (var i = 0; i < 49; i++)
        {
            var index = i;
            var button = new Button { MinSize = new Vector2(46, 42), Text = "·" };
            button.OnPressed += _ => { _selected = index; UpdateDetail(); };
            _cells[i] = button;
            grid.AddChild(button);
        }
        _detail.MaxWidth = 345;
        left.AddChild(_detail);
        left.AddChild(new Label { Text = Loc.GetString("nuclear-ui-legend"), StyleClasses = { "terminal-muted" } });
        var readings = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 16, HorizontalExpand = true };
        columns.AddChild(readings);
        readings.AddChild(new Label { Text = Loc.GetString("nuclear-ui-instruments"), StyleClasses = { "terminal-heading" } });
        readings.AddChild(_thermal);
        readings.AddChild(_coolant);
        readings.AddChild(_rods);
        readings.AddChild(_physics);
        readings.AddChild(new Label { Text = Loc.GetString("nuclear-ui-rods"), StyleClasses = { "terminal-muted" } });
        foreach (var values in new[] { new[] { 0, 25, 50 }, new[] { 75, 90, 100 } })
        {
            var row = new BoxContainer { SeparationOverride = 4 };
            readings.AddChild(row);
            foreach (var value in values)
            {
                var button = new Button { Text = $"{value}%", HorizontalExpand = true };
                button.OnPressed += _ => Command?.Invoke(NuclearCommand.Rods, value / 100f);
                _rodButtons.Add(button);
                row.AddChild(button);
            }
        }
        var fine = new BoxContainer { SeparationOverride = 4 };
        readings.AddChild(fine);
        foreach (var delta in new[] { -5, -1, 1, 5 })
        {
            var button = new Button { Text = $"{delta:+0;-0}%", HorizontalExpand = true };
            button.OnPressed += _ => Command?.Invoke(NuclearCommand.Rods, Math.Clamp(_state.RodTarget + delta / 100f, 0, 1));
            _rodButtons.Add(button);
            fine.AddChild(button);
        }
        var actions = new BoxContainer { SeparationOverride = 12 };
        root.AddChild(actions);
        var scram = new Button { Text = Loc.GetString("nuclear-ui-scram"), HorizontalExpand = true, MinHeight = 44, Modulate = Color.FromHex("#E3A9A0") };
        scram.OnPressed += _ => Command?.Invoke(NuclearCommand.Scram, 1);
        actions.AddChild(scram);
        _reset = new Button { Text = Loc.GetString("nuclear-ui-reset"), HorizontalExpand = true };
        _reset.OnPressed += _ => Command?.Invoke(NuclearCommand.Reset, 0);
        actions.AddChild(_reset);
        root.AddChild(new Label { Text = Loc.GetString("nuclear-ui-heat-warning"), StyleClasses = { "terminal-muted" } });
        ResearchTerminalTheme.StyleButtons(root);
    }

    public void Update(NuclearReactorState state)
    {
        _state = state;
        _status.Text = Loc.GetString(state.Status) + (state.Faults > 0 ? $" ({state.Faults})" : "");
        _thermal.Text = Loc.GetString("nuclear-ui-thermal", ("temperature", MathF.Round(state.Temperature)),
            ("power", MathF.Round(state.Power / 1000)), ("decay", MathF.Round(state.Decay / 1000)));
        _coolant.Text = Loc.GetString("nuclear-ui-coolant", ("pressure", MathF.Round(state.Pressure)),
            ("flow", state.Flow.ToString("0.0", CultureInfo.CurrentCulture)), ("cooling", MathF.Round(state.Cooling / 1000)));
        _rods.Text = Loc.GetString("nuclear-ui-rod-position", ("actual", MathF.Round(100 * state.RodPosition)), ("target", MathF.Round(100 * state.RodTarget)));
        _physics.Text = Loc.GetString("nuclear-ui-physics", ("reactivity", (state.Reactivity * 100).ToString("0.00", CultureInfo.CurrentCulture)),
            ("xenon", state.Xenon.ToString("0.00", CultureInfo.CurrentCulture)));
        _reset.Disabled = !state.Powered || !state.Complete || !state.Scrammed || state.Temperature >= 600 || state.Flow < .01f;
        foreach (var button in _rodButtons) button.Disabled = !state.Powered || state.Scrammed;
        for (var i = 0; i < 49; i++)
        {
            var cell = i < state.Cells.Length ? state.Cells[i] : new NuclearCellState(NuclearPartKind.Missing, false, 0);
            _cells[i].Text = Symbol(cell.Kind);
            _cells[i].Modulate = !cell.Valid ? Color.FromHex("#D48178") : cell.Kind switch
            {
                NuclearPartKind.Fuel => Color.FromHex("#D5CC8A"),
                NuclearPartKind.Control => Color.FromHex("#8EBBC9"),
                NuclearPartKind.Graphite => Color.FromHex("#A4A8AA"),
                _ => Color.White,
            };
            _cells[i].ToolTip = Loc.GetString($"nuclear-part-{cell.Kind.ToString().ToLowerInvariant()}");
        }
        UpdateDetail();
    }

    private void UpdateDetail()
    {
        if (_selected >= _state.Cells.Length) { _detail.Text = ""; return; }
        var cell = _state.Cells[_selected];
        _detail.Text = $"[{_selected % 7 - 3:+0;-0;0}, {3 - _selected / 7:+0;-0;0}] " +
            Loc.GetString($"nuclear-part-{cell.Kind.ToString().ToLowerInvariant()}");
        if (cell.Kind == NuclearPartKind.Fuel) _detail.Text += "\n" + Loc.GetString("nuclear-fuel-examine", ("percent", MathF.Round(cell.Fuel * 100)));
    }

    private static string Symbol(NuclearPartKind kind) => kind switch
    {
        NuclearPartKind.Shield => "#", NuclearPartKind.Fuel => "F", NuclearPartKind.Graphite => "G",
        NuclearPartKind.Control => "R", NuclearPartKind.Inlet => ">", NuclearPartKind.Outlet => ">",
        NuclearPartKind.Console => "C", NuclearPartKind.Core => "O", NuclearPartKind.Conflict => "!", _ => "+",
    };
}
