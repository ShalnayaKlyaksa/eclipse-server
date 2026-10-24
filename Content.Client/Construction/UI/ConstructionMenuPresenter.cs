using System.Linq;
using System.Numerics;
using Content.Client.Lobby;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Whitelist;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client.Construction.UI;

/// <summary>
/// Presents the Construction/Crafting UI, linking <see cref="ConstructionSystem"/> with the view.
/// </summary>
internal sealed class ConstructionMenuPresenter : IDisposable
{
    [Dependency] private readonly EntityManager _entManager = default!;
    [Dependency] private readonly IEntitySystemManager _systemManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IPlacementManager _placementManager = default!;
    [Dependency] private readonly IUserInterfaceManager _uiManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IClientPreferencesManager _preferencesManager = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    private readonly SpriteSystem _spriteSystem;
    private readonly ISawmill _sawmill;

    private readonly IConstructionMenuView _constructionView;
    private readonly EntityWhitelistSystem _whitelistSystem;

    private ConstructionSystem? _constructionSystem;
    private ConstructionPrototype? _selected;
    private List<ConstructionPrototype> _favoritedRecipes = [];
    private string _selectedCategory = string.Empty;

    private const string FavoriteCatName = "construction-category-favorites";
    private const string ForAllCategoryName = "construction-category-all";

    private ConstructionType ActiveRecipeType =>
        _constructionView.ActiveTab == ConstructionMenuTab.Construction
            ? ConstructionType.Structure
            : ConstructionType.Item;

    private bool CraftingAvailable
    {
        get => _uiManager.GetActiveUIWidget<GameTopMenuBar>().CraftingButton.Visible;
        set
        {
            _uiManager.GetActiveUIWidget<GameTopMenuBar>().CraftingButton.Visible = value;
            if (!value)
                _constructionView.Close();
        }
    }

    private bool IsAtFront => _constructionView.IsOpen && _constructionView.IsAtFront();

    private bool WindowOpen
    {
        get => _constructionView.IsOpen;
        set
        {
            if (value && CraftingAvailable)
            {
                if (_constructionView.IsOpen)
                    _constructionView.MoveToFront();
                else
                    _constructionView.OpenCentered();

                if (_selected != null)
                    PopulateInfo(_selected);
            }
            else
                _constructionView.Close();
        }
    }

    public ConstructionMenuPresenter()
    {
        IoCManager.InjectDependencies(this);
        _constructionView = new ConstructionMenu();
        _whitelistSystem = _entManager.System<EntityWhitelistSystem>();
        _spriteSystem = _entManager.System<SpriteSystem>();
        _sawmill = _logManager.GetSawmill("construction.ui");

        if (_systemManager.TryGetEntitySystem<ConstructionSystem>(out var constructionSystem))
            SystemBindingChanged(constructionSystem);

        _systemManager.SystemLoaded += OnSystemLoaded;
        _systemManager.SystemUnloaded += OnSystemUnloaded;
        _placementManager.PlacementChanged += OnPlacementChanged;

        _constructionView.OnClose +=
            () => _uiManager.GetActiveUIWidget<GameTopMenuBar>().CraftingButton.Pressed = false;
        _constructionView.ClearAllGhosts += (_, _) => _constructionSystem?.ClearAllGhosts();
        _constructionView.PopulateRecipes += OnViewPopulateRecipes;
        _constructionView.RecipeSelected += OnViewRecipeSelected;
        _constructionView.CategorySelected += (_, cat) => _selectedCategory = cat;
        _constructionView.TabChanged += (_, _) => OnTabChanged();
        _constructionView.BuildButtonToggled += (_, b) => BuildButtonToggled(b);
        _constructionView.EraseButtonToggled += (_, b) =>
        {
            if (_constructionSystem is null)
                return;
            if (b)
                _placementManager.Clear();
            _placementManager.ToggleEraserHijacked(new ConstructionPlacementHijack(_constructionSystem, null));
            _constructionView.EraseButtonPressed = b;
        };
        _constructionView.RecipeFavorited += (_, _) => OnViewFavoriteRecipe();
        _constructionView.CraftRequested += (_, _) => OnCraftRequested();

        SetFavorites(_preferencesManager.Preferences?.ConstructionFavorites ?? []);
        RefreshAll();
    }

    public void OnHudCraftingButtonToggled(BaseButton.ButtonToggledEventArgs args)
    {
        WindowOpen = args.Pressed;
    }

    public void Dispose()
    {
        _constructionView.Dispose();

        SystemBindingChanged(null);
        _systemManager.SystemLoaded -= OnSystemLoaded;
        _systemManager.SystemUnloaded -= OnSystemUnloaded;
        _placementManager.PlacementChanged -= OnPlacementChanged;
    }

