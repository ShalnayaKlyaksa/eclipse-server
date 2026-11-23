using System.Linq;
using System.Numerics;
using Content.Client._Eclipse.UserInterface;
using Content.Client.UserInterface.Controls;
using Content.Shared._Eclipse.Beekeeping;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Content.Client._Eclipse.UserInterface.WorkshopUi;

namespace Content.Client._Eclipse.Beekeeping;

public sealed class ApiaryWindow : FancyWindow
{
    private readonly Label _status = Text();
    private readonly Label _queen = Text();
    private readonly Label _age = Text("", true);
    private readonly Label _time = Text("", true);
    private readonly Label _frame = Text("", true);
    private readonly Label _environment = Text("", true);
    private readonly Label _outputCount = Text("", true);
    private readonly BoxContainer _parents = Row();
    private readonly BoxContainer _details = Column();
    private readonly BoxContainer _speciesList = Column();
    private readonly GridContainer _outputs = new() { Columns = 7, HSeparationOverride = 5, VSeparationOverride = 5 };
    private readonly LineEdit _search = new() { Name = "ApiarySearch", MinHeight = 32 };
    private readonly WorkshopGauge _nectar = new("", Green);
    private readonly WorkshopGauge _water = new("", Color.FromHex("#80B8D4"));
    private readonly WorkshopGauge _minerals = new("", Amber);
    private readonly ProgressBar _progress = Progress(Amber);
    private readonly Button _toggle;
    private readonly Button _harvest;
    private readonly Button _analyse;
    private readonly Button _removeFrame;
    private ApiaryState? _state;
    private string? _selected;
    private string _catalog = "";
    private string _items = "";
    private string? _detailKey;
    public TabContainer Tabs { get; }
    public event Action<ApiaryAction>? OnAction;
    public event Action<bool, int>? OnSlot;

    public ApiaryWindow()
    {
        Title = Loc.GetString("apiary-title");
        HelpGuidebookIds = new() { "EclipseBeekeeping" };
        var root = Setup(this, new Vector2(860, 700), Amber);
        root.AddChild(_status);
        var tabs = Tabs = new TabContainer { Name = "ApiaryTabs", VerticalExpand = true }; root.AddChild(tabs);
        var family = Row(); family.Margin = new Thickness(0, 10, 0, 0);
        tabs.AddChild(family); TabContainer.SetTabTitle(family, Loc.GetString("apiary-ui-colony"));
        var left = Column(); left.MinWidth = left.MaxWidth = 316;
        var leftScroll = new ScrollContainer { HScrollEnabled = false, MinWidth = 334, MaxWidth = 334, VerticalExpand = true };
        leftScroll.AddChild(left);
        var leftPane = Column(); leftPane.MinWidth = leftPane.MaxWidth = 334; leftPane.HorizontalExpand = false;
        leftPane.AddChild(leftScroll); family.AddChild(leftPane);
        var queenCard = Card(Loc.GetString("apiary-ui-family"), out var queenBody);
        var queenRow = Row(); queenRow.AddChild(Icon("EclipseBeePrincess", 44));
        var identity = Column(); identity.SeparationOverride = 3; identity.AddChild(_queen); identity.AddChild(_age); queenRow.AddChild(identity);
        queenBody.AddChild(queenRow); _parents.Name = "ApiaryParentSlots"; queenBody.AddChild(_parents);
        _analyse = Button(Loc.GetString("apiary-ui-analyse"), () => OnAction?.Invoke(ApiaryAction.Analyse)); queenBody.AddChild(_analyse);
        left.AddChild(queenCard);
        var supplies = Card(Loc.GetString("apiary-ui-supplies"), out var suppliesBody);
        _nectar = new WorkshopGauge(Loc.GetString("apiary-ui-nectar"), Green);
        _water = new WorkshopGauge(Loc.GetString("apiary-ui-water"), Color.FromHex("#80B8D4"));
        _minerals = new WorkshopGauge(Loc.GetString("apiary-ui-minerals"), Amber);
        suppliesBody.AddChild(_nectar); suppliesBody.AddChild(_water); suppliesBody.AddChild(_minerals); suppliesBody.AddChild(_environment);
        var frameRow = Row(); frameRow.AddChild(_frame);
        _removeFrame = Button(Loc.GetString("apiary-ui-remove-frame"), () => OnAction?.Invoke(ApiaryAction.RemoveFrame));
        _removeFrame.MinWidth = _removeFrame.MaxWidth = 60; _removeFrame.MinHeight = 28;
        frameRow.AddChild(_removeFrame); suppliesBody.AddChild(frameRow);
        left.AddChild(supplies);
        var insert = Button(Loc.GetString("apiary-ui-add-held"), () => OnAction?.Invoke(ApiaryAction.InsertHeld));
        insert.Name = "ApiaryInsertHeld"; leftPane.AddChild(insert);
        _toggle = Button(Loc.GetString("apiary-action-toggle"), () => OnAction?.Invoke(ApiaryAction.Toggle), true);
        _toggle.Name = "ApiaryToggle"; leftPane.AddChild(_toggle);

        var right = Column(); right.VerticalExpand = true; family.AddChild(right);
        var cycle = Card(Loc.GetString("apiary-ui-cycle"), out var cycleBody);
        cycleBody.AddChild(_progress); cycleBody.AddChild(_time); right.AddChild(cycle);
        var outputCard = Card(Loc.GetString("apiary-ui-output"), out var outputBody); outputCard.VerticalExpand = true;
        outputBody.AddChild(_outputCount);
        var products = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true };
        _outputs.Name = "ApiaryOutputSlots"; products.AddChild(_outputs); outputBody.AddChild(products);
        outputBody.AddChild(Paragraph(Loc.GetString("apiary-ui-output-hint"), 440));
        _harvest = Button(Loc.GetString("apiary-ui-harvest"), () => OnAction?.Invoke(ApiaryAction.Harvest)); outputBody.AddChild(_harvest);
        right.AddChild(outputCard);

