using System.Linq;
using System.Numerics;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared._Erida.Research;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Research.Prototypes;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._Erida.Research.UI;

public sealed partial class FancyResearchConsoleMenu
{
    private Dictionary<string, TechnologyPrototype> _technologies = new();
    private readonly Dictionary<string, string[]> _researchTerms = new();
    private readonly Dictionary<string, ContainerButton> _catalogButtons = new();
    private Dictionary<string, Vector2> _nodePositions = new();
    private string? _goalTechnology;

    private void InitializeResearchBrowser()
    {
        _technologies = _prototype.EnumeratePrototypes<TechnologyPrototype>().Where(t => !t.Hidden).ToDictionary(t => t.ID);
        var terms = _technologies.Keys.ToDictionary(id => id, _ => new HashSet<string>());
        void Add(string? id, string value)
        {
            if (id != null && terms.TryGetValue(id, out var names)) names.Add(value);
        }
        void Entity(string? id, string entity)
        {
            if (_prototype.TryIndex<EntityPrototype>(entity, out var prototype)) Add(id, prototype.Name);
        }
        foreach (var technology in _technologies.Values)
        {
            Add(technology.ID, Loc.GetString(technology.Name));
            foreach (var unlock in technology.RecipeUnlocks)
                if (_prototype.TryIndex(unlock, out var recipe) && recipe.Result is { } result) Entity(technology.ID, result.Id);
        }
        foreach (var recipe in _prototype.EnumeratePrototypes<IndustrialRecipePrototype>())
        {
            var id = recipe.RequiredTechnology?.Id;
            if (recipe.Name is { } name) Add(id, Loc.GetString(name));
            foreach (var output in recipe.Outputs.Keys) Entity(id, output);
            foreach (var reagent in recipe.FluidOutputs.Keys)
                Add(id, _prototype.Index(reagent).LocalizedName);
        }
        foreach (var recipe in _prototype.EnumeratePrototypes<IndustrialFabricationRecipePrototype>())
            Entity(recipe.RequiredTechnology?.Id, recipe.Result.Id);
        foreach (var (id, names) in terms) _researchTerms[id] = names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray();
        foreach (var key in new[] { "all", "unfinished", "ready", "studied" })
            AvailabilityFilter.AddItem(Loc.GetString("research-browser-filter-" + key));
        AvailabilityFilter.OnItemSelected += args => { AvailabilityFilter.SelectId(args.Id); UpdatePanels(List); Recenter(); };
        QueueGoalButton.OnPressed += _ => { if (CurrentTech is { } goal) OnCommand?.Invoke("queue plan " + goal.Id); };
        GoalButton.OnToggled += _ => SetResearchGoal(GoalButton.Pressed ? CurrentTech?.Id : null);
    }

    public void SetResearchGoal(string? technology)
    {
        _goalTechnology = technology != null && List.ContainsKey(technology) ? technology : null;
        GoalButton.Pressed = _goalTechnology != null;
        if (_goalTechnology != null) CurrentTech = _goalTechnology;
        UpdatePanels(List);
        FitGraph();
    }

