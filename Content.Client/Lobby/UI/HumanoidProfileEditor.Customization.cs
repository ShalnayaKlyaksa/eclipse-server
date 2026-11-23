using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private bool _loadingDimensions;

    private void InitializeCustomization()
    {
        BindOption<VulpkaninBodyType>(BodyTypeButton, "body-type", (values, bodyType) => values with { BodyType = bodyType },
            ReloadProfilePreview);
        BindOption<VocalStyle>(VocalStyleButton, "vocal-style", (values, style) => values with { VocalStyle = style },
            UpdateTTSVoicesControls);
        BindOption<ErpPreference>(ErpButton, "erp", (values, erp) => values with { Erp = erp });
        BindOption<CharacterHistory>(VirginityButton, "history", (values, history) => values with { Virginity = history });
        BindOption<CharacterHistory>(AnalVirginityButton, "history", (values, history) => values with { AnalVirginity = history });

        HeightSlider.Rounded = true;
        WeightSlider.Rounded = true;
        HeightSlider.OnValueChanged += _ => SaveDimensions();
        WeightSlider.OnValueChanged += _ => SaveDimensions();
        ResetHeightButton.OnPressed += _ => ResetDimension(height: true);
        ResetWeightButton.OnPressed += _ => ResetDimension(height: false);
    }

    private void BindOption<T>(OptionButton button, string key, Func<CharacterCustomization, T, CharacterCustomization> update,
        Action? refresh = null) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        for (var i = 0; i < values.Length; i++)
            button.AddItem(Loc.GetString($"humanoid-profile-editor-{key}-{values[i].ToString().ToLowerInvariant()}"), i);

        button.OnItemSelected += args =>
        {
            if (Profile == null)
                return;

            button.SelectId(args.Id);
            Profile = Profile.WithCustomization(update(Profile.Customization, values[args.Id]));
            refresh?.Invoke();
            SetDirty();
        };
    }

    private void UpdateCustomizationControls()
    {
        if (Profile == null || !_prototypeManager.TryIndex<SpeciesPrototype>(Profile.Species, out var species))
            return;

        var customization = Profile.Customization;
        BodyTypeRow.Visible = CharacterCustomization.HasBodyTypes(Profile.Species);
        BodyTypeButton.SelectId((int) customization.BodyType);
        VocalStyleButton.SelectId((int) customization.VocalStyle);
        ErpButton.SelectId((int) customization.Erp);
        VirginityButton.SelectId((int) customization.Virginity);
        AnalVirginityButton.SelectId((int) customization.AnalVirginity);

        _loadingDimensions = true;
        HeightSlider.MaxValue = species.MaxHeight;
        HeightSlider.MinValue = species.MinHeight;
        HeightSlider.Value = customization.Height == 0 ? species.DefaultHeight : customization.Height;
        WeightSlider.MaxValue = species.MaxWeight;
        WeightSlider.MinValue = species.MinWeight;
        WeightSlider.Value = customization.Weight == 0 ? species.DefaultWeight : customization.Weight;
        _loadingDimensions = false;
        UpdateDimensionLabels();
    }

    private void SaveDimensions()
    {
        if (_loadingDimensions || Profile == null || !_prototypeManager.TryIndex<SpeciesPrototype>(Profile.Species, out var species))
            return;

        var height = (int) HeightSlider.Value;
        var weight = (int) WeightSlider.Value;
        Profile = Profile.WithCustomization(Profile.Customization with
        {
            Height = height == species.DefaultHeight ? 0 : height,
            Weight = weight == species.DefaultWeight ? 0 : weight,
        });
        UpdateDimensionLabels();
        ReloadProfilePreview();
    }

    private void ResetDimension(bool height)
    {
        if (Profile == null)
            return;

        Profile = Profile.WithCustomization(height
            ? Profile.Customization with { Height = 0 }
            : Profile.Customization with { Weight = 0 });
        UpdateCustomizationControls();
        ReloadProfilePreview();
    }

    private void UpdateDimensionLabels()
    {
        HeightLabel.Text = Loc.GetString("humanoid-profile-editor-height", ("value", (int) HeightSlider.Value));
        WeightLabel.Text = Loc.GetString("humanoid-profile-editor-weight", ("value", (int) WeightSlider.Value));
    }
}