        var catalogue = Row(); catalogue.Margin = new Thickness(0, 10, 0, 0);
        tabs.AddChild(catalogue); TabContainer.SetTabTitle(catalogue, Loc.GetString("apiary-ui-selection"));
        var list = Column(); list.MinWidth = list.MaxWidth = 220; list.HorizontalExpand = false; catalogue.AddChild(list);
        _search.PlaceHolder = Loc.GetString("apiary-ui-search"); _search.OnTextChanged += _ => BuildList(); list.AddChild(_search);
        var listScroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true };
        _speciesList.Name = "ApiarySpeciesList"; _speciesList.SeparationOverride = 3; listScroll.AddChild(_speciesList); list.AddChild(listScroll);
        var detailScroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
        _details.Margin = new Thickness(4); _details.Name = "ApiarySelectionDetails"; detailScroll.AddChild(_details); catalogue.AddChild(detailScroll);
        Apply(new ApiaryState("apiary-status-idle", "", "", "", "", 0, 0, 0, 0, 0, 0, false));
    }

    public void Apply(ApiaryState state)
    {
        _state = state;
        _status.Text = Loc.GetString(state.Status); _status.ToolTip = _status.Text;
        _status.FontColorOverride = state.Enabled && state.Status == "apiary-status-working" ? Green : Amber;
        _queen.Text = state.ActiveQueen ? state.Queen : Loc.GetString("apiary-ui-no-queen");
        _age.Text = state.ActiveQueen ? Loc.GetString("apiary-ui-age", ("generation", state.Generation), ("minutes", MathF.Round(state.LineageMinutes, 1)))
            : Loc.GetString("apiary-ui-choose-parents");
        _nectar.Apply(state.Nectar, 200); _water.Apply(state.Water, 200); _minerals.Apply(state.Minerals, 50);
        _environment.Text = Loc.GetString("apiary-ui-environment", ("temperature", MathF.Round(state.Temperature - 273.15f, 1)), ("pressure", MathF.Round(state.Pressure)));
        _frame.Text = state.Frame > 0 ? Loc.GetString("apiary-ui-frame", ("frame", Loc.GetString($"apiary-frame-{state.FrameKind.ToLowerInvariant()}")), ("cycles", state.Frame)) : Loc.GetString("apiary-ui-no-frame");
        _frame.ToolTip = _frame.Text + "\n" + Loc.GetString($"apiary-frame-description-{state.FrameKind.ToLowerInvariant()}") + "\n"
            + (state.SmokeSeconds > 0 ? Loc.GetString("apiary-ui-smoke", ("seconds", MathF.Ceiling(state.SmokeSeconds))) : Loc.GetString("apiary-ui-protection"));
        _progress.Value = Math.Clamp(state.Progress, 0, 1);
        _time.Text = state.ActiveQueen ? Loc.GetString("workshop-time-left", ("seconds", MathF.Ceiling(state.SecondsRemaining))) : Loc.GetString("apiary-ui-cycle-idle");
        _toggle.Text = Loc.GetString(state.Enabled ? "apiary-action-pause" : state.ActiveQueen ? "apiary-ui-resume" : "apiary-ui-start");
        _toggle.Disabled = !state.ActiveQueen && state.ParentSlots.Length < 2;
        _analyse.Disabled = !state.ActiveQueen && state.ParentSlots.Length == 0;
        _removeFrame.Disabled = state.ActiveQueen || state.Frame <= 0;
        _harvest.Disabled = state.OutputSlots.Length == 0;
        _outputCount.Text = Loc.GetString("apiary-ui-output-count", ("count", state.OutputSlots.Length), ("cycles", state.Cycles));
        var signature = string.Join("|", state.ParentSlots.Select(s => s.Index + s.Name)) + "/" + state.ActiveQueen
            + string.Join("|", state.OutputSlots.Select(s => s.Index + s.Name + s.Count));
        if (signature != _items)
        {
            _items = signature;
            _parents.RemoveAllChildren();
            foreach (var caste in Enum.GetValues<BeeCaste>())
            {
                var item = state.ParentSlots.FirstOrDefault(s => s.Role == caste.ToString());
                var slot = Column(); slot.HorizontalExpand = false; slot.MinWidth = 110;
                slot.AddChild(Item(item?.Prototype, item?.Name ?? Loc.GetString("apiary-ui-parent-empty"), () =>
                { if (item == null) OnAction?.Invoke(ApiaryAction.InsertHeld); else OnSlot?.Invoke(false, item.Index); }));
                slot.AddChild(Text(Loc.GetString(caste == BeeCaste.Princess ? "apiary-ui-princess" : "apiary-ui-drone"), true));
                _parents.AddChild(slot);
            }
            _outputs.RemoveAllChildren();
            foreach (var item in state.OutputSlots)
                _outputs.AddChild(Item(item.Prototype, item.Name, () => OnSlot?.Invoke(true, item.Index), item.Count));
            if (state.OutputSlots.Length == 0)
                for (var i = 0; i < 16; i++)
                {
                    var empty = Item(null, Loc.GetString("apiary-ui-output-empty"), () => { }); empty.Disabled = true; _outputs.AddChild(empty);
                }
        }
        var catalog = string.Join("|", state.Species.Select(s => s.Id));
        if (catalog != _catalog) { _catalog = catalog; _selected ??= state.Species.FirstOrDefault()?.Id; BuildList(); }
        ShowDetails();
    }

    private void BuildList()
    {
        _speciesList.RemoveAllChildren();
        if (_state == null) return;
        foreach (var species in _state.Species.Where(s => s.Name.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            var button = Button(species.Name, () => SelectSpecies(species.Id));
            button.Name = "BeeSpecies" + species.Id; button.ToggleMode = true; button.Pressed = species.Id == _selected;
            button.MinWidth = button.MaxWidth = 202; button.MinHeight = 30;
            _speciesList.AddChild(button);
        }
    }

    private void ShowDetails()
    {
        var key = _selected + "|" + _catalog;
        if (_detailKey == key) return;
        _detailKey = key;
        _details.RemoveAllChildren();
        var species = _state?.Species.FirstOrDefault(s => s.Id == _selected);
        if (species == null) { _details.AddChild(Paragraph(Loc.GetString("apiary-ui-select-species"), 470)); return; }
        _details.AddChild(Text(species.Name));
        _details.AddChild(Text(Loc.GetString("apiary-ui-branch", ("branch", Loc.GetString($"apiary-branch-{species.Branch.ToLowerInvariant()}"))), true));
        _details.AddChild(Text(Loc.GetString("apiary-ui-species-cycle", ("tier", species.Tier), ("minutes", MathF.Round(species.Seconds / 60, 1))), true));
        _details.AddChild(Text(Loc.GetString("apiary-ui-temperature-range", ("minimum", MathF.Round(species.MinTemperature - 273.15f)),
            ("maximum", MathF.Round(species.MaxTemperature - 273.15f))), true));
        var mutation = _state!.Crosses.FirstOrDefault(c => c.Result == species.Id);
        if (mutation != null)
        {
            var card = Card(Loc.GetString("apiary-ui-cross"), out var body);
            var parents = Row();
            foreach (var id in new[] { mutation.First, mutation.Second })
            {
                var parent = _state.Species.FirstOrDefault(s => s.Id == id);
                var parentButton = Button(parent?.Name ?? id, () => SelectSpecies(id));
                parents.AddChild(parentButton);
            }
            body.AddChild(parents);
            body.AddChild(Text(Loc.GetString("apiary-ui-chance", ("chance", MathF.Round(mutation.Chance * 100, 2)))));
            body.AddChild(Text(Loc.GetString("apiary-ui-requirements", ("generation", mutation.Generation), ("minutes", mutation.Minutes)), true));
            if (mutation.Minerals) body.AddChild(Text(Loc.GetString("apiary-minerals-required"), true));
            _details.AddChild(card);
        }
        else _details.AddChild(Paragraph(Loc.GetString("apiary-ui-foundation"), 470));
        var products = Card(Loc.GetString("apiary-ui-products"), out var productBody);
        var comb = Row(); comb.AddChild(Icon(species.Comb)); comb.AddChild(Text(species.CombName)); productBody.AddChild(comb);
        if (species.Byproduct != null)
        {
            var extra = Row(); extra.AddChild(Icon(species.Byproduct)); extra.AddChild(Text(species.ByproductName ?? "")); productBody.AddChild(extra);
            productBody.AddChild(Text(Loc.GetString("apiary-ui-byproduct", ("chance", MathF.Round(species.ByproductChance * 100))), true));
        }
        if (species.RareProduct != null)
        {
            var rare = Row(); rare.AddChild(Icon(species.RareProduct)); rare.AddChild(Text(species.RareName ?? "")); productBody.AddChild(rare);
            productBody.AddChild(Text(Loc.GetString("apiary-ui-rare-chance", ("chance", MathF.Round(species.RareChance * 100, 2))), true));
            productBody.AddChild(Text(Loc.GetString("apiary-ui-requirements", ("generation", species.Generation), ("minutes", species.Minutes)), true));
        }
        if (species.Tier >= 4) productBody.AddChild(Paragraph(Loc.GetString("apiary-ui-controlled"), 450));
        _details.AddChild(products);
        _details.AddChild(Paragraph(Loc.GetString("apiary-ui-selection-hint"), 470));
    }

    private void SelectSpecies(string id)
    {
        _selected = id;
        foreach (var button in _speciesList.Children.OfType<Button>()) button.Pressed = button.Name == "BeeSpecies" + id;
        ShowDetails();
    }
}
