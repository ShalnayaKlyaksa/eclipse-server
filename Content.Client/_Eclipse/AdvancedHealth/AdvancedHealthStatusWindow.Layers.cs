using System.Linq;
using Content.Client.HealthAnalyzer.UI;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Armor;
using Content.Shared.Body;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Eclipse.AdvancedHealth;

public sealed partial class AdvancedHealthStatusWindow
{
    private MedicalBodyViewMode _viewMode;
    private MedicalBodyViewMode? _styledViewMode;
    private RichTextLabel _mapHint = default!;
    private Control _severityLegend = default!;
    private readonly Dictionary<MedicalBodyViewMode, Button> _viewButtons = new();
    private readonly List<MedicalOrganMarker> _organMarkers = new();
    private readonly Dictionary<string, float> _armorCoefficients = new();
    private EntityUid? _selectedOrgan;
    public EntityUid? SelectedOrgan => _selectedOrgan;

    public void SelectOrgan(EntityUid organ)
    {
        if (!_organMarkers.Any(marker => marker.Entity == organ)) return;
        _selectedOrgan = organ;
        _bodyDoll.SelectedOrgan = organ;
        RefreshSelectedOrgan();
    }

    public MedicalBodyViewMode ViewMode => _viewMode;

    private Control BuildViewModes()
    {
        _viewButtons.Clear();
        _styledViewMode = null;
        var modes = VCol();
        modes.Name = "MedicalChipViewModes";
        modes.MinWidth = modes.MaxWidth = 48 * _s;
        modes.VerticalAlignment = VAlignment.Center;
        modes.SeparationOverride = Math.Max(4, (int) (10 * _s));
        foreach (var mode in Enum.GetValues<MedicalBodyViewMode>())
        {
            var button = MedicalButton("");
            button.ToolTip = Loc.GetString("advanced-health-layer-" + mode.ToString().ToLowerInvariant());
            button.AddChild(new MedicalViewIcon(mode) { MouseFilter = MouseFilterMode.Ignore });
            button.Name = "MedicalChipMode" + mode;
            button.ToggleMode = true;
            button.MinHeight = 48 * _s;
            button.OnPressed += _ => SelectView(mode);
            _viewButtons.Add(mode, button);
            modes.AddChild(button);
        }
        return modes;
    }

    public void SelectView(MedicalBodyViewMode mode)
    {
        _viewMode = mode;
        _styledViewMode = null;
        _conditionSignature = "";
        Refresh();
    }

