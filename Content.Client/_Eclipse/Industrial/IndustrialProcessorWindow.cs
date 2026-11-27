using System.Linq;
using System.Globalization;
using System.Numerics;
using Content.Client.Stylesheets;
using Content.Client.Resources;
using Content.Client.UserInterface.Controls;
using Content.Shared._Eclipse.Industrial;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Shared.Random;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Eclipse.Industrial;

public sealed partial class IndustrialProcessorWindow : FancyWindow
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IRobustRandom _random = default!;

    private readonly Stylesheet _styles;
    private readonly Label _status;
    private readonly Label _metrics;
    private readonly PanelContainer _statusStripe;
    private readonly Button _toggle;
    private readonly BoxContainer _catalogueSidebar;
    private readonly Control _catalogueDivider;
    private readonly PanelContainer _inputCard;
    private readonly LineEdit _search;
    private readonly CheckBox _availableOnly;
    private readonly Button _automatic;
    private readonly Label _recipeCount;
    private readonly Label _catalogueCaption;
    private readonly BoxContainer _recipeList;
    private readonly RichTextLabel _emptyList;
    private readonly Label _modeCaption;
    private readonly Label _processTitle;
    private readonly RichTextLabel _description;
    private readonly Label _inputCaption;
    private readonly Label _inputCount;
    private readonly Label _outputCount;
    private readonly PanelContainer _outputCard;
    private readonly BoxContainer _machineStage;
    private readonly Label _inputArrow;
    private readonly Label _outputArrow;
    private readonly GridContainer _input;
    private readonly GridContainer _output;
    private readonly BoxContainer _cycle;
    private readonly Label _currentRecipe;
    private readonly Label _progressText;
    private readonly ProgressBar _progress;
    private readonly BoxContainer _materials;
    private readonly BoxContainer _recipeInputSection;
    private readonly BoxContainer _recipeInputs;
    private readonly BoxContainer _recipeOutputs;
    private readonly Label _resultCaption;
    private readonly Label _heatCost;
    private readonly ProcessBoard _processBoard;
    private readonly RichTextLabel _slotHelp;
    private readonly Dictionary<string, ContainerButton> _recipeButtons = new();
    private readonly Dictionary<Direction, OptionButton> _ports = new();
    private readonly List<Slot> _inputSlots = new();
    private readonly List<Slot> _outputSlots = new();
    private readonly List<MaterialCounter> _materialCounters = new();
    private IndustrialRecipeUiState[] _catalog = [];
    private IndustrialProcessorBoundUserInterfaceState? _state;
    private string? _inspectedRecipe;
    private string? _detailsId;
    private bool _detailsBuffer;
    private float _elapsed;
    private int _displayPercent = -1;
    private int _displaySeconds = -1;
    private bool _updating;

    public event Action<bool, int>? OnSlotPressed;
    public event Action<string?>? OnRecipeSelected;
    public event Action? OnToggle;
    public event Action? OnManualOperate;
    public event Action<Direction, FacePortState>? OnPortChanged;

    public IndustrialProcessorWindow(EntityUid? machine = null)
    {
        IoCManager.InjectDependencies(this);
        _styles = CreateStyles(_ui.Stylesheet, _resources);
        Title = Loc.GetString("industrial-processor-ui-title");
        MinSize = new Vector2(800, 520);
        SetSize = new Vector2(880, 660);
        HelpGuidebookIds = new() { "EclipseIndustrial" };
        FindControl<PanelContainer>("WindowHeader").PanelOverride = new StyleBoxFlat(Background);
        FindControl<Label>("WindowTitle").FontColorOverride = Ink;
        FindControl<Label>("WindowTitle").FontOverride = _resources.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 14);
        ContentsContainer.AddChild(new PanelContainer { PanelOverride = new StyleBoxFlat(Background) });
        var root = Column();
        root.Stylesheet = _styles;
        root.Margin = new Thickness(12);
        root.SeparationOverride = 12;
        ContentsContainer.AddChild(root);

        var header = Row();
        _statusStripe = new PanelContainer { MinWidth = 3, PanelOverride = new StyleBoxFlat(Muted) };
        header.AddChild(_statusStripe);
        var diagnostic = Column();
        diagnostic.SeparationOverride = 2;
        _status = new Label { Name = "MachineStatus", ClipText = true, HorizontalExpand = true };
        _metrics = new Label { ClipText = true, HorizontalExpand = true, FontColorOverride = Muted, StyleClasses = { StyleClass.FontSmall } };
        diagnostic.AddChild(_status);
        diagnostic.AddChild(_metrics);
        header.AddChild(diagnostic);
        var reference = new Button
        {
            Name = "QuickReference", Text = Loc.GetString("industrial-ui-reference"), MinHeight = 34,
            VerticalAlignment = VAlignment.Center, ModulateSelfOverride = Color.White,
            StyleClasses = { "industrial-control" },
        };
        reference.OnPressed += _ => Help();
        header.AddChild(reference);
        _toggle = new Button
        {
            Name = "PauseButton", MinWidth = 118, MinHeight = 34, VerticalAlignment = VAlignment.Center,
            ModulateSelfOverride = Color.White, StyleClasses = { "industrial-control" },
        };
        _toggle.OnPressed += _ =>
        {
            if (_state is { IsManual: true, Enabled: true }) OnManualOperate?.Invoke();
            else OnToggle?.Invoke();
        };
        header.AddChild(_toggle);
        root.AddChild(header);
        root.AddChild(Divider());

        var body = Row();
        body.VerticalExpand = true;
        body.SeparationOverride = 14;
        root.AddChild(body);
        var sidebar = _catalogueSidebar = Column();
        sidebar.MinWidth = sidebar.MaxWidth = 240;
        sidebar.HorizontalExpand = false;
        sidebar.VerticalExpand = true;
        var catalogueHeading = Row();
        _catalogueCaption = Caption("industrial-ui-catalogue");
        _catalogueCaption.HorizontalExpand = true;
        catalogueHeading.AddChild(_catalogueCaption);
        _recipeCount = new Label { FontColorOverride = Muted, StyleClasses = { StyleClass.FontSmall } };
        catalogueHeading.AddChild(_recipeCount);
        sidebar.AddChild(catalogueHeading);
        var recipesScroll = new ScrollContainer { Name = "RecipeScroll", HScrollEnabled = false, VerticalExpand = true };
        void FilterFromStart()
        {
            FilterRecipes();
            recipesScroll.SetScrollValue(Vector2.Zero);
        }

        _search = new LineEdit
        {
            Name = "RecipeSearch", PlaceHolder = Loc.GetString("industrial-ui-search"), MinHeight = 32,
            StyleClasses = { "industrial-search" },
        };
        _search.OnTextChanged += _ => FilterFromStart();
        sidebar.AddChild(_search);
        _availableOnly = new CheckBox
        {
            Name = "AvailableRecipesOnly", Text = Loc.GetString("industrial-ui-available-only"),
            HorizontalAlignment = HAlignment.Left,
        };
        _availableOnly.OnToggled += _ => FilterFromStart();
        sidebar.AddChild(_availableOnly);
        _automatic = new Button
        {
            Name = "AutomaticRecipe", ToggleMode = true, ClipText = true, MinHeight = 34,
            ModulateSelfOverride = Color.White, StyleClasses = { "industrial-control" },
        };
        _automatic.OnPressed += _ =>
        {
            _inspectedRecipe = string.Empty;
            OnRecipeSelected?.Invoke(null);
            RefreshRecipeDetails();
        };
        sidebar.AddChild(_automatic);
        _recipeList = Column();
        _recipeList.SeparationOverride = 2;
        _emptyList = Hint("industrial-ui-no-recipes");
        _recipeList.AddChild(_emptyList);
        recipesScroll.AddChild(_recipeList);
        sidebar.AddChild(recipesScroll);
        body.AddChild(sidebar);
        _catalogueDivider = Divider(true);
        body.AddChild(_catalogueDivider);

        var inspector = Column();
        inspector.VerticalExpand = true;
        body.AddChild(inspector);
        var processScroll = new ScrollContainer
        {
            Name = "ProductionScroll", HScrollEnabled = false, VerticalExpand = true, ReserveScrollbarSpace = true,
        };
        var process = Column();
        process.SeparationOverride = 10;
        process.Margin = new Thickness(0, 0, 8, 0);
        _modeCaption = Caption("industrial-ui-process");
        _processTitle = new Label { Name = "ProcessTitle", ClipText = true, HorizontalExpand = true, FontColorOverride = Ink, StyleClasses = { "industrial-heading" } };
        process.AddChild(_modeCaption);
        process.AddChild(_processTitle);
        _description = Hint("industrial-ui-automatic-help");
        process.AddChild(_description);

        var board = _processBoard = new ProcessBoard(_random.NextAngle().ToVec() * 6) { Name = "ProcessBoard", HorizontalExpand = true };
        var flow = Row();
        flow.SeparationOverride = 4;
        flow.Margin = new Thickness(8, 14, 8, 14);
        var input = Column();
        (_inputCaption, _inputCount) = BufferHeading(input, "industrial-ui-input");
        _input = SlotGrid();
        input.AddChild(_input);
        var inputCard = _inputCard = TransparentPanel(input);
        inputCard.Name = "InputBuffer";
        flow.AddChild(inputCard);
        _inputArrow = FlowArrow();
        flow.AddChild(_inputArrow);
        _machineStage = Column();
        _machineStage.MinWidth = _machineStage.MaxWidth = 64;
        _machineStage.HorizontalExpand = false;
        _machineStage.VerticalAlignment = VAlignment.Center;
        var sprite = new SpriteView(machine, _entities)
        {
            SetSize = new Vector2(64, 64), Stretch = SpriteView.StretchMode.Fill,
            HorizontalAlignment = HAlignment.Center, OverrideDirection = Direction.South,
        };
        _machineStage.AddChild(sprite);
        _machineStage.AddChild(new Label
        {
            Text = Loc.GetString("industrial-ui-machine"), Align = Label.AlignMode.Center,
            FontColorOverride = Muted, StyleClasses = { StyleClass.FontSmall },
        });
        flow.AddChild(_machineStage);
        _outputArrow = FlowArrow();
        flow.AddChild(_outputArrow);
        var output = Column();
        (_, _outputCount) = BufferHeading(output, "industrial-ui-output");
        _output = SlotGrid();
        output.AddChild(_output);
        _outputCard = TransparentPanel(output);
        _outputCard.Name = "OutputBuffer";
        flow.AddChild(_outputCard);
        board.AddChild(flow);
        process.AddChild(board);

        _cycle = Column();
        var cycleHeader = Row();
        _currentRecipe = new Label { Name = "CurrentRecipe", ClipText = true, HorizontalExpand = true, FontColorOverride = Muted, StyleClasses = { StyleClass.FontSmall } };
        _progressText = new Label { FontColorOverride = Amber, StyleClasses = { "industrial-number" } };
        cycleHeader.AddChild(_currentRecipe);
        cycleHeader.AddChild(_progressText);
        _cycle.AddChild(cycleHeader);
        _progress = new ProgressBar
        {
            Name = "CycleProgress", MinHeight = 7, MaxValue = 1, HorizontalExpand = true,
            BackgroundStyleBoxOverride = new StyleBoxFlat(Color.FromHex("#111519")),
            ForegroundStyleBoxOverride = new StyleBoxFlat(Amber),
        };
        _cycle.AddChild(_progress);
        process.AddChild(_cycle);
        var slotHelp = _slotHelp = Hint("industrial-ui-slot-help");
        slotHelp.AddStyleClass(StyleClass.FontSmall);
        process.AddChild(slotHelp);

        _materials = Row();
        _recipeInputSection = Column();
        _recipeInputSection.AddChild(Caption("industrial-ui-per-cycle"));
        _recipeInputs = Column();
        _recipeInputSection.AddChild(_recipeInputs);
        _materials.AddChild(_recipeInputSection);
        var results = Column();
        _resultCaption = Caption("industrial-ui-expected-output");
        results.AddChild(_resultCaption);
        _recipeOutputs = Column();
        results.AddChild(_recipeOutputs);
        _materials.AddChild(results);
        process.AddChild(_materials);
        _heatCost = new Label { ClipText = true, FontColorOverride = Muted, StyleClasses = { StyleClass.FontSmall } };
        process.AddChild(_heatCost);
        process.AddChild(CreateFluidPanel());
        processScroll.AddChild(process);
        inspector.AddChild(processScroll);
        inspector.AddChild(Divider());

        var portsHeading = Row();
        var portsCaption = Caption("industrial-ui-connections-tab");
        portsCaption.HorizontalExpand = true;
        portsHeading.AddChild(portsCaption);
        var keyHint = new Label { Text = Loc.GetString("industrial-ui-port-key"), FontColorOverride = Muted, StyleClasses = { StyleClass.FontSmall } };
        keyHint.ToolTip = Loc.GetString("industrial-ui-port-help");
        portsHeading.AddChild(keyHint);
        inspector.AddChild(portsHeading);
        var ports = Row();
        ports.Name = "MachineConnections";
        foreach (var face in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
            AddPort(ports, face);
        inspector.AddChild(ports);
    }

    public void UpdateState(IndustrialProcessorBoundUserInterfaceState state)
    {
        if (_state == null && Math.Max(state.MaxInputSlots, state.MaxOutputSlots) > 8)
        {
            var height = _ui.RootControl.Height > MinHeight ? Math.Min(740, _ui.RootControl.Height - 20) : 740;
            SetSize = new Vector2(880, height);
        }
        if (_state != null && _state.SelectedRecipeId != state.SelectedRecipeId)
            _inspectedRecipe = null;
        _state = state;
        _processBoard.Running = state.StateKey == "industrial-processor-working";
        _elapsed = 0;
        _updating = true;
        Title = state.MachineName;
        var statusColor = state.StateKey == "industrial-processor-working" ? Ready :
            state.StateKey == "industrial-processor-idle" ? Muted : Amber;
        _status.Text = Loc.GetString(state.StateKey);
        _status.ToolTip = _status.Text;
        _status.FontColorOverride = statusColor;
        ((StyleBoxFlat) _statusStripe.PanelOverride!).BackgroundColor = statusColor;
        var energy = state.UsesHeat
            ? Loc.GetString("industrial-ui-temperature", ("current", (int) state.Temperature), ("required", (int) state.RequiredTemperature))
            : state.SuppliedPowerWatts is { } supplied
                ? Loc.GetString("industrial-ui-power-supply", ("received", (int) supplied), ("required", (int) state.PowerWatts))
                : Loc.GetString("industrial-ui-power", ("watts", (int) state.PowerWatts));
        _metrics.Text = $"{state.TierName}   ·   ×{state.SpeedMultiplier:0.##}   ·   {energy}";
        _metrics.ToolTip = _metrics.Text;
        _toggle.Text = Loc.GetString(state switch
        {
            { IsExtractor: true, Enabled: true } => "industrial-extraction-stop",
            { IsExtractor: true } => "industrial-extraction-start",
            { IsManual: true, Enabled: true } => "industrial-manual-operate",
            { Enabled: true } => "industrial-ui-pause",
            _ => "industrial-ui-resume",
        });
        _toggle.Disabled = state.ManualBusy;
        _toggle.Visible = _outputCard.Visible = _machineStage.Visible = _inputArrow.Visible = _outputArrow.Visible = !state.IsBuffer;
        _catalogueSidebar.Visible = _catalogueDivider.Visible = !state.IsExtractor;
        _availableOnly.Visible = !state.IsBuffer;
        _inputCard.Visible = _inputArrow.Visible = !state.IsExtractor;
        _automatic.Text = Loc.GetString(state.IsBuffer ? "industrial-ui-no-filter" : "industrial-ui-automatic");
        _automatic.ToolTip = Loc.GetString(state.IsBuffer ? "industrial-ui-buffer-help" : "industrial-ui-automatic-help");
        _modeCaption.Text = Loc.GetString(state.IsBuffer ? "industrial-ui-filter" : "industrial-ui-process");
        _catalogueCaption.Text = Loc.GetString(state.IsBuffer ? "industrial-ui-filters" : "industrial-ui-catalogue");
        _resultCaption.Text = Loc.GetString(state.IsBuffer ? "industrial-ui-accepted" : "industrial-ui-expected-output");
        _inputCaption.Text = Loc.GetString(state.IsBuffer ? "industrial-ui-stored" : "industrial-ui-input");
        _inputCount.Text = $"{state.InputSlots.Count(s => s.PrototypeId != null)} / {state.MaxInputSlots}";
        _outputCount.Text = $"{state.OutputSlots.Count(s => s.PrototypeId != null)} / {state.MaxOutputSlots}";
        _input.Columns = state.IsBuffer ? Math.Clamp(state.MaxInputSlots, 1, 8) : state.MaxInputSlots > 4 ? 4 : 2;
        _output.Columns = state.MaxOutputSlots > 4 ? 4 : 2;
        UpdateSlots(_input, _inputSlots, state.InputSlots, true);
        UpdateSlots(_output, _outputSlots, state.OutputSlots, false);
        UpdateFluidTanks(state);
        UpdateCatalogue(state.Recipes);
        RefreshRecipeDetails();
        foreach (var (face, button) in _ports)
        {
            var mode = face switch
            {
                Direction.North => state.NorthFacePort, Direction.South => state.SouthFacePort,
                Direction.East => state.EastFacePort, _ => state.WestFacePort,
            };
            button.SetItemDisabled(3, !state.UsesHeat);
            button.SetItemDisabled(1, state.MaxInputSlots == 0);
            button.SetItemDisabled(2, state.MaxOutputSlots == 0);
            button.SetItemDisabled(4, !state.UsesFluids);
            button.SetItemDisabled(5, !state.UsesFluids);
            button.TrySelectId(mode switch { FacePortState.HeatInput => 3, FacePortState.LiquidInput => 4, FacePortState.LiquidOutput => 5, _ => (int) mode });
            button.ToolTip = SharedIndustrialProcessorSystem.GetDirectionName(face) + ": " + SharedIndustrialProcessorSystem.GetFacePortName(mode);
        }
        _updating = false;
        UpdateProgress();
    }

    private void UpdateCatalogue(IndustrialRecipeUiState[] recipes)
    {
        var changed = _catalog.Length != recipes.Length || !_catalog.Zip(recipes).All(p =>
            p.First.Id == p.Second.Id && p.First.Name == p.Second.Name && p.First.Unlocked == p.Second.Unlocked && p.First.Seconds == p.Second.Seconds);
        _catalog = recipes;
        if (!changed)
            return;
        foreach (var button in _recipeButtons.Values)
            button.Orphan();
        _recipeButtons.Clear();
        _detailsId = null;
        foreach (var recipe in recipes.OrderByDescending(r => r.Unlocked).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var button = new ContainerButton
            {
                Name = "Recipe-" + recipe.Id, ToggleMode = true, HorizontalExpand = true, MinHeight = 50,
                StyleClasses = { "industrial-recipe" }, ModulateSelfOverride = Color.White,
                ToolTip = recipe.Name + "\n" + Loc.GetString("industrial-ui-duration", ("seconds", recipe.Seconds.ToString("0.#", CultureInfo.CurrentCulture))) + "\n" +
                    string.Join(" + ", recipe.Outputs.Select(o => $"{o.Count} × {o.DisplayName}")) +
                    (recipe.Unlocked ? "" : "\n" + Loc.GetString("industrial-processor-unresearched")),
            };
            var row = new Control { HorizontalExpand = true, MinHeight = 40 };
            var icon = ItemIcon(recipe.Outputs.FirstOrDefault()?.PrototypeId ?? recipe.Inputs.FirstOrDefault()?.PrototypeId, 26);
            icon.HorizontalAlignment = HAlignment.Center;
            var iconFrame = new PanelContainer
            {
                MinSize = new Vector2(32), MaxSize = new Vector2(32), VerticalAlignment = VAlignment.Center, HorizontalAlignment = HAlignment.Left,
                PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#252B31"), BorderColor = Border, BorderThickness = new Thickness(1) },
            };
            iconFrame.AddChild(icon);
            if (recipe.Outputs.Length == 0 && recipe.FluidOutputs.FirstOrDefault() is { } liquid)
            {
                icon.Visible = false;
                iconFrame.AddChild(FluidSwatch(liquid.Color, 12));
            }
            row.AddChild(iconFrame);
            var name = new RichTextLabel
            {
                HorizontalExpand = true, MaxHeight = 40, RectClipContent = true,
                Margin = new Thickness(38, 0, 44, 0), StyleClasses = { "industrial-recipe-name" },
            };
            name.SetMessage(FormattedMessage.FromUnformatted(SentenceCase(recipe.Name)), recipe.Unlocked ? Ink : Muted);
            row.AddChild(name);
            var metadata = Column();
            metadata.HorizontalExpand = false;
            metadata.HorizontalAlignment = HAlignment.Right;
            metadata.VerticalAlignment = VAlignment.Center;
            metadata.MinWidth = metadata.MaxWidth = 38;
            metadata.SeparationOverride = 1;
            metadata.AddChild(new Label
            {
                Text = Loc.GetString(recipe.Unlocked ? "industrial-ui-duration-short" : "industrial-ui-unavailable-short", ("seconds", recipe.Seconds.ToString("0.#", CultureInfo.CurrentCulture))),
                ClipText = true,
                FontColorOverride = recipe.Unlocked ? Muted : Amber, Align = Label.AlignMode.Right, StyleClasses = { StyleClass.FontSmall },
            });
            var extra = recipe.Outputs.Skip(1).Sum(o => o.Count);
            metadata.AddChild(new Label
            {
                Text = recipe.Outputs.Length == 0 ? $"{recipe.FluidOutputs.Sum(f => f.Amount):0.#} u" : extra > 0 ? $"×{recipe.Outputs[0].Count}+{extra}" : $"×{recipe.Outputs[0].Count}",
                ClipText = true,
                Align = Label.AlignMode.Right, FontColorOverride = Muted, StyleClasses = { StyleClass.FontSmall },
            });
            row.AddChild(metadata);
            button.AddChild(row);
            button.OnPressed += _ =>
            {
                _inspectedRecipe = recipe.Id;
                if (recipe.Unlocked)
                    OnRecipeSelected?.Invoke(recipe.Id);
                RefreshRecipeDetails();
            };
            _recipeList.AddChild(button);
            _recipeButtons.Add(recipe.Id, button);
        }
        FilterRecipes();
    }

    private void FilterRecipes()
    {
        var search = _search.Text.Trim();
        var availableOnly = _availableOnly.Visible && _availableOnly.Pressed;
        var visible = 0;
        foreach (var recipe in _catalog)
        {
            var button = _recipeButtons[recipe.Id];
            button.Visible = (!availableOnly || recipe.Unlocked) && (
                recipe.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                recipe.Inputs.Any(i => i.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase)) ||
                recipe.Outputs.Any(i => i.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase)) ||
                recipe.FluidInputs.Concat(recipe.FluidOutputs).Concat(recipe.Bath)
                    .Any(i => i.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)));
            if (button.Visible)
                visible++;
        }
        _recipeCount.Text = $"{visible} / {_catalog.Length}";
        _emptyList.Visible = visible == 0;
        if (_emptyList.Visible)
        {
            var message = availableOnly && _catalog.Any(r => !r.Unlocked)
                ? "industrial-ui-no-available-recipes"
                : "industrial-ui-no-recipes";
            _emptyList.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(message)), Muted);
        }
    }

    private string? GetRecipeRequirements(IndustrialRecipeUiState recipe)
    {
        if (!_prototypes.TryIndex<IndustrialRecipePrototype>(recipe.Id, out var prototype))
            return recipe.Unlocked ? null : Loc.GetString("industrial-processor-unresearched");

        var requirements = new List<string>();
        if (prototype.MinimumTier > MachineTier.Basic)
        {
            var tier = prototype.MinimumTier == MachineTier.Perfect ? "industrial-machine-tier-perfect" : "industrial-machine-tier-industrial";
            requirements.Add(Loc.GetString("industrial-processor-requires-tier", ("tier", Loc.GetString(tier))));
        }

        if (prototype.RequiredTechnology is { } technology)
        {
            var disk = prototype.RequireDataDisk || technology.Id.StartsWith("Industrial", StringComparison.Ordinal);
            requirements.Add(Loc.GetString(disk ? "industrial-recipe-requires-disk" : "industrial-processor-requires-research",
                ("technology", Loc.GetString(_prototypes.Index(technology).Name))));
        }

        if (prototype.CleanroomGrade > 0)
            requirements.Add(Loc.GetString("industrial-recipe-requires-cleanroom", ("grade", prototype.CleanroomGrade)));

        return requirements.Count > 0 ? string.Join('\n', requirements) : null;
    }

    private void RefreshRecipeDetails()
    {
        if (_state == null)
            return;
        var selected = _inspectedRecipe ?? _state.SelectedRecipeId;
        if (selected == string.Empty)
            selected = null;
        foreach (var (id, button) in _recipeButtons)
            button.Pressed = id == selected;
        _automatic.Pressed = selected == null;
        var recipe = _catalog.FirstOrDefault(r => r.Id == (selected ?? _state.CurrentRecipeId));
        _processBoard.Visible = _slotHelp.Visible = (_state.MaxInputSlots > 0 || _state.MaxOutputSlots > 0) &&
            (recipe == null || recipe.Inputs.Length > 0 || recipe.Outputs.Length > 0);
        _processTitle.Text = SentenceCase(recipe?.Name ?? Loc.GetString(_state.IsBuffer ? "industrial-ui-no-filter" : "industrial-ui-automatic"));
        if (_state.IsExtractor)
            _processTitle.Text = Loc.GetString("industrial-extraction-title");
        _processTitle.ToolTip = _processTitle.Text;
        var requirements = recipe != null && !_state.IsBuffer ? GetRecipeRequirements(recipe) : null;
        _description.Visible = recipe == null || requirements != null;
        _description.SetMessage(FormattedMessage.FromUnformatted(requirements ??
            Loc.GetString(_state.IsBuffer ? "industrial-ui-buffer-help" : "industrial-ui-automatic-help")),
            recipe is { Unlocked: false } ? Amber : Muted);
        if (_state.IsExtractor)
        {
            _description.Visible = true;
            _description.SetMessage(Loc.GetString(_state.ExtractionResource == null ? "industrial-extraction-no-vein" : "industrial-extraction-reserve",
                ("resource", _state.ExtractionResource ?? string.Empty), ("amount", _state.ExtractionReserve)));
        }
        _materials.Visible = recipe != null;
        _recipeInputSection.Visible = !_state.IsBuffer;
        _heatCost.Visible = !_state.IsBuffer && recipe is { HeatKilojoules: > 0 };
        if (_heatCost.Visible)
            _heatCost.Text = Loc.GetString("industrial-ui-cycle-heat", ("kilojoules", recipe!.HeatKilojoules));
        if (recipe?.Id != _detailsId || _detailsBuffer != _state.IsBuffer)
        {
            _detailsId = recipe?.Id;
            _detailsBuffer = _state.IsBuffer;
            _recipeInputs.RemoveAllChildren();
            _recipeOutputs.RemoveAllChildren();
            _materialCounters.Clear();
            _fluidCounters.Clear();
            if (recipe != null)
            {
                foreach (var ingredient in recipe.Inputs)
                    AddMaterial(_recipeInputs, ingredient, true);
                foreach (var product in recipe.Outputs)
                    AddMaterial(_recipeOutputs, product, false);
                foreach (var ingredient in recipe.FluidInputs)
                    AddFluidMaterial(_recipeInputs, ingredient, true, false, recipe.Bath.FirstOrDefault(b => b.Id == ingredient.Id)?.Amount ?? 0);
                foreach (var medium in recipe.Bath)
                    AddFluidMaterial(_recipeInputs, medium, true, true, recipe.FluidInputs.FirstOrDefault(b => b.Id == medium.Id)?.Amount ?? 0);
                foreach (var product in recipe.FluidOutputs)
                    AddFluidMaterial(_recipeOutputs, product, false, false, 0);
            }
        }
        foreach (var counter in _materialCounters)
        {
            var available = Available(counter.Prototype);
            counter.Amount.Text = $"{available} / {counter.Required}";
            counter.Amount.FontColorOverride = available >= counter.Required ? Ready : Amber;
        }
        UpdateFluidCounters();
    }

    private void AddMaterial(BoxContainer list, IndustrialProcessorSlotState item, bool input)
    {
        var row = Row();
        row.SeparationOverride = 5;
        row.ToolTip = _state is { IsBuffer: true } ? item.DisplayName : item.DisplayName + " ×" + item.Count;
        row.AddChild(ItemIcon(item.PrototypeId, 24));
        row.AddChild(new Label { Text = item.DisplayName, HorizontalExpand = true, ClipText = true, FontColorOverride = Ink, StyleClasses = { StyleClass.FontSmall } });
        var amount = new Label
        {
            Text = $"×{item.Count}", Visible = input || _state is not { IsBuffer: true },
            FontColorOverride = Ink, StyleClasses = { "industrial-number" },
        };
        row.AddChild(amount);
        list.AddChild(row);
        if (input)
            _materialCounters.Add(new MaterialCounter(item.PrototypeId, item.Count, amount));
    }

    private TextureRect ItemIcon(string? prototype, int size)
    {
        var icon = new TextureRect
        {
            MinSize = new Vector2(size), MaxSize = new Vector2(size), VerticalAlignment = VAlignment.Center,
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
        };
        SetIcon(icon, prototype);
        return icon;
    }

    private void SetIcon(TextureRect icon, string? prototype)
    {
        icon.Texture = null;
        if (prototype == null || !_prototypes.TryIndex<EntityPrototype>(prototype, out var proto))
            return;
        icon.Texture = _entities.System<SpriteSystem>().Frame0(proto);
        icon.Modulate = proto.TryGetComponent<SpriteComponent>(out var sprite, _entities.ComponentFactory) ? sprite.Color : Color.White;
    }

    private void UpdateSlots(GridContainer grid, List<Slot> controls, IndustrialProcessorSlotState[] states, bool input)
    {
        if (controls.Count != states.Length)
        {
            grid.RemoveAllChildren();
            controls.Clear();
            for (var i = 0; i < states.Length; i++)
            {
                var index = i;
                var button = new SlotButton { Name = $"{(input ? "Input" : "Output")}Slot{i}", IsInput = input };
                var content = new Control { Margin = new Thickness(3) };
                var icon = new TextureRect { MinSize = new Vector2(32), MaxSize = new Vector2(32), HorizontalAlignment = HAlignment.Center, VerticalAlignment = VAlignment.Center };
                var count = new Label { Margin = new Thickness(2, 0), FontColorOverride = Ink, StyleClasses = { StyleClass.FontSmall } };
                var badge = new PanelContainer
                {
                    HorizontalAlignment = HAlignment.Right, VerticalAlignment = VAlignment.Bottom,
                    PanelOverride = new StyleBoxFlat(Color.FromHex("#14171BE6")),
                };
                badge.AddChild(count);
                content.AddChild(icon);
                content.AddChild(badge);
                button.AddChild(content);
                button.OnPressed += _ => OnSlotPressed?.Invoke(input, index);
                grid.AddChild(button);
                controls.Add(new Slot(button, icon, count, badge));
            }
        }
        for (var i = 0; i < states.Length; i++)
        {
            var slot = controls[i];
            var item = states[i];
            slot.Button.Empty = item.PrototypeId == null;
            slot.Button.ToolTip = item.PrototypeId == null
                ? Loc.GetString(input ? "industrial-ui-insert-slot" : "industrial-ui-empty-slot") : $"{item.DisplayName} ×{item.Count}";
            slot.Badge.Visible = item.PrototypeId != null;
            slot.Count.Text = item.Count.ToString();
            if (slot.Prototype == item.PrototypeId)
                continue;
            SetIcon(slot.Icon, item.PrototypeId);
            slot.Prototype = item.PrototypeId;
        }
    }

    private void AddPort(BoxContainer row, Direction face)
    {
        var column = Column();
        column.SeparationOverride = 4;
        column.AddChild(new Label { Text = SharedIndustrialProcessorSystem.GetDirectionName(face), ClipText = true, FontColorOverride = Muted, StyleClasses = { StyleClass.FontSmall } });
        var button = new ClippedOptionButton
        {
            Name = $"Port{face}", HorizontalExpand = true, PrefixMargin = false, MinHeight = 30,
            StyleClasses = { "industrial-control" }, ModulateSelfOverride = Color.White,
        };
        button.OptionStyleClasses.Add("industrial-control");
        if (button.OptionsScroll.Parent is { } popup)
            popup.Stylesheet = _styles;
        button.AddItem(Loc.GetString("industrial-ui-port-off"), 0);
        button.AddItem(Loc.GetString("industrial-ui-port-input"), 1);
        button.AddItem(Loc.GetString("industrial-ui-port-output"), 2);
        button.AddItem(Loc.GetString("industrial-ui-port-heat"), 3);
        button.AddItem(Loc.GetString("industrial-ui-port-fluid-input"), 4);
        button.AddItem(Loc.GetString("industrial-ui-port-fluid-output"), 5);
        button.OnItemSelected += args =>
        {
            if (!_updating)
                OnPortChanged?.Invoke(face, args.Id switch { 3 => FacePortState.HeatInput, 4 => FacePortState.LiquidInput, 5 => FacePortState.LiquidOutput, _ => (FacePortState) args.Id });
        };
        column.AddChild(button);
        row.AddChild(column);
        _ports.Add(face, button);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _elapsed += args.DeltaSeconds;
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        if (_state == null)
            return;
        var running = _state.StateKey == "industrial-processor-working";
        var remaining = MathF.Max(0, _state.SecondsRemaining - (running ? _elapsed : 0));
        var duration = _state.Progress < 1 && _state.SecondsRemaining > 0 ? _state.SecondsRemaining / (1 - _state.Progress) : 0;
        _progress.Value = duration > 0 && running ? Math.Clamp(1 - remaining / duration, 0, 1) : _state.Progress;
        _progress.Visible = _state.CurrentRecipeName != null;
        _cycle.Visible = !_state.IsBuffer && (!_state.IsExtractor || _progress.Visible);
        _currentRecipe.Text = _state.CurrentRecipeName ?? Loc.GetString("industrial-ui-idle");
        _currentRecipe.ToolTip = _currentRecipe.Text;
        var percent = _state.CurrentRecipeName == null ? -1 : (int) (_progress.Value * 100);
        var seconds = (int) MathF.Ceiling(remaining);
        if (percent != _displayPercent || seconds != _displaySeconds || _progressText.Text == null)
        {
            _progressText.Text = percent < 0 ? "—" : Loc.GetString("industrial-ui-progress-short", ("percent", percent), ("seconds", seconds));
            _displayPercent = percent;
            _displaySeconds = seconds;
        }
        _inputArrow.FontColorOverride = _outputArrow.FontColorOverride = running ? Amber : Muted;
    }

    private int Available(string? prototype)
    {
        if (prototype == null || _state == null)
            return 0;
        var resources = _entities.System<IndustrialResourceSystem>();
        return _state.InputSlots.Where(s => s.PrototypeId != null && resources.Key(s.PrototypeId) == resources.Key(prototype)).Sum(s => s.Count);
    }

    private static BoxContainer Column() => new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 7, HorizontalExpand = true };
    private static BoxContainer Row() => new() { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 10, HorizontalExpand = true };
    private static Label Caption(string key) => new() { Text = Loc.GetString(key), ClipText = true, FontColorOverride = Muted, StyleClasses = { "industrial-caption" } };
    private static string SentenceCase(string text) => text.Length == 0 ? text : text.Substring(0, 1).ToUpperInvariant() + text.Substring(1);
    private static GridContainer SlotGrid() => new() { Columns = 2, HSeparationOverride = 4, VSeparationOverride = 4, HorizontalAlignment = HAlignment.Center };
    private static Label FlowArrow() => new() { Text = "→", MinWidth = 10, Align = Label.AlignMode.Center, FontColorOverride = Muted, VerticalAlignment = VAlignment.Center };
    private static PanelContainer TransparentPanel(Control content)
    {
        var panel = new PanelContainer { HorizontalExpand = true, PanelOverride = new StyleBoxFlat(Color.Transparent) };
        panel.AddChild(content);
        return panel;
    }
    private static PanelContainer Divider(bool vertical = false) => new()
    {
        MinWidth = vertical ? 1 : 0, MinHeight = vertical ? 0 : 1,
        PanelOverride = new StyleBoxFlat(Border),
    };
    private static (Label Caption, Label Count) BufferHeading(BoxContainer content, string key)
    {
        var row = Row();
        var caption = new Label { Text = Loc.GetString(key), ClipText = true, HorizontalExpand = true, FontColorOverride = Ink };
        var count = new Label { FontColorOverride = Muted, StyleClasses = { StyleClass.FontSmall } };
        row.AddChild(caption);
        row.AddChild(count);
        content.AddChild(row);
        return (caption, count);
    }
    private static RichTextLabel Hint(string key)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(key)), Muted);
        return label;
    }
    private sealed record Slot(SlotButton Button, TextureRect Icon, Label Count, PanelContainer Badge)
    {
        public string? Prototype;
    }
    private sealed record MaterialCounter(string? Prototype, int Required, Label Amount);
}
