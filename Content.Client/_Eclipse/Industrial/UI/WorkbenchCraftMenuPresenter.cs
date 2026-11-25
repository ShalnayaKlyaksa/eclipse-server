using Content.Client.Construction.UI;
using Content.Client.Hands.Systems;
using Content.Shared._Eclipse.Industrial;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared.Construction.Components;
using Content.Shared.Item;
using Content.Shared.Storage;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Eclipse.Industrial.UI;

public sealed partial class WorkbenchCraftMenuPresenter : IDisposable
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    private readonly IndustrialResourceSystem _resources;
    private readonly SpriteSystem _spriteSystem;
    private readonly HandsSystem _hands;

    private readonly ConstructionMenu _view;
    private readonly EntityUid _workbench;
    private readonly IndustrialWorkbenchBoundUserInterface _bui;

    private readonly List<IndustrialFabricationRecipePrototype> _recipes = [];
    private IndustrialFabricationRecipePrototype? _selected;
    private string _selectedCategory = string.Empty;
    private string _search = string.Empty;
    private int _inventoryHash;

    private const string ForAllCategoryName = "construction-category-all";
    private const string ComponentsCategoryName = "industrial-workbench-category-components";
    private const string MachinesCategoryName = "industrial-workbench-category-machines";

    public WorkbenchCraftMenuPresenter(IndustrialWorkbenchBoundUserInterface bui, EntityUid workbench)
    {
        IoCManager.InjectDependencies(this);
        _bui = bui;
        _workbench = workbench;
        _view = new ConstructionMenu
        {
            Title = Loc.GetString("industrial-workbench-window-title", ("tier", Workbench.Tier)),
            HelpGuidebookIds = new() { "EclipseIndustrialStart", "EclipseIndustrial" },
        };
        _resources = _entManager.System<IndustrialResourceSystem>();
        _spriteSystem = _entManager.System<SpriteSystem>();
        _hands = _entManager.System<HandsSystem>();

        _view.SetMode(ConstructionMenuMode.CraftOnly);
        _view.SetFavoritesPanelVisible(false);

        _view.OnClose += () => _bui.Close();
        _view.PopulateRecipes += OnPopulateRecipes;
        _view.CategorySelected += (_, cat) => _selectedCategory = cat;
        _view.RecipeSelected += OnRecipeSelected;
        _view.CraftRequested += (_, _) => OnCraftRequested();

        _view.ContentsContainer.AddChild(new InventoryRefreshControl(RefreshInventory));
        _recipes.AddRange(_prototypeManager.EnumeratePrototypes<IndustrialFabricationRecipePrototype>());
        _recipes.Sort((a, b) => string.Compare(RecipeName(a), RecipeName(b), StringComparison.InvariantCulture));
        RefreshAll();
    }

    public void Open()
    {
        if (_view.IsOpen)
            _view.MoveToFront();
        else
            _view.OpenCentered();
    }

    public void Dispose() => _view.Dispose();

    private string RecipeName(IndustrialFabricationRecipePrototype recipe)
        => recipe.Name != null ? Loc.GetString(recipe.Name) : _prototypeManager.Index(recipe.Result).Name;

    private IndustrialWorkbenchComponent Workbench => _entManager.GetComponent<IndustrialWorkbenchComponent>(_workbench);

    private int RoomGrade => _entManager.TryGetComponent<RoomSanitationComponent>(_workbench, out var room) ? room.Grade : 0;

    private void OnPopulateRecipes(object? sender, (string search, string category) args)
    {
        var rows = new List<ConstructionMenu.RecipeRowData>();
        var (search, category) = args;
        _search = search;
        search = search.Trim();
        var isEmptyCategory = string.IsNullOrEmpty(category) || category == ForAllCategoryName;

        foreach (var recipe in _recipes)
        {
            if (!_prototypeManager.TryIndex(recipe.Result, out var resultProto))
                continue;

            if (!isEmptyCategory && GetRecipeCategory(resultProto) != category)
                continue;

            var name = RecipeName(recipe);
            var machine = GetModuleMachine(resultProto);
            if (search.Length > 0 && !name.Contains(search, StringComparison.InvariantCultureIgnoreCase) &&
                !resultProto.Name.Contains(search, StringComparison.InvariantCultureIgnoreCase) &&
                machine?.Name.Contains(search, StringComparison.InvariantCultureIgnoreCase) != true)
                continue;

            var locked = GetLockReason(recipe);
            var available = locked == null && CanAfford(recipe);
            if (_view.ShowAvailableOnly && !available)
                continue;

            rows.Add(new ConstructionMenu.RecipeRowData(
                recipe.ID,
                name,
                resultProto,
                available,
                false,
                locked));
        }

        _view.SetRecipeListHeader(rows.Count);
        _view.PopulateRecipeList(rows);
        PopulateCategories(category);
    }

    private void PopulateCategories(string? selectCategory = null)
    {
        var machineCount = 0;

        foreach (var recipe in _recipes)
        {
            if (GetRecipeCategory(_prototypeManager.Index(recipe.Result)) == MachinesCategoryName)
                machineCount++;
        }

        var selected = string.IsNullOrEmpty(selectCategory) ? ForAllCategoryName : selectCategory;
        var entries = new List<ConstructionMenu.CategoryEntry>
        {
            new(ForAllCategoryName, Loc.GetString(ForAllCategoryName), _recipes.Count, selected == ForAllCategoryName),
            new(ComponentsCategoryName, Loc.GetString(ComponentsCategoryName), _recipes.Count - machineCount,
                selected == ComponentsCategoryName),
            new(MachinesCategoryName, Loc.GetString(MachinesCategoryName), machineCount,
                selected == MachinesCategoryName),
        };

        _view.SetCategories(entries);
    }

    private string GetRecipeCategory(EntityPrototype result)
    {
        var components = _entManager.ComponentFactory;
        return result.TryGetComponent<IndustrialUpgradeModuleComponent>(out _, components) ||
            result.TryGetComponent<IndustrialBlueprintComponent>(out _, components) ||
            !result.TryGetComponent<ItemComponent>(out _, components) ||
            result.TryGetComponent<FlatpackComponent>(out _, components)
            ? MachinesCategoryName
            : ComponentsCategoryName;
    }

    private EntityPrototype? GetModuleMachine(EntityPrototype result)
        => result.TryGetComponent<IndustrialUpgradeModuleComponent>(out var module, _entManager.ComponentFactory)
            ? _prototypeManager.Index(module.ResultMachine)
            : null;

    private string? GetLockReason(IndustrialFabricationRecipePrototype recipe)
    {
        if (Workbench.Tier < recipe.MinimumWorkbenchTier)
            return Loc.GetString("industrial-workbench-requires-tier", ("tier", recipe.MinimumWorkbenchTier));

        if (recipe.RequiredTechnology is { } technology &&
            (!_entManager.TryGetComponent<IndustrialMachineKnowledgeComponent>(_workbench, out var knowledge) ||
                !knowledge.Technologies.Contains(technology)))
        {
            return Loc.GetString("industrial-recipe-requires-disk", ("technology", Loc.GetString(_prototypeManager.Index(technology).Name)));
        }

        return RoomGrade < recipe.CleanroomGrade
            ? Loc.GetString("industrial-recipe-requires-cleanroom", ("grade", recipe.CleanroomGrade))
            : null;
    }

    private void OnRecipeSelected(object? sender, ConstructionMenu.RecipeSelectionData? item)
    {
        if (item is null)
        {
            _selected = null;
            _view.ClearRecipeInfo();
            return;
        }

        if (!_prototypeManager.TryIndex<IndustrialFabricationRecipePrototype>(item.Id, out var recipe))
            return;

        _selected = recipe;
        PopulateInfo(recipe);
    }

    private void PopulateInfo(IndustrialFabricationRecipePrototype recipe)
    {
        _view.ClearRecipeInfo();

        if (!_prototypeManager.TryIndex(recipe.Result, out var resultProto))
            return;

        var name = RecipeName(recipe);
        var description = GetModuleMachine(resultProto) is { } machine
            ? Loc.GetString("industrial-workbench-module-assembly", ("machine", machine.Name))
            : resultProto.Description ?? string.Empty;

        _view.SetRecipeInfo(name, description, resultProto, isStructure: false, isFavorite: false);
        _view.SetCraftAvailable(GetLockReason(recipe) == null && CanAfford(recipe));

        _view.SetTargetStats(Loc.GetString(
            "industrial-workbench-craft-duration",
            ("seconds", (int) MathF.Ceiling(recipe.Duration * Math.Clamp(_view.Quantity, 1, 10) / Workbench.CraftSpeed))));

        var stepList = _view.RecipeStepList;
        if (recipe.MinimumWorkbenchTier > 1)
            stepList.AddItem(Loc.GetString("industrial-workbench-requires-tier", ("tier", recipe.MinimumWorkbenchTier)), selectable: false);
        if (recipe.RequiredTechnology is { } technology)
            stepList.AddItem(Loc.GetString("industrial-recipe-requires-disk", ("technology", Loc.GetString(_prototypeManager.Index(technology).Name))), selectable: false);
        if (recipe.CleanroomGrade > 0)
            stepList.AddItem(Loc.GetString("industrial-recipe-requires-cleanroom", ("grade", recipe.CleanroomGrade)), selectable: false);

        foreach (var (protoId, amount) in recipe.Ingredients)
        {
            if (!_prototypeManager.TryIndex(protoId, out EntityPrototype? ingredientProto))
                continue;

            var have = CountAvailable(protoId);
            var text = Loc.GetString(
                "industrial-workbench-ingredient-line",
                ("have", have),
                ("need", amount * Math.Clamp(_view.Quantity, 1, 10)),
                ("name", ingredientProto.Name));

            var icon = _spriteSystem.Frame0(ingredientProto);
            stepList.AddItem(text, icon, false);
        }
    }

    private IEnumerable<EntityUid> Ingredients()
    {
        if (_entManager.TryGetComponent<StorageComponent>(_workbench, out var storage))
        {
            foreach (var item in storage.Container.ContainedEntities)
                yield return item;
        }
        if (_playerManager.LocalEntity is { } player)
        {
            foreach (var item in _hands.EnumerateHeld(player))
                yield return item;
        }
    }

    private int CountAvailable(string protoId) => _resources.Count(Ingredients(), protoId);

    private void OnCraftRequested()
    {
        if (_selected is null)
            return;

        var amount = Math.Clamp(_view.Quantity, 1, 10);
        _bui.SendCraftRequest(_selected.ID, amount);
    }

    private void RefreshAll()
    {
        OnPopulateRecipes(_view, (_search, _selectedCategory));
    }

    private bool CanAfford(IndustrialFabricationRecipePrototype recipe)
        => _resources.CanAfford(Ingredients(), recipe.Ingredients, Math.Clamp(_view.Quantity, 1, 10));
    private void RefreshInventory()
    {
        var hash = new HashCode();
        hash.Add(_view.Quantity);
        hash.Add(RoomGrade);
        if (_entManager.TryGetComponent<IndustrialMachineKnowledgeComponent>(_workbench, out var knowledge))
            hash.Add(knowledge.Technologies.Count);
        foreach (var item in Ingredients())
        {
            hash.Add(item);
            hash.Add(_resources.Count(item));
        }
        var current = hash.ToHashCode();
        if (_inventoryHash == current)
            return;
        _inventoryHash = current;
        RefreshAll();
        if (_selected != null)
            PopulateInfo(_selected);
    }

    private sealed class InventoryRefreshControl(Action refresh) : Control
    {
        private float _elapsed;
        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);
            _elapsed += args.DeltaSeconds;
            if (_elapsed < 0.5f)
                return;
            _elapsed %= 0.5f;
            refresh();
        }
    }

}
