using System.Linq;
using Content.Client.Construction.UI;
using Content.Client.Hands.Systems;
using Content.Shared._Eclipse.Industrial;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._Eclipse.Industrial.UI;

public sealed class WorkbenchCraftMenuPresenter : IDisposable
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private readonly SpriteSystem _spriteSystem;
    private readonly HandsSystem _hands;

    private readonly ConstructionMenu _view;
    private readonly EntityUid _workbench;
    private readonly IndustrialWorkbenchBoundUserInterface _bui;

    private readonly List<IndustrialFabricationRecipePrototype> _recipes = [];
    private IndustrialFabricationRecipePrototype? _selected;
    private string _selectedCategory = string.Empty;

    private const string ForAllCategoryName = "construction-category-all";
    private const string ComponentsCategoryName = "industrial-workbench-category-components";
    private const string ModulesCategoryName = "industrial-workbench-category-modules";

    public WorkbenchCraftMenuPresenter(IndustrialWorkbenchBoundUserInterface bui, EntityUid workbench)
    {
        IoCManager.InjectDependencies(this);
        _bui = bui;
        _workbench = workbench;
        _view = new ConstructionMenu();
        _spriteSystem = _entManager.System<SpriteSystem>();
        _hands = _entManager.System<HandsSystem>();

        _view.SetMode(ConstructionMenuMode.CraftOnly);
        _view.SetFavoritesPanelVisible(false);

        _view.OnClose += () => _bui.Close();
        _view.PopulateRecipes += OnPopulateRecipes;
        _view.CategorySelected += (_, cat) => _selectedCategory = cat;
        _view.RecipeSelected += OnRecipeSelected;
        _view.CraftRequested += (_, _) => OnCraftRequested();

        ReloadRecipes();
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

    private void ReloadRecipes()
    {
        _recipes.Clear();
        _recipes.AddRange(_prototypeManager.EnumeratePrototypes<IndustrialFabricationRecipePrototype>());
        _recipes.Sort((a, b) =>
        {
            var nameA = a.Name != null ? Loc.GetString(a.Name) : a.ID;
            var nameB = b.Name != null ? Loc.GetString(b.Name) : b.ID;
            return string.Compare(nameA, nameB, StringComparison.InvariantCulture);
        });
    }

    private void OnPopulateRecipes(object? sender, (string search, string category) args)
    {
        var rows = new List<ConstructionMenu.RecipeRowData>();
        var (search, category) = args;
        var isEmptyCategory = string.IsNullOrEmpty(category) || category == ForAllCategoryName;

        foreach (var recipe in _recipes)
        {
            var recipeCategory = GetRecipeCategory(recipe);
            if (!isEmptyCategory && recipeCategory != category)
                continue;

            var name = recipe.Name != null ? Loc.GetString(recipe.Name) : recipe.ID;
            if (!string.IsNullOrEmpty(search)
                && !name.Contains(search.Trim(), StringComparison.InvariantCultureIgnoreCase))
                continue;

            if (!_prototypeManager.TryIndex(recipe.Result, out var resultProto))
                continue;

            var available = CanAfford(recipe);
            if (_view.ShowAvailableOnly && !available)
                continue;

            rows.Add(new ConstructionMenu.RecipeRowData(
                recipe.ID,
                name,
                resultProto,
                available,
                false));
        }

        _view.SetRecipeListHeader(rows.Count);
        _view.PopulateRecipeList(rows);
        PopulateCategories(category);
    }

    private void PopulateCategories(string? selectCategory = null)
    {
        var componentCount = 0;
        var moduleCount = 0;

        foreach (var recipe in _recipes)
        {
            if (GetRecipeCategory(recipe) == ModulesCategoryName)
                moduleCount++;
            else
                componentCount++;
        }

        var selected = string.IsNullOrEmpty(selectCategory) ? ForAllCategoryName : selectCategory;
        var entries = new List<ConstructionMenu.CategoryEntry>
        {
            new(ForAllCategoryName, Loc.GetString(ForAllCategoryName), _recipes.Count, selected == ForAllCategoryName),
            new(ComponentsCategoryName, Loc.GetString(ComponentsCategoryName), componentCount,
                selected == ComponentsCategoryName),
            new(ModulesCategoryName, Loc.GetString(ModulesCategoryName), moduleCount,
                selected == ModulesCategoryName),
        };

        _view.SetCategories(entries);
    }

    private static string GetRecipeCategory(IndustrialFabricationRecipePrototype recipe)
    {
        return recipe.ID.Contains("UpgradeModule", StringComparison.Ordinal)
            ? ModulesCategoryName
            : ComponentsCategoryName;
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

        var name = recipe.Name != null ? Loc.GetString(recipe.Name) : recipe.ID;
        var description = resultProto.Description ?? string.Empty;

        _view.SetRecipeInfo(name, description, resultProto, isStructure: false, isFavorite: false);

        _view.SetTargetStats(Loc.GetString(
            "industrial-workbench-craft-duration",
            ("seconds", (int) recipe.Duration)));

        var stepList = _view.RecipeStepList;
        foreach (var (protoId, amount) in recipe.Ingredients)
        {
            if (!_prototypeManager.TryIndex(protoId, out EntityPrototype? ingredientProto))
                continue;

            var have = CountAvailable(protoId);
            var text = Loc.GetString(
                "industrial-workbench-ingredient-line",
                ("have", have),
                ("need", amount),
                ("name", ingredientProto.Name));

            var icon = _spriteSystem.Frame0(ingredientProto);
            stepList.AddItem(text, icon, false);
        }
    }

    private int CountAvailable(string protoId)
    {
        var total = 0;

        if (_entManager.TryGetComponent<StorageComponent>(_workbench, out var storage))
        {
            foreach (var item in storage.Container.ContainedEntities)
                total += GetItemCount(item, protoId);
        }

        if (_playerManager.LocalEntity is { } player)
        {
            foreach (var hand in _hands.EnumerateHeld(player))
                total += GetItemCount(hand, protoId);
        }

        return total;
    }

    private int GetItemCount(EntityUid item, string protoId)
    {
        if (_entManager.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID != protoId)
            return 0;

        if (_entManager.TryGetComponent<StackComponent>(item, out var stack))
            return stack.Count;

        return 1;
    }

    private void OnCraftRequested()
    {
        if (_selected is null)
            return;

        var amount = Math.Clamp(_view.Quantity, 1, 10);
        _bui.SendCraftRequest(_selected.ID, amount);
    }

    private void RefreshAll()
    {
        OnPopulateRecipes(_view, (string.Empty, _selectedCategory));
    }

    private bool CanAfford(IndustrialFabricationRecipePrototype recipe)
    {
        if (_playerManager.LocalEntity is not { } player)
            return false;

        var remaining = new Dictionary<string, int>(recipe.Ingredients);
        CountFromStorage(_workbench, remaining);

        if (remaining.Count == 0)
            return true;

        foreach (var hand in _hands.EnumerateHeld(player))
            CountFromItem(hand, remaining);

        return remaining.Count == 0;
    }

    private void CountFromStorage(EntityUid workbench, Dictionary<string, int> remaining)
    {
        if (!_entManager.TryGetComponent<StorageComponent>(workbench, out var storage))
            return;

        foreach (var item in storage.Container.ContainedEntities)
            CountFromItem(item, remaining);
    }

    private void CountFromItem(EntityUid item, Dictionary<string, int> remaining)
    {
        if (remaining.Count == 0)
            return;

        var protoId = _entManager.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID;
        if (protoId == null || !remaining.TryGetValue(protoId, out var needed))
            return;

        if (_entManager.TryGetComponent<StackComponent>(item, out var stack))
        {
            if (stack.Count < needed)
                return;

            remaining[protoId] = needed - stack.Count;
            if (remaining[protoId] <= 0)
                remaining.Remove(protoId);

            return;
        }

        remaining[protoId] = needed - 1;
        if (remaining[protoId] <= 0)
            remaining.Remove(protoId);
    }
}
