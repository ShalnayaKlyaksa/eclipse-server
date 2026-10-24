using System.Linq;
using Content.Client.Lobby.UI.Roles;
using Content.Client.Stylesheets;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{

    /// <summary>
    /// Refreshes traits selector
    /// </summary>
    public void RefreshTraits()
    {
        TraitsList.RemoveAllChildren();

        var traits = _prototypeManager.EnumeratePrototypes<TraitPrototype>().OrderBy(t => Loc.GetString(t.Name)).ToList();
        var maxTraitPoints = _cfgManager.GetCVar(CCVars.GameMaxTraitPoints);
        var selectedTraits = Profile?.TraitPreferences ?? new HashSet<ProtoId<TraitPrototype>>();
        HumanoidCharacterProfile.TryCalculateTraitPoints(
            selectedTraits,
            _prototypeManager,
            maxTraitPoints,
            out var gained,
            out var spent,
            out var balance);
        // TabContainer.SetTabTitle(3, Loc.GetString("humanoid-profile-editor-traits-tab")); // Corvax-TTS-Edit

        if (traits.Count < 1)
        {
            TraitsList.AddChild(new Label
            {
                Text = Loc.GetString("humanoid-profile-editor-no-traits"),
                FontColorOverride = Color.Gray,
            });
            return;
        }

        TraitsList.AddChild(new Label
        {
            Text = Loc.GetString(
                "humanoid-profile-editor-trait-balance",
                ("balance", balance),
                ("max", maxTraitPoints),
                ("gained", gained),
                ("spent", spent)),
            FontColorOverride = balance < 0 || gained > maxTraitPoints ? Color.Red : Color.Gray
        });

        // Setup model
        var traitGroups = new Dictionary<string, List<string>>
        {
            { TraitCategoryPrototype.Disadvantages, new List<string>() },
            { TraitCategoryPrototype.Advantages, new List<string>() },
            { TraitCategoryPrototype.Neutral, new List<string>() },
            { TraitCategoryPrototype.Speech, new List<string>() },
            { TraitCategoryPrototype.Languages, new List<string>() },
            { TraitCategoryPrototype.Default, new List<string>() },
        };

        foreach (var trait in traits)
        {
            if (trait.Category == null)
            {
                traitGroups[TraitCategoryPrototype.Default].Add(trait.ID);
                continue;
            }

            if (!_prototypeManager.HasIndex(trait.Category))
                continue;

            var group = traitGroups.GetOrNew(trait.Category);
            group.Add(trait.ID);
        }

        // Create UI view from model
        foreach (var (categoryId, categoryTraits) in traitGroups)
        {
            TraitCategoryPrototype? category = null;

            if (categoryId != TraitCategoryPrototype.Default)
            {
                category = _prototypeManager.Index<TraitCategoryPrototype>(categoryId);
                // Label
                TraitsList.AddChild(new Label
                {
                    Text = Loc.GetString(category.Name),
                    Margin = new Thickness(0, 10, 0, 0),
                    StyleClasses = { StyleClass.LabelHeading },
                });
            }

            if (categoryTraits.Count == 0)
                continue;

            if (categoryId == TraitCategoryPrototype.Languages)
            {
                TraitsList.AddChild(new Label
                {
                    Text = Loc.GetString("humanoid-profile-editor-language-count-hint"),
                    FontColorOverride = Color.Gray
                });
            }

            var selectedLanguages = CountSelectedLanguages(selectedTraits);
            var displayedSelectedLanguages = 0;
            foreach (var traitProto in categoryTraits)
            {
                var trait = _prototypeManager.Index<TraitPrototype>(traitProto);
                var selected = selectedTraits.Contains(trait.ID);
                var displayCost = trait.Category == TraitCategoryPrototype.Languages
                    ? -HumanoidCharacterProfile.GetTraitPointCost(
                        trait,
                        selected
                            ? displayedSelectedLanguages
                            : selectedLanguages)
                    : trait.Cost;

                var selector = new TraitPreferenceSelector(trait, displayCost);

                selector.Preference = selected;
                if (selected && trait.Category == TraitCategoryPrototype.Languages)
                    displayedSelectedLanguages++;

                selector.PreferenceChanged += preference =>
                {
                    if (preference)
                    {
                        Profile = Profile?.WithTraitPreference(trait.ID, _prototypeManager, maxTraitPoints);
                    }
                    else
                    {
                        Profile = Profile?.WithoutTraitPreference(trait.ID, _prototypeManager);
                    }

                    SetDirty();
                    RefreshTraits(); // If too many traits are selected, they will be reset to the real value.
                };

                if (!selector.Preference)
                    selector.SetBlockReason(GetTraitBlockReason(trait, selectedTraits, maxTraitPoints));

                TraitsList.AddChild(selector);
            }
        }
    }

    private string? GetTraitBlockReason(
        TraitPrototype trait,
        IReadOnlySet<ProtoId<TraitPrototype>> selectedTraits,
        int maxTraitPoints)
    {
        if (HumanoidCharacterProfile.HasTraitConflict(trait.ID, trait, selectedTraits, _prototypeManager))
        {
            foreach (var selected in selectedTraits)
            {
                if (!_prototypeManager.TryIndex<TraitPrototype>(selected, out var selectedProto))
                    continue;

                if (trait.Conflicts.Contains(selected) || selectedProto.Conflicts.Contains(trait.ID))
                {
                    return Loc.GetString(
                        "humanoid-profile-editor-trait-conflict",
                        ("trait", Loc.GetString(selectedProto.Name)));
                }
            }
        }

        var withTrait = new HashSet<ProtoId<TraitPrototype>>(selectedTraits) { trait.ID };
        HumanoidCharacterProfile.TryCalculateTraitPoints(
            withTrait,
            _prototypeManager,
            maxTraitPoints,
            out var gained,
            out var spent,
            out var balance);

        if (gained > maxTraitPoints)
        {
            return Loc.GetString(
                "humanoid-profile-editor-trait-gain-limit",
                ("current", gained),
                ("max", maxTraitPoints));
        }

        if (balance < 0)
        {
            return Loc.GetString(
                "humanoid-profile-editor-trait-negative-balance",
                ("spent", spent),
                ("gained", gained));
        }

        return null;
    }

    private int CountSelectedLanguages(IReadOnlySet<ProtoId<TraitPrototype>> selectedTraits)
    {
        var languages = 0;
        foreach (var selected in selectedTraits)
        {
            if (_prototypeManager.TryIndex<TraitPrototype>(selected, out var selectedProto) &&
                selectedProto.Category == TraitCategoryPrototype.Languages)
            {
                languages++;
            }
        }

        return languages;
    }
}