    private void OnPlacementChanged(object? sender, EventArgs e)
    {
        _constructionView.ResetPlacement();
    }

    private void OnTabChanged()
    {
        _selected = null;
        _selectedCategory = string.Empty;
        _constructionView.ClearRecipeInfo();
        RefreshAll();
    }

    private void OnViewRecipeSelected(object? sender, ConstructionMenu.RecipeSelectionData? item)
    {
        if (item is null)
        {
            _selected = null;
            _constructionView.ClearRecipeInfo();
            return;
        }

        if (!_prototypeManager.TryIndex<ConstructionPrototype>(item.Id, out var prototype))
            return;

        var requiredTab = prototype.Type == ConstructionType.Structure
            ? ConstructionMenuTab.Construction
            : ConstructionMenuTab.Craft;

        if (_constructionView.ActiveTab != requiredTab)
        {
            _constructionView.ActiveTab = requiredTab;
            _selectedCategory = string.Empty;
            RefreshAll();
        }

        _selected = prototype;

        if (_placementManager is { IsActive: true, Eraser: false })
            UpdateGhostPlacement();

        PopulateInfo(_selected);
    }

    private void OnViewPopulateRecipes(object? sender, (string search, string catagory) args)
    {
        if (_constructionSystem is null)
            return;

        var recipes = GetAndSortRecipes(args);
        var rows = new List<ConstructionMenu.RecipeRowData>();

        foreach (var recipe in recipes)
        {
            var available = IsRecipeAvailable(recipe.Prototype);
            if (_constructionView.ShowAvailableOnly && !available)
                continue;

            rows.Add(new ConstructionMenu.RecipeRowData(
                recipe.Prototype.ID,
                recipe.Prototype.Name ?? recipe.Prototype.ID,
                recipe.TargetPrototype,
                available,
                _favoritedRecipes.Contains(recipe.Prototype)));
        }

        _constructionView.SetRecipeListHeader(rows.Count);
        _constructionView.PopulateRecipeList(rows);
        PopulateCategories(args.catagory);
        PopulateFavoritesBar();
    }

    private void RefreshAll()
    {
        OnViewPopulateRecipes(_constructionView, (string.Empty, _selectedCategory));
    }

    private List<ConstructionMenu.ConstructionMenuListData> GetAndSortRecipes((string, string) args)
    {
        var recipes = new List<ConstructionMenu.ConstructionMenuListData>();

        var (search, category) = args;
        var isEmptyCategory = string.IsNullOrEmpty(category) || category == ForAllCategoryName;
        _selectedCategory = isEmptyCategory ? string.Empty : category;

        foreach (var recipe in _prototypeManager.EnumeratePrototypes<ConstructionPrototype>())
        {
            if (recipe.Hide)
                continue;

            if (recipe.Type != ActiveRecipeType)
                continue;

            if (_playerManager.LocalSession == null
                || _playerManager.LocalEntity == null
                || _whitelistSystem.IsWhitelistFail(recipe.EntityWhitelist, _playerManager.LocalEntity.Value))
                continue;

            if (!string.IsNullOrEmpty(search) && recipe.Name is { } name &&
                !name.Contains(search.Trim(), StringComparison.InvariantCultureIgnoreCase))
                continue;

            if (!isEmptyCategory)
            {
                if ((category != FavoriteCatName || !_favoritedRecipes.Contains(recipe)) &&
                    recipe.Category != category)
                    continue;
            }

            if (!_constructionSystem!.TryGetRecipePrototype(recipe.ID, out var targetProtoId))
            {
                _sawmill.Error("Cannot find the target prototype in the recipe cache with the id \"{0}\".",
                    recipe.ID);
                continue;
            }

            if (!_prototypeManager.TryIndex(targetProtoId, out EntityPrototype? proto))
                continue;

            recipes.Add(new(recipe, proto));
        }

        recipes.Sort((a, b) =>
            string.Compare(a.Prototype.Name, b.Prototype.Name, StringComparison.InvariantCulture));

        return recipes;
    }

    private void PopulateCategories(string? selectCategory = null)
    {
        var categoryCounts = new Dictionary<string, int>();

        foreach (var recipe in _prototypeManager.EnumeratePrototypes<ConstructionPrototype>())
        {
            if (recipe.Hide || recipe.Type != ActiveRecipeType)
                continue;

            if (_playerManager.LocalEntity == null
                || _whitelistSystem.IsWhitelistFail(recipe.EntityWhitelist, _playerManager.LocalEntity.Value))
                continue;

            if (string.IsNullOrEmpty(recipe.Category))
                continue;

            categoryCounts.TryGetValue(recipe.Category, out var count);
            categoryCounts[recipe.Category] = count + 1;
        }

        var selected = string.IsNullOrEmpty(selectCategory) ? ForAllCategoryName : selectCategory;
        var entries = new List<ConstructionMenu.CategoryEntry>();

        var totalCount = categoryCounts.Values.Sum();
        entries.Add(new(ForAllCategoryName, Loc.GetString(ForAllCategoryName), totalCount,
            selected == ForAllCategoryName));

        if (_favoritedRecipes.Count > 0)
        {
            var favCount = _favoritedRecipes.Count(r => r.Type == ActiveRecipeType);
            entries.Add(new(FavoriteCatName, Loc.GetString(FavoriteCatName), favCount,
                selected == FavoriteCatName));
        }

        foreach (var cat in categoryCounts.Keys.OrderBy(Loc.GetString))
        {
            entries.Add(new(cat, Loc.GetString(cat), categoryCounts[cat], selected == cat));
        }

        _constructionView.SetCategories(entries);
    }