    public static bool MatchesResearchSearch(string query, IEnumerable<string> terms)
    {
        var text = string.Join('\n', terms).Replace('ё', 'е').Replace('Ё', 'Е');
        return query.Replace('ё', 'е').Replace('Ё', 'Е').Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => text.Contains(word, StringComparison.CurrentCultureIgnoreCase));
    }

    private IEnumerable<TechnologyPrototype> ResearchCandidates()
    {
        var searching = !string.IsNullOrWhiteSpace(Search.Text);
        return List.Where(pair => _technologies.ContainsKey(pair.Key))
            .Where(pair => AvailabilityFilter.SelectedId switch
            {
                1 => pair.Value != ResearchAvailability.Researched,
                2 => pair.Value is ResearchAvailability.Available or ResearchAvailability.PrereqsMet,
                3 => pair.Value == ResearchAvailability.Researched,
                _ => true,
            })
            .Select(pair => _technologies[pair.Key])
            .Where(t => searching ? MatchesResearchSearch(Search.Text, _researchTerms[t.ID].Append(t.ID))
                : (!_factoryOnly || t.ID.StartsWith("Industrial", StringComparison.Ordinal)) &&
                  (_disciplineFilter == null || t.Discipline.Id == _disciplineFilter))
            .OrderBy(t => List[t.ID] == ResearchAvailability.Available ? 0 : List[t.ID] == ResearchAvailability.PrereqsMet ? 1 :
                List[t.ID] == ResearchAvailability.Researched ? 3 : 2)
            .ThenBy(t => t.Cost).ThenBy(t => Loc.GetString(t.Name), StringComparer.CurrentCultureIgnoreCase);
    }

    private void UpdateResearchCatalog(TechnologyPrototype[] technologies)
    {
        ResearchCatalog.RemoveAllChildren();
        _catalogButtons.Clear();
        CatalogCount.Text = Loc.GetString("research-browser-count", ("count", technologies.Length), ("total", List.Count));
        foreach (var technology in technologies)
        {
            var availability = List[technology.ID];
            var state = availability switch
            {
                ResearchAvailability.Available => "available",
                ResearchAvailability.PrereqsMet => "points",
                ResearchAvailability.Researched => "studied",
                _ => "locked",
            };
            var button = new ContainerButton { ToggleMode = true, HorizontalExpand = true, Pressed = CurrentTech == technology.ID };
            button.AddStyleClass("terminal-control");
            var contents = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 3 };
            var title = new RichTextLabel { HorizontalExpand = true };
            title.SetMessage(Loc.GetString(technology.Name), defaultColor: Color.FromHex("#EEEEEE"));
            contents.AddChild(title);
            var detail = new Label { Text = Loc.GetString("research-browser-state-" + state) + $" · {technology.Cost:N0}", ClipText = true };
            detail.AddStyleClass("terminal-muted");
            contents.AddChild(detail);
            if (!string.IsNullOrWhiteSpace(Search.Text))
            {
                var match = _researchTerms[technology.ID].FirstOrDefault(term => MatchesResearchSearch(Search.Text, [term]));
                if (match != null && match != Loc.GetString(technology.Name))
                {
                    var label = new Label { Text = "> " + match, ClipText = true };
                    label.AddStyleClass("terminal-node");
                    contents.AddChild(label);
                }
            }
            button.AddChild(contents);
            button.ToolTip = string.Join('\n', _researchTerms[technology.ID]);
            button.OnPressed += _ => RevealResearch(technology.ID);
            _catalogButtons[technology.ID] = button;
            ResearchCatalog.AddChild(button);
        }
        if (technologies.Length == 0)
        {
            var empty = new RichTextLabel { HorizontalExpand = true };
            empty.SetMessage(Loc.GetString("research-browser-empty"));
            ResearchCatalog.AddChild(empty);
        }
    }

    private void RevealResearch(string id)
    {
        if (!_technologies.TryGetValue(id, out var technology) || !List.TryGetValue(id, out var availability)) return;
        CurrentTech = id;
        if (_goalTechnology != null)
        {
            _goalTechnology = id;
            UpdatePanels(List);
        }
        else if (!_nodePositions.ContainsKey(id))
        {
            Search.Text = "";
            AvailabilityFilter.SelectId(0);
            SelectResearchSection(technology.ID.StartsWith("Industrial", StringComparison.Ordinal) ? 1 : _disciplines.IndexOf(technology.Discipline.Id) + 2);
        }
        SelectTech(technology, availability);
        Recenter();
    }

    private void UpdateGoalSummary()
    {
        if (CurrentTech is not { } selected || !_technologies.ContainsKey(selected.Id))
        {
            GoalButton.Disabled = QueueGoalButton.Disabled = true;
            GoalSummary.SetMessage("");
            return;
        }
        GoalButton.Disabled = false;
        QueueGoalButton.Disabled = !_canResearch || List.GetValueOrDefault(selected.Id) == ResearchAvailability.Researched;
        var missing = ResearchMapLayout.PrerequisitePath(_goalTechnology ?? selected.Id, _technologies)
            .Where(id => List.TryGetValue(id, out var state) && state != ResearchAvailability.Researched).ToArray();
        GoalSummary.SetMessage(Loc.GetString("research-browser-path-cost", ("count", missing.Length),
            ("points", missing.Sum(id => _technologies[id].Cost))), defaultColor: Color.FromHex("#A6A6A6"));
    }
}
