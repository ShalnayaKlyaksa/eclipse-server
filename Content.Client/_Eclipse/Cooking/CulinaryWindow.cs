using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Eclipse.Cooking;
using Robust.Client.UserInterface.Controls;
using static Content.Client._Eclipse.UserInterface.WorkshopUi;

namespace Content.Client._Eclipse.Cooking;

/// <summary>A small appliance-specific window. Physical preparation and transfers happen in the world.</summary>
public sealed class CulinaryWindow : FancyWindow
{
    private readonly Label _status = Text();
    private readonly Label _volume = Text("", true);
    private readonly Label _power = Text("", true);
    private readonly GridContainer _ingredients = new() { Columns = 6, HSeparationOverride = 5, VSeparationOverride = 5 };
    private readonly OptionButton _method = new() { Name = "CulinaryMethodPicker", MinHeight = 36, ClipText = true, HorizontalExpand = true };
    private readonly BoxContainer _description = Column();
    private readonly BoxContainer _details = Column();
    private readonly BoxContainer _working = Column();
    private readonly ProgressBar _progress = Progress(Green);
    private readonly Label _time = Text("", true);
    private readonly Button _run;
    private readonly Button _insert;
    private readonly Button _serve;
    private readonly Button _showDetails;
    private CulinaryState? _state;
    public event Action<CulinaryMethod>? OnMethod;
    public event Action<CulinaryAction>? OnAction;
    public event Action<int>? OnSlot;