    private void PopulateFavoritesBar()
    {
        var favorites = new List<ConstructionMenu.ConstructionMenuListData>();

        foreach (var recipe in _favoritedRecipes)
        {
            if (_constructionSystem?.TryGetRecipePrototype(recipe.ID, out var targetProtoId) != true
                || string.IsNullOrEmpty(targetProtoId))
                continue;

            if (!_prototypeManager.TryIndex(targetProtoId, out EntityPrototype? proto) || proto is null)
                continue;

            favorites.Add(new(recipe, proto));
        }

        _constructionView.PopulateFavorites(favorites);
    }

    private void PopulateInfo(ConstructionPrototype? prototype)
    {
        if (_constructionSystem is null)
            return;

        _constructionView.ClearRecipeInfo();

        if (prototype is null)
            return;

        if (!_constructionSystem.TryGetRecipePrototype(prototype.ID, out var targetProtoId))
            return;

        if (!_prototypeManager.TryIndex(targetProtoId, out EntityPrototype? proto))
            return;

        _constructionView.SetRecipeInfo(
            prototype.Name!,
            prototype.Description!,
            proto,
            prototype.Type == ConstructionType.Structure,
            !_favoritedRecipes.Contains(prototype));

        GenerateStepList(prototype, _constructionView.RecipeStepList);
    }

    private void GenerateStepList(ConstructionPrototype prototype, ItemList stepList)
    {
        if (_constructionSystem?.GetGuide(prototype) is not { } guide)
            return;

        foreach (var entry in guide.Entries)
        {
            var text = entry.Arguments != null
                ? Loc.GetString(entry.Localization, entry.Arguments)
                : Loc.GetString(entry.Localization);

            if (entry.EntryNumber is { } number)
            {
                text = Loc.GetString("construction-presenter-step-wrapper",
                    ("step-number", number),
                    ("text", text));
            }

            text = text.PadLeft(text.Length + entry.Padding);

            var icon = entry.Icon != null ? _spriteSystem.Frame0(entry.Icon) : Texture.Transparent;
            stepList.AddItem(text, icon, false);
        }
    }

    private bool IsRecipeAvailable(ConstructionPrototype prototype)
    {
        if (prototype.Type == ConstructionType.Structure)
            return true;

        if (_playerManager.LocalEntity is not { } player)
            return false;

        if (_constructionSystem?.GetGuide(prototype) is not { } guide)
            return true;

        foreach (var entry in guide.Entries)
        {
            if (entry.Arguments is not { } args)
                continue;

            if (entry.Localization != "construction-presenter-material-step")
                continue;

            var amountObj = args.FirstOrDefault(a => a.Item1 == "amount").Item2;
            if (amountObj is not int amount)
                continue;

            // Material availability is validated server-side; optimistic on client.
        }

        return true;
    }

    private void BuildButtonToggled(bool pressed)
    {
        if (pressed)
        {
            if (_selected == null)
                return;

            if (_constructionSystem is null)
            {
                _constructionView.BuildButtonPressed = false;
                return;
            }

            if (_selected.Type == ConstructionType.Item)
            {
                _constructionView.BuildButtonPressed = false;
                return;
            }

            _placementManager.BeginPlacing(new PlacementInformation
                {
                    IsTile = false,
                    PlacementOption = _selected.PlacementMode
                },
                new ConstructionPlacementHijack(_constructionSystem, _selected));

            UpdateGhostPlacement();
        }
        else
            _placementManager.Clear();

        _constructionView.BuildButtonPressed = pressed;
    }

    private void OnCraftRequested()
    {
        if (_selected is not { Type: ConstructionType.Item } || _constructionSystem is null)
            return;

        var quantity = Math.Min(_constructionView.Quantity, 10);
        for (var i = 0; i < quantity; i++)
            _constructionSystem.TryStartItemConstruction(_selected.ID);
    }