    private void RefreshBodyLayer(AdvancedHealthComponent health)
    {
        _bodyDoll.ViewMode = _viewMode;
        _severityLegend.Visible = _viewMode == MedicalBodyViewMode.General;
        if (_styledViewMode != _viewMode)
        {
            foreach (var (mode, button) in _viewButtons)
            {
                button.Pressed = mode == _viewMode;
                button.Label.FontColorOverride = button.Pressed ? HealthMenuStyle.AmberBright : HealthMenuStyle.AmberDim;
                button.StyleBoxOverride = new StyleBoxFlat
                {
                    BackgroundColor = button.Pressed ? HealthMenuStyle.Amber.WithAlpha(0.14f) : HealthMenuStyle.PanelBg,
                    BorderColor = button.Pressed ? HealthMenuStyle.AmberBright : HealthMenuStyle.AmberBorder,
                    BorderThickness = new Thickness(button.Pressed ? 3 : 1, 1, 1, 1),
                    ContentMarginLeftOverride = 5 * _s, ContentMarginRightOverride = 5 * _s,
                };
            }
            _mapHint.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(_viewMode == MedicalBodyViewMode.General
                ? "advanced-health-chip-body-hint" : "advanced-health-layer-hint-" + _viewMode.ToString().ToLowerInvariant())), HealthMenuStyle.AmberDim);
            _styledViewMode = _viewMode;
        }

        if (_viewMode == MedicalBodyViewMode.Protection)
        {
            var query = new CoefficientQueryEvent(~SlotFlags.POCKET);
            _entities.EventBus.RaiseLocalEvent(_target, query);
            _armorCoefficients.Clear();
            foreach (var (type, coefficient) in query.DamageModifiers.Coefficients)
                _armorCoefficients[type] = coefficient;
        }
        _bodyDoll.SetLayerValues(health.BodyParts.Select(pair => (pair.Key, _viewMode switch
        {
            MedicalBodyViewMode.Protection => (float?) (Protection("Piercing") / 100f),
            _ => null,
        })));
        if (_viewMode != MedicalBodyViewMode.Organs)
            return;

        _organMarkers.Clear();
        if (_entities.System<SharedContainerSystem>().TryGetContainer(_target, BodyComponent.ContainerID, out var organs))
        {
            foreach (var uid in organs.ContainedEntities)
            {
                if (!_entities.TryGetComponent<OrganComponent>(uid, out var organ) || organ.Category is not { } category)
                    continue;
                var region = AdvancedOrganAnatomy.Region(category.Id);
                if (region is { } slot)
                    _organMarkers.Add(new MedicalOrganMarker(slot, category.Id,
                        _entities.GetComponent<MetaDataComponent>(uid).EntityName, uid,
                        _entities.TryGetComponent<AdvancedOrganHealthComponent>(uid, out var state) ? state.Function : 1f));
            }
        }
        // Custom chip artwork is independent from inventory sprites and keeps native organ identity.
        _bodyDoll.SetOrgans(_organMarkers.OrderBy(organ => organ.Category == "Heart"));
        if (!_organMarkers.Any(marker => marker.Entity == _selectedOrgan))
            _selectedOrgan = _organMarkers.Count > 0 ? _organMarkers[0].Entity : null;
        _bodyDoll.SelectedOrgan = _selectedOrgan;

    }

    private void RefreshSelectedOrgan()
    {
        _regionalSelector.Visible = _zoneBottom.Visible = false;
        _zoneEquipmentSection.Visible = false;
        _conditionRows.Clear();
        _woundsCaption.Visible = _woundsSeparator.Visible = _functionDetails.Visible = true;
        _woundsCaption.Text = Loc.GetString("advanced-organ-known-conditions");
        var marker = _organMarkers.FirstOrDefault(organ => organ.Entity == _selectedOrgan);
        if (marker == null || !_entities.TryGetComponent<AdvancedOrganHealthComponent>(marker.Entity, out var organ))
        {
            _partName.Text = Loc.GetString("advanced-organ-select");
            _partSeverity.Text = "";
            _zoneStats.Visible = false;
            _functionDetails.Text = Loc.GetString("advanced-organ-unavailable");
            RenderConditionCards("organ-none");
            return;
        }
        _partName.Text = marker.Name.ToUpperInvariant();
        _partName.ClipText = true;
        _partName.ToolTip = marker.Name;
        _partSeverity.Text = Loc.GetString(organ.Function < 0.01f ? "advanced-organ-failed"
            : organ.Function < 0.7f ? "advanced-organ-impaired" : "advanced-organ-working");
        _partSeverity.FontColorOverride = organ.Function < 0.7f ? HealthMenuStyle.Red : HealthMenuStyle.AmberBright;
        _zoneStats.Visible = true;
        _zoneHealthBar.Fraction = organ.Function;
        _zoneHealthValue.Text = Loc.GetString("advanced-health-vital-percent", ("value", MathF.Round(organ.Function * 100)));
        _zoneSkinBar.Fraction = organ.ExaminedIntegrity is { } integrity ? integrity / 100f : 0;
        _zoneSkinBar.Visible = organ.ExaminedIntegrity != null;
        _zoneSkinValue.Text = organ.ExaminedIntegrity is { } known
            ? Loc.GetString("advanced-health-vital-percent", ("value", MathF.Round(known))) : "—";
        _functionDetails.Text = Loc.GetString(organ.ExaminedIntegrity != null ? "advanced-organ-examined" : "advanced-organ-unexamined");
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        foreach (var condition in organ.KnownConditions)
        {
            if (!prototypes.TryIndex(condition.Prototype, out var prototype)) continue;
            _conditionRows.Add(("organ-condition-" + prototype.ID, Loc.GetString(prototype.Name),
                Loc.GetString(prototype.Description), prototype.Beneficial ? HealthMenuStyle.AmberBright : HealthMenuStyle.Red));
        }
        if (_conditionRows.Count == 0)
            _conditionRows.Add(("organ-none-known", Loc.GetString("advanced-organ-none-known"),
                Loc.GetString("advanced-organ-hidden-warning"), HealthMenuStyle.AmberDim));
        RenderConditionCards("organ-" + marker.Entity);
    }

    private float Protection(string type) => Math.Clamp((1 - _armorCoefficients.GetValueOrDefault(type, 1f)) * 100f, 0, 100);

    private void AppendLayerConditions(AdvancedHealthComponent health, BodyPartSlot slot, BodyPartState part, bool section)
    {
        var title = Loc.GetString("advanced-health-part-" + slot.ToString().ToLowerInvariant());
        switch (_viewMode)
        {
            case MedicalBodyViewMode.Protection:
                // Current SS14 armour applies to the entire body. Do not fabricate regional absorption.
                if (_conditionRows.Count != 0)
                    break;
                foreach (var type in new[] { "Piercing", "Blunt", "Slash", "Heat" })
                    _conditionRows.Add(("armor-" + type, Loc.GetString("armor-damage-type-" + type.ToLowerInvariant()),
                        Loc.GetString("advanced-health-layer-resistance", ("value", MathF.Round(Protection(type)))), HealthMenuStyle.AmberBright));
                _conditionRows.Add(("armor-model", Loc.GetString("advanced-health-layer-current-armor"),
                    Loc.GetString("advanced-health-layer-hint-protection"), HealthMenuStyle.AmberDim));
                var inventory = _entities.System<InventorySystem>().GetSlotEnumerator(_target, ~SlotFlags.POCKET);
                while (inventory.NextItem(out var item))
                {
                    if (!_entities.TryGetComponent<ArmorComponent>(item, out var armor)) continue;
                    var flats = string.Join(", ", armor.Modifiers.FlatReduction.Select(entry =>
                        Loc.GetString("armor-damage-type-" + entry.Key.ToLowerInvariant()) + ": " + entry.Value.ToString("0.#")));
                    if (flats.Length > 0)
                        _conditionRows.Add(("armor-flat-" + item, _entities.GetComponent<MetaDataComponent>(item).EntityName,
                            Loc.GetString("advanced-health-layer-flat-reduction", ("values", flats)), HealthMenuStyle.AmberDim));
                }
                break;
        }
    }
}