    public CulinaryWindow()
    {
        Title = Loc.GetString("culinary-title");
        HelpGuidebookIds = new() { "EclipseExperimentalCooking" };
        var root = Setup(this, new Vector2(620, 580), Green);
        var heading = Row(); heading.AddChild(_status); heading.AddChild(_volume); root.AddChild(heading);
        _volume.MinWidth = _volume.MaxWidth = 130; _volume.Align = Label.AlignMode.Right;
        var scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true };
        var body = Column(); scroll.AddChild(body); root.AddChild(scroll);
        var inputs = Card(Loc.GetString("culinary-ui-ingredients"), out var inputBody);
        _ingredients.Name = "CulinarySlots"; inputBody.AddChild(_ingredients);
        _insert = Button(Loc.GetString("culinary-ui-add-held"), () => OnAction?.Invoke(CulinaryAction.InsertHeld));
        inputBody.AddChild(_insert); body.AddChild(inputs);
        body.AddChild(Paragraph(Loc.GetString("culinary-ui-load-hint"), 540));
        _method.AddStyleClass("workshop-button");
        _method.OnItemSelected += args => { _method.SelectId(args.Id); OnMethod?.Invoke((CulinaryMethod) args.Id); };
        body.AddChild(_method); body.AddChild(_description); body.AddChild(_power);
        _working.AddChild(_progress); _working.AddChild(_time); body.AddChild(_working);
        _run = Button("", () => OnAction?.Invoke(_state?.Working == true ? CulinaryAction.Stop : CulinaryAction.Start), true);
        _run.Name = "CulinaryRun"; body.AddChild(_run);
        var transfer = Paragraph(Loc.GetString("culinary-ui-transfer-hint"), 540);
        transfer.ToolTip = Loc.GetString("culinary-ui-physical-help"); body.AddChild(transfer);
        _showDetails = Button(Loc.GetString("culinary-ui-show-details"), () =>
        {
            _details.Visible = !_details.Visible;
            _showDetails!.Text = Loc.GetString(_details.Visible ? "culinary-ui-hide-details" : "culinary-ui-show-details");
        });
        body.AddChild(_showDetails); _details.Name = "CulinaryProperties"; _details.Visible = false; body.AddChild(_details);
        _serve = Button(Loc.GetString("culinary-ui-serve"), () => OnAction?.Invoke(CulinaryAction.Serve), true);
        _serve.ToolTip = Loc.GetString("culinary-ui-serving-hint"); root.AddChild(_serve);
        Apply(new CulinaryState([], "", 0, CulinaryMethod.Boil, 0, false, false, false, "culinary-status-idle"));
    }

    public void Apply(CulinaryState state)
    {
        var previous = _state; _state = state;
        if (state.StationName.Length > 0) Title = state.StationName;
        _status.Text = Loc.GetString(state.Status); _status.ToolTip = _status.Text;
        _status.FontColorOverride = state.Working ? Green : state.Ready ? Amber : Ink;
        _volume.Text = $"{state.Volume:0} / {state.Capacity:0} " + Loc.GetString("culinary-ui-units");
        _power.Text = Loc.GetString("culinary-unpowered"); _power.Visible = !state.Powered && CulinaryMath.IsHot(state.Method);
        _working.Visible = state.Working; _progress.Value = Math.Clamp(state.Progress, 0, 1);
        _time.Text = Loc.GetString("workshop-time-left", ("seconds", MathF.Ceiling(state.SecondsRemaining)));
        if (previous == null || !previous.AllowedMethods.SequenceEqual(state.AllowedMethods))
        {
            _method.Clear();
            foreach (var method in state.AllowedMethods)
                _method.AddItem(Loc.GetString($"culinary-short-{method.ToString().ToLowerInvariant()}"), (int) method);
        }
        if (state.AllowedMethods.Contains(state.Method)) _method.SelectId((int) state.Method);
        _method.Visible = state.AllowedMethods.Length > 1; _method.Disabled = state.Working;
        if (previous?.Method != state.Method)
        {
            _description.RemoveAllChildren();
            _description.AddChild(Paragraph(Loc.GetString($"culinary-instruction-{state.Method.ToString().ToLowerInvariant()}"), 540));
            _method.ToolTip = Loc.GetString($"culinary-description-{state.Method.ToString().ToLowerInvariant()}");
        }
        if (previous == null || !previous.InputSlots.Select(s => (s.Index, s.Prototype, s.Name)).SequenceEqual(state.InputSlots.Select(s => (s.Index, s.Prototype, s.Name))))
        {
            _ingredients.RemoveAllChildren();
            var slots = state.InputSlots.Length < 6 ? 6 : 12;
            for (var i = 0; i < slots; i++)
            {
                var item = i < state.InputSlots.Length ? state.InputSlots[i] : null;
                _ingredients.AddChild(Item(item?.Prototype, item?.Name ?? Loc.GetString("culinary-ui-empty-slot"), () =>
                { if (item == null) OnAction?.Invoke(CulinaryAction.InsertHeld); else OnSlot?.Invoke(item.Index); }));
            }
        }
        foreach (var tile in _ingredients.Children.OfType<ContainerButton>()) tile.Disabled = state.Working;
        _insert.Disabled = state.Working;
        _run.Text = Loc.GetString(state.Working ? "culinary-action-stop" : $"culinary-short-{state.Method.ToString().ToLowerInvariant()}");
        _run.Disabled = !state.Working && (state.Volume <= 0 || state.MethodDone);
        _serve.Visible = state.Ready && !state.Working;
        if (previous == null || !previous.PropertyValues.Select(p => (p.Property, p.Value)).SequenceEqual(state.PropertyValues.Select(p => (p.Property, p.Value)))
            || !previous.History.SequenceEqual(state.History) || !previous.Ingredients.SequenceEqual(state.Ingredients))
        {
            _details.RemoveAllChildren();
            foreach (var ingredient in state.Ingredients) _details.AddChild(Text(ingredient, true));
            foreach (var method in state.History) _details.AddChild(Text("✓ " + Loc.GetString($"culinary-short-{method.ToLowerInvariant()}"), true));
            _details.AddChild(Paragraph(Loc.GetString("culinary-ui-properties-hint"), 540));
            if (state.PropertyValues.Length == 0) _details.AddChild(Paragraph(Loc.GetString("culinary-ui-no-properties"), 540));
            foreach (var trait in state.PropertyValues)
                _details.AddChild(Text($"{Loc.GetString($"culinary-property-{trait.Property.ToString().ToLowerInvariant()}")}: {trait.Value:0.0}"));
        }
    }
}