    private void UpdateGhostPlacement()
    {
        if (_selected == null)
            return;

        if (_selected.Type != ConstructionType.Structure)
        {
            _placementManager.Clear();
            return;
        }

        var constructSystem = _systemManager.GetEntitySystem<ConstructionSystem>();

        _placementManager.BeginPlacing(new PlacementInformation
            {
                IsTile = false,
                PlacementOption = _selected.PlacementMode,
            },
            new ConstructionPlacementHijack(constructSystem, _selected));

        _constructionView.BuildButtonPressed = true;
    }

    private void OnSystemLoaded(object? sender, SystemChangedArgs args)
    {
        if (args.System is ConstructionSystem system)
            SystemBindingChanged(system);
    }

    private void OnSystemUnloaded(object? sender, SystemChangedArgs args)
    {
        if (args.System is ConstructionSystem)
            SystemBindingChanged(null);
    }

    private void OnViewFavoriteRecipe()
    {
        if (_selected is null)
            return;

        if (!_favoritedRecipes.Remove(_selected))
            _favoritedRecipes.Add(_selected);

        if (_selectedCategory == FavoriteCatName)
            RefreshAll();

        var newFavorites = new List<ProtoId<ConstructionPrototype>>(_favoritedRecipes.Count);
        foreach (var recipe in _favoritedRecipes)
            newFavorites.Add(recipe.ID);

        _preferencesManager.UpdateConstructionFavorites(newFavorites);
        PopulateInfo(_selected);
        PopulateCategories(_selectedCategory);
        PopulateFavoritesBar();
    }

    public void SetFavorites(IReadOnlyList<ProtoId<ConstructionPrototype>> favorites)
    {
        _favoritedRecipes.Clear();

        foreach (var id in favorites)
        {
            if (_prototypeManager.TryIndex(id, out ConstructionPrototype? recipe))
                _favoritedRecipes.Add(recipe);
        }

        if (_selectedCategory == FavoriteCatName)
            RefreshAll();

        PopulateCategories(_selectedCategory);
        PopulateFavoritesBar();
    }

    private void SystemBindingChanged(ConstructionSystem? newSystem)
    {
        if (newSystem is null)
        {
            if (_constructionSystem is null)
                return;

            UnbindFromSystem();
        }
        else
        {
            if (_constructionSystem is null)
            {
                BindToSystem(newSystem);
                return;
            }

            UnbindFromSystem();
            BindToSystem(newSystem);
        }
    }

    private void BindToSystem(ConstructionSystem system)
    {
        _constructionSystem = system;

        RefreshAll();

        system.ToggleCraftingWindow += SystemOnToggleMenu;
        system.FlipConstructionPrototype += SystemFlipConstructionPrototype;
        system.CraftingAvailabilityChanged += SystemCraftingAvailabilityChanged;
        system.ConstructionGuideAvailable += SystemGuideAvailable;
        if (_uiManager.GetActiveUIWidgetOrNull<GameTopMenuBar>() != null)
            CraftingAvailable = system.CraftingEnabled;
    }

    private void UnbindFromSystem()
    {
        var system = _constructionSystem ?? throw new InvalidOperationException();

        system.ToggleCraftingWindow -= SystemOnToggleMenu;
        system.FlipConstructionPrototype -= SystemFlipConstructionPrototype;
        system.CraftingAvailabilityChanged -= SystemCraftingAvailabilityChanged;
        system.ConstructionGuideAvailable -= SystemGuideAvailable;
        _constructionSystem = null;
    }

    private void SystemCraftingAvailabilityChanged(object? sender, CraftingAvailabilityChangedArgs e)
    {
        if (_uiManager.ActiveScreen == null)
            return;
        CraftingAvailable = e.Available;
    }

    private void SystemOnToggleMenu(object? sender, EventArgs eventArgs)
    {
        if (!CraftingAvailable)
            return;

        if (WindowOpen)
        {
            if (IsAtFront)
            {
                WindowOpen = false;
                _uiManager.GetActiveUIWidget<GameTopMenuBar>()
                    .CraftingButton.SetClickPressed(false);
            }
            else
                _constructionView.MoveToFront();
        }
        else
        {
            WindowOpen = true;
            _uiManager.GetActiveUIWidget<GameTopMenuBar>()
                .CraftingButton.SetClickPressed(true);
        }
    }

    private void SystemFlipConstructionPrototype(object? sender, EventArgs eventArgs)
    {
        if (!_placementManager.IsActive || _placementManager.Eraser)
            return;

        if (_selected == null || _selected.Mirror == null)
            return;

        _selected = _prototypeManager.Index<ConstructionPrototype>(_selected.Mirror);
        UpdateGhostPlacement();
    }

    private void SystemGuideAvailable(object? sender, string e)
    {
        if (!CraftingAvailable || !_constructionView.IsOpen || _selected == null)
            return;

        PopulateInfo(_selected);
        RefreshAll();
    }
}
