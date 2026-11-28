using System.Linq;
using Content.Client.UserInterface.Controls;
using Content.Shared._Eclipse.MedicalChemistry;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._Eclipse.MedicalChemistry;

public sealed partial class MedicalSynthesizerControl : BoxContainer
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly ClippedOptionButton _recipe = new() { HorizontalExpand = true };
    private readonly ClippedOptionButton _protocol = new() { HorizontalExpand = true };
    private readonly SpinBox _amount = new() { Value = 30, MinWidth = 100 };
    private readonly RichTextLabel _instructions = new();
    private readonly RichTextLabel _description = new();
    private readonly RichTextLabel _analysis = new();
    private readonly RichTextLabel _readout = new();
    private readonly RichTextLabel _status = new();
    private readonly RichTextLabel _reference = new();
    private readonly BoxContainer _amountRow = new() { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8 };
    private readonly BoxContainer _heatRow = new() { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
    private readonly BoxContainer _sampleRow = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };
    private readonly ProgressBar _progress = new() { MinValue = 0, MaxValue = 1, MinHeight = 20 };
    private readonly Button _start;
    private readonly Button _cancel;
    private readonly Button[] _heatButtons;
    private MedicalSynthesisPrototype[] _recipes;
    private MedicalSynthesizerState? _state;
    private float _sourceVolume;

    public event Action<BoundUserInterfaceMessage>? OnMessage;

    public MedicalSynthesizerControl()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = VerticalExpand = true;
        Margin = new Thickness(8);
        SeparationOverride = 8;
        var scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true };
        var body = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 12, HorizontalExpand = true };
        scroll.AddChild(body);
        AddChild(scroll);

        _instructions.SetMessage(Loc.GetString("medical-chemistry-instructions"));
        body.AddChild(_instructions);
        _recipes = _prototypes.EnumeratePrototypes<MedicalSynthesisPrototype>().Where(recipe => !recipe.Advanced).OrderBy(recipe => recipe.ID).ToArray();
        for (var i = 0; i < _recipes.Length; i++)
            _recipe.AddItem(_prototypes.Index(_recipes[i].Medicine).LocalizedName, i);
        _recipe.OnItemSelected += args => { _recipe.SelectId(args.Id); UpdateControls(); };
        body.AddChild(_recipe);

        foreach (var protocol in Enum.GetValues<MedicalSynthesisProtocol>())
        {
            if (protocol == MedicalSynthesisProtocol.Personalize)
                continue;
            _protocol.AddItem(Loc.GetString($"medical-chemistry-protocol-{protocol.ToString().ToLowerInvariant()}"), (int) protocol);
        }
        _protocol.SelectId((int) MedicalSynthesisProtocol.Precision);
        _protocol.OnItemSelected += args => { _protocol.SelectId(args.Id); UpdateControls(); };
        body.AddChild(_protocol);
        body.AddChild(_description);

        _amountRow.AddChild(new Label { Text = Loc.GetString("medical-chemistry-amount") });
        _amount.InitDefaultButtons();
        _amount.IsValid = amount => amount is >= 1 and <= MedicalSynthesisRules.MaxBatchVolume;
        _amount.ValueChanged += _ => UpdateControls();
        _amountRow.AddChild(_amount);
        body.AddChild(_amountRow);
        body.AddChild(_analysis);
        _sampleRow.AddChild(_reference);
        var sampleButton = new Button { Text = Loc.GetString("medical-chemistry-reference-slot") };
        sampleButton.OnPressed += _ => OnMessage?.Invoke(new ItemSlotButtonPressedEvent(MedicalSynthesisRules.SampleSlot));
        _sampleRow.AddChild(sampleButton);
        body.AddChild(_sampleRow);

        _start = new Button { Text = Loc.GetString("medical-chemistry-start"), MinHeight = 36 };
        _start.OnPressed += _ =>
        {
            if (_state?.AnalysisOnly == true)
                OnMessage?.Invoke(new MedicalSampleAnalyzeMessage());
            else if (_recipes.Length > 0)
                OnMessage?.Invoke(new MedicalSynthesisStartMessage(_recipes[_recipe.SelectedId].ID,
                    (MedicalSynthesisProtocol) _protocol.SelectedId, _amount.Value));
        };
        AddChild(_status);
        AddChild(_progress);
        AddChild(_readout);

        _heatButtons = Enum.GetValues<MedicalReactorHeat>().Select(heat =>
        {
            var button = new Button
            {
                Text = Loc.GetString($"medical-chemistry-heat-{heat.ToString().ToLowerInvariant()}"),
                ToggleMode = true,
                HorizontalExpand = true,
            };
            button.OnPressed += _ => OnMessage?.Invoke(new MedicalReactorHeatMessage(heat));
            _heatRow.AddChild(button);
            return button;
        }).ToArray();
        AddChild(_heatRow);
        _heatRow.ToolTip = Loc.GetString("medical-chemistry-thermal-help");
        AddChild(_start);

        _cancel = new Button { Text = Loc.GetString("medical-chemistry-cancel"), MinHeight = 32 };
        _cancel.OnPressed += _ => OnMessage?.Invoke(new MedicalSynthesisCancelMessage());
        AddChild(_cancel);
        UpdateControls();
    }

    public void Apply(MedicalSynthesizerState? state, float sourceVolume = 0)
    {
        if (state != null)
        {
            var recipes = state.Analysis.Select(entry => _prototypes.Index<MedicalSynthesisPrototype>(entry.Recipe)).ToArray();
            if (recipes.Length > 0 && !recipes.Select(recipe => recipe.ID).SequenceEqual(_recipes.Select(recipe => recipe.ID)))
            {
                var previous = _recipes.ElementAtOrDefault(_recipe.SelectedId)?.ID;
                _recipes = recipes;
                _recipe.Clear();
                for (var i = 0; i < _recipes.Length; i++)
                    _recipe.AddItem(_prototypes.Index(_recipes[i].Medicine).LocalizedName, i);
                var selected = Array.FindIndex(_recipes, recipe => recipe.ID == previous);
                _recipe.SelectId(Math.Max(0, selected));
            }
            if (state.Advanced != (_state?.Advanced ?? false))
            {
                _protocol.Clear();
                foreach (var protocol in Enum.GetValues<MedicalSynthesisProtocol>())
                {
                    if (!state.Advanced && protocol == MedicalSynthesisProtocol.Personalize)
                        continue;
                    _protocol.AddItem(Loc.GetString($"medical-chemistry-protocol-{protocol.ToString().ToLowerInvariant()}"), (int) protocol);
                }
                _protocol.SelectId((int) MedicalSynthesisProtocol.Precision);
            }
        }
        _state = state;
        _sourceVolume = sourceVolume;
        if (state?.ActiveRecipe is { } active)
        {
            var index = Array.FindIndex(_recipes, recipe => recipe.ID == active);
            if (index >= 0)
                _recipe.SelectId(index);
            _protocol.SelectId((int) state.Protocol);
        }
        UpdateControls();
    }

    private void UpdateControls()
    {
        if (_recipes.Length == 0)
            return;
        var recipe = _recipes[_recipe.SelectedId];
        var protocol = (MedicalSynthesisProtocol) _protocol.SelectedId;
        var analysis = _state?.Analysis.FirstOrDefault(entry => entry.Recipe == recipe.ID);
        var analyzer = _state?.AnalysisOnly == true;
        var running = _state?.ActiveRecipe != null || _state?.Analyzing == true;
        var powered = _state?.Powered == true;
        var finished = protocol is MedicalSynthesisProtocol.Purify or MedicalSynthesisProtocol.Personalize;
        var inputPurity = finished ? analysis?.Purity ?? 1f : analysis?.PrecursorPurity ?? 1f;
        var (purity, yield) = MedicalSynthesisRules.Result(protocol, 0, inputPurity);
        _protocol.ToolTip = Loc.GetString($"medical-chemistry-description-{protocol.ToString().ToLowerInvariant()}");
        _description.SetMessage(Loc.GetString("medical-chemistry-preview", ("seconds", MedicalSynthesisRules.Duration(protocol)),
                ("volume", Math.Round((FixedPoint2.New(_amount.Value) * yield).Float(), 2)), ("purity", purity),
                ("min", Math.Round(recipe.MinTemperature - 273.15, 2)), ("max", Math.Round(recipe.MaxTemperature - 273.15, 2))));
        _analysis.SetMessage(Loc.GetString("medical-chemistry-analysis",
            ("precursor", _prototypes.Index(recipe.Precursor).LocalizedName), ("raw", Math.Round(analysis?.Precursor.Float() ?? 0, 2)),
            ("medicine", _prototypes.Index(recipe.Medicine).LocalizedName), ("volume", Math.Round(analysis?.Medicine.Float() ?? 0, 2)),
            ("purity", MathF.Round((analysis?.Purity ?? 1f) * 100))));
        _analysis.ToolTip = Loc.GetString("medical-chemistry-purity-help");
        _recipe.Disabled = _protocol.Disabled = running;
        _amount.LineEditDisabled = running;
        _amount.SetButtonDisabled(running);
        var available = finished ? analysis?.Medicine.Float() ?? 0 : analysis?.Precursor.Float() ?? 0;
        _start.Disabled = !powered || running || available < _amount.Value
            || finished && analysis?.MixedProfiles == true
            || protocol == MedicalSynthesisProtocol.Purify && inputPurity >= 0.99f
            || protocol == MedicalSynthesisProtocol.Personalize && _state?.SampleProfile == null;
        if (finished && analysis?.MixedProfiles == true)
            _analysis.SetMessage(Loc.GetString("medical-chemistry-mixed-profiles"));
        _instructions.SetMessage(Loc.GetString(analyzer ? "medical-chemistry-analyzer-instructions" : "medical-chemistry-instructions"));
        _recipe.Visible = _protocol.Visible = _description.Visible = _amountRow.Visible = _heatRow.Visible = !analyzer;
        _sampleRow.Visible = _state?.Advanced == true && !analyzer;
        _reference.SetMessage(_state?.SampleProfile is { } profile
            ? Loc.GetString("medical-chemistry-reference-ready", ("profile", MedicalBatch.ProfileCode(profile)))
            : Loc.GetString("medical-chemistry-reference-needed"));
        _start.Text = Loc.GetString(analyzer ? "medical-chemistry-analyze" : "medical-chemistry-start");
        if (analyzer)
        {
            _start.Disabled = !powered || running || _sourceVolume < 5;
            var report = new List<string> { Loc.GetString("medical-chemistry-assay-heading") };
            foreach (var reagent in _state?.AssayResults ?? [])
            {
                var name = _prototypes.Index<ReagentPrototype>(reagent.Reagent.Prototype).LocalizedName;
                report.Add(Loc.GetString("medical-chemistry-assay-compound", ("name", name),
                    ("volume", Math.Round(reagent.Quantity.Float(), 2)), ("purity", MathF.Round(MedicalBatch.GetPurity(reagent.Reagent) * 100))));
                if (MedicalBatch.GetPatientDna(reagent.Reagent) is { } target)
                    report.Add(Loc.GetString("medical-chemistry-reference-ready", ("profile", MedicalBatch.ProfileCode(target))));
            }
            report.Add(_state?.SampleProfile is { } assayed
                ? Loc.GetString("medical-chemistry-reference-ready", ("profile", MedicalBatch.ProfileCode(assayed)))
                : Loc.GetString("medical-chemistry-assay-no-profile"));
            _analysis.SetMessage(string.Join('\n', report));
        }
        _cancel.Disabled = !running;
        var status = !powered ? "medical-chemistry-status-unpowered" : _state?.Status ?? "medical-chemistry-status-ready";
        if (powered && running && !analyzer)
            status = _state!.Temperature > recipe.MaxTemperature ? "medical-chemistry-status-overheated"
                : _state.Temperature < recipe.MinTemperature ? "medical-chemistry-status-warming" : "medical-chemistry-status-running";
        _status.SetMessage(Loc.GetString(status));
        _progress.Value = _state?.Progress ?? 0;
        _readout.SetMessage(Loc.GetString(analyzer ? "medical-chemistry-assay-readout" : "medical-chemistry-readout",
            ("temperature", Math.Round((_state?.Temperature ?? 293.15f) - 273.15, 1)),
            ("progress", MathF.Round((_state?.Progress ?? 0) * 100)),
            ("stress", MathF.Round((_state?.Stress ?? 0) * 100)), ("volume", Math.Round(_state?.Volume.Float() ?? 0, 2))));
        for (var i = 0; i < _heatButtons.Length; i++)
        {
            _heatButtons[i].Pressed = _state?.Heat == (MedicalReactorHeat) i;
            _heatButtons[i].Disabled = !running || !powered;
        }
    }
}
