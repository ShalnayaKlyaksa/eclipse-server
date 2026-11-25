using System.Linq;
using Content.Client.UserInterface.Systems.Guidebook;
using Content.Shared.Guidebook;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using static Content.Client.Corvax.SponsorOnlyHelpers;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    public event Action<List<ProtoId<GuideEntryPrototype>>>? OnOpenGuidebook;

    private ColorSelectorSliders _rgbSkinColorSelector;
    private List<SpeciesPrototype> _species = new();
    private readonly List<SpeciesPrototype> _speciesVariants = new();
    private static readonly ProtoId<GuideEntryPrototype> DefaultSpeciesGuidebook = "Species";

    public void UpdateSpeciesGuidebookIcon()
    {
        SpeciesInfoButton.Visible = Profile != null;
        SpeciesInfoButton.StyleIdentifier = "SpeciesInfoDefault";
    }

    private void UpdateGenderControls()
    {
        if (Profile == null)
        {
            return;
        }

        PronounsButton.SelectId((int)Profile.Gender);
    }

    private void UpdateAgeEdit()
    {
        AgeEdit.Text = Profile?.Age.ToString() ?? "";
    }

    private void UpdateSexControls()
    {
        if (Profile == null)
            return;

        SexButton.Clear();

        var sexes = new List<Sex>();

        // add species sex options, default to just none if we are in bizzaro world and have no species
        if (_prototypeManager.Resolve(Profile.Species, out var speciesProto))
        {
            foreach (var sex in speciesProto.Sexes)
            {
                sexes.Add(sex);
            }
            if (speciesProto.AllowsIntersex)
                sexes.Add(Sex.Intersex);
        }
        else
        {
            sexes.Add(Sex.Unsexed);
        }

        // add button for each sex
        foreach (var sex in sexes)
        {
            SexButton.AddItem(Loc.GetString($"humanoid-profile-editor-sex-{sex.ToString().ToLower()}-text"), (int)sex);
        }

        if (sexes.Contains(Profile.Sex))
            SexButton.SelectId((int)Profile.Sex);
        else
            SexButton.SelectId((int)sexes[0]);
    }

    private void UpdateEyePickers()
    {
        if (Profile == null)
        {
            return;
        }

        _markingsModel.SetOrganEyeColor(Profile.Appearance.EyeColor);
        EyeColorPicker.SetData(Profile.Appearance.EyeColor);
    }

    private void UpdateSkinColor()
    {
        if (Profile == null)
            return;

        var skin = _prototypeManager.Index<SpeciesPrototype>(Profile.Species).SkinColoration;
        var strategy = _prototypeManager.Index(skin).Strategy;

        switch (strategy.InputType)
        {
            case SkinColorationStrategyInput.Unary:
                {
                    if (!Skin.Visible)
                    {
                        Skin.Visible = true;
                        RgbSkinColorContainer.Visible = false;
                    }

                    Skin.Value = strategy.ToUnary(Profile.Appearance.SkinColor);

                    break;
                }
            case SkinColorationStrategyInput.Color:
                {
                    if (!RgbSkinColorContainer.Visible)
                    {
                        Skin.Visible = false;
                        RgbSkinColorContainer.Visible = true;
                    }

                    _rgbSkinColorSelector.Color = strategy.ClosestSkinColor(Profile.Appearance.SkinColor);

                    break;
                }
        }
    }

    private void UpdateSpawnPriorityControls()
    {
        if (Profile == null)
        {
            return;
        }

        SpawnPriorityButton.SelectId((int)Profile.SpawnPriority);
    }

    /// <summary>
    /// Refreshes the species selector.
    /// </summary>
    public void RefreshSpecies()
    {
        SpeciesButton.Clear();
        _species.Clear();

        _species.AddRange(_prototypeManager.EnumeratePrototypes<SpeciesPrototype>().Where(o => o.RoundStart && o.VariantOf == null));
        _species.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        for (var i = 0; i < _species.Count; i++)
        {
            var name = Loc.GetString(_species[i].Name);

            if (_species[i].SponsorOnly) // Corvax-Sponsors
                name += GetSponsorOnlySuffix();

            SpeciesButton.AddItem(name, i);
        }

        if (Profile != null)
        {
            var species = HumanoidCharacterProfile.MigratePlayableSpecies(Profile.Species);
            if (!_prototypeManager.TryIndex(species, out var prototype) || !prototype.RoundStart)
                species = HumanoidCharacterProfile.DefaultPlayableSpecies;

            if (species != Profile.Species)
                SetSpecies(species.Id);
            else
                RefreshSpeciesVariant(prototype!);
        }
    }

    private void RefreshSpeciesVariant(SpeciesPrototype selected)
    {
        var groupId = selected.VariantOf?.Id ?? selected.ID;
        var index = _species.FindIndex(species => species.ID == groupId);
        if (index >= 0)
            SpeciesButton.SelectId(index);

        _speciesVariants.Clear();
        SpeciesVariantButton.Clear();
        SpeciesVariantRow.Visible = selected.VariantName != null;
        SpeciesPaletteRow.Visible = selected.AppearancePresets.Count > 0;
        SpeciesPaletteButton.Clear();
        if (SpeciesPaletteRow.Visible)
        {
            SpeciesPaletteButton.AddItem(Loc.GetString("species-appearance-palette-select"), 0);
            SpeciesPaletteButton.SetItemDisabled(0, true);
            for (var i = 0; i < selected.AppearancePresets.Count; i++)
                SpeciesPaletteButton.AddItem(Loc.GetString(selected.AppearancePresets[i].Name), i + 1);
            SpeciesPaletteButton.SelectId(0);
        }

        if (!SpeciesVariantRow.Visible)
            return;

        _speciesVariants.Add(_prototypeManager.Index<SpeciesPrototype>(groupId));
        _speciesVariants.AddRange(_prototypeManager.EnumeratePrototypes<SpeciesPrototype>()
            .Where(species => species.RoundStart && species.VariantOf?.Id == groupId)
            .OrderBy(species => species.ID, StringComparer.Ordinal));
        for (var i = 0; i < _speciesVariants.Count; i++)
        {
            var variant = _speciesVariants[i];
            SpeciesVariantButton.AddItem(Loc.GetString(variant.VariantName?.Id ?? variant.Name), i);
            if (variant.ID == selected.ID)
                SpeciesVariantButton.SelectId(i);
        }
    }

    private void SetSpecies(string newSpecies)
    {
        if (Profile == null)
            return;

        var species = _prototypeManager.Index<SpeciesPrototype>(newSpecies);
        var useSpeciesTone = Profile.Species.Id != newSpecies && species.AppearancePresets.Count > 0;
        Profile = Profile.WithSpecies(newSpecies).WithCustomization(Profile.Customization.Validated(species));
        RefreshSpeciesVariant(species);
        if (!species.Sexes.Contains(Profile.Sex) && (Profile.Sex != Sex.Intersex || !species.AllowsIntersex))
            Profile = Profile.WithSex(species.Sexes[0]);
        if (useSpeciesTone)
        {
            Profile = Profile.WithCharacterAppearance(Profile.Appearance.WithSkinColor(species.DefaultSkinTone));
            UpdateSkinColor();
        }
        OnSkinColorOnValueChanged(); // Species may have special color prefs, make sure to update it.
        _markingsModel.Species = newSpecies;
        _markingsModel.OrganData = _markingManager.GetMarkingData(newSpecies);
        _markingsModel.ValidateMarkings();
        // In case there's job restrictions for the species
        RefreshJobs();
        // In case there's species restrictions for loadouts
        RefreshLoadouts();
        UpdateSexControls(); // update sex for new species
        UpdateCustomizationControls();
        UpdateSpeciesGuidebookIcon();
        ReloadPreview();
    }

    private void ApplySpeciesPalette(int index)
    {
        if (Profile == null || !_prototypeManager.TryIndex(Profile.Species, out var species)
            || index < 0 || index >= species.AppearancePresets.Count)
            return;

        var appearance = Profile.Appearance.WithMarkings(_markingsModel.Markings);
        Profile = Profile.WithCharacterAppearance(appearance.WithColorPreset(species.AppearancePresets[index]));
        UpdateSkinColor();
        UpdateMarkings();
        ReloadProfilePreview();
    }

    private void SetAge(int newAge)
    {
        Profile = Profile?.WithAge(newAge);
        ReloadPreview();
    }

    private void SetSex(Sex newSex)
    {
        Profile = Profile?.WithSex(newSex);
        // for convenience, default to most common gender when new sex is selected
        switch (newSex)
        {
            case Sex.Male:
                Profile = Profile?.WithGender(Gender.Male);
                break;
            case Sex.Female:
                Profile = Profile?.WithGender(Gender.Female);
                break;
            default:
                Profile = Profile?.WithGender(Gender.Epicene);
                break;
        }

        UpdateGenderControls();
        _markingsModel.SetOrganSexes(newSex.PresentationSex());
        ReloadPreview();
    }

    private void SetGender(Gender newGender)
    {
        Profile = Profile?.WithGender(newGender);
        ReloadPreview();
    }

    private void SetSpawnPriority(SpawnPriorityPreference newSpawnPriority)
    {
        Profile = Profile?.WithSpawnPriorityPreference(newSpawnPriority);
        SetDirty();
    }

    private void OnSpeciesInfoButtonPressed(BaseButton.ButtonEventArgs args)
    {
        var guidebookController = UserInterfaceManager.GetUIController<GuidebookUIController>();
        var page = DefaultSpeciesGuidebook;
        if (Profile != null && _prototypeManager.TryIndex(Profile.Species, out var species))
        {
            var entry = new ProtoId<GuideEntryPrototype>(species.VariantOf?.Id ?? species.ID);
            if (_prototypeManager.HasIndex(entry))
                page = entry;
        }

        if (_prototypeManager.Resolve(DefaultSpeciesGuidebook, out var guideRoot))
        {
            var dict = new Dictionary<ProtoId<GuideEntryPrototype>, GuideEntry>();
            dict.Add(DefaultSpeciesGuidebook, guideRoot);
            //TODO: Don't close the guidebook if its already open, just go to the correct page
            guidebookController.OpenGuidebook(dict, includeChildren: true, selected: page);
        }
    }

    private void OnSkinColorOnValueChanged()
    {
        if (Profile is null) return;

        var skin = _prototypeManager.Index<SpeciesPrototype>(Profile.Species).SkinColoration;
        var strategy = _prototypeManager.Index(skin).Strategy;

        switch (strategy.InputType)
        {
            case SkinColorationStrategyInput.Unary:
                {
                    if (!Skin.Visible)
                    {
                        Skin.Visible = true;
                        RgbSkinColorContainer.Visible = false;
                    }

                    var color = strategy.FromUnary(Skin.Value);

                    _markingsModel.SetOrganSkinColor(color);
                    Profile = Profile.WithCharacterAppearance(Profile.Appearance.WithSkinColor(color));

                    break;
                }
            case SkinColorationStrategyInput.Color:
                {
                    if (!RgbSkinColorContainer.Visible)
                    {
                        Skin.Visible = false;
                        RgbSkinColorContainer.Visible = true;
                    }

                    var color = strategy.ClosestSkinColor(_rgbSkinColorSelector.Color);

                    _markingsModel.SetOrganSkinColor(color);
                    Profile = Profile.WithCharacterAppearance(Profile.Appearance.WithSkinColor(color));

                    break;
                }
        }

        ReloadProfilePreview();
    }
}
