using System.Linq;
using System.Numerics;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Buckle.Components;
using Content.Shared.Interaction;
using Content.Shared.Input;
using Robust.Client.Input;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>Visible injuries and treatment for a nearby patient; detailed chip data needs a table.</summary>
public sealed class AdvancedHealthFieldWindow : DefaultWindow
{
    private readonly IEntityManager _entities = IoCManager.Resolve<IEntityManager>();
    private readonly EntityUid _patient;
    private readonly EntityUid _medic;
    private readonly Label _summary;
    private readonly Label _selected;
    private readonly BoxContainer _injuries;
    private readonly Button _chip;
    private AdvancedHealthStatusWindow? _chipWindow;
    private BodyPartTarget _part;
    private float _refresh;

    public AdvancedHealthFieldWindow(EntityUid patient, EntityUid medic)
    {
        _patient = patient;
        _medic = medic;
        var name = _entities.GetComponent<MetaDataComponent>(patient).EntityName;
        Title = Loc.GetString("advanced-health-field-title") + (string.IsNullOrWhiteSpace(name) ? "" : " · " + name);
        var available = Vector2.Max(Vector2.One, UserInterfaceManager.WindowRoot.Size - new Vector2(24));
        SetSize = Vector2.Min(new Vector2(580, 570), available);
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Margin = new Thickness(8) };
        Contents.AddChild(root);
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Margin = new Thickness(12) };
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
        scroll.AddChild(column);
        root.AddChild(scroll);
        _summary = new Label();
        column.AddChild(_summary);
        column.AddChild(new Label { Text = Loc.GetString("advanced-health-field-select", ("key", IoCManager.Resolve<IInputManager>().GetKeyFunctionButtonString(ContentKeyFunctions.AdvancedHealthAimTarget))) });
        _injuries = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 3 };
        column.AddChild(_injuries);
        _selected = new Label { Name = "MedicalSelectedPart" };
        column.AddChild(_selected);
        _chip = new Button { Name = "MedicalChipReadings", Text = Loc.GetString("advanced-health-field-chip"), ToolTip = Loc.GetString("advanced-health-field-table") };
        _chip.OnPressed += _ =>
        {
            if (!OnOperatingTable()) return;
            _chipWindow?.Close();
            _chipWindow = new AdvancedHealthStatusWindow(_patient, _medic);
            _chipWindow.Open();
        };
        root.AddChild(_chip);
        var tableHint = new RichTextLabel();
        tableHint.SetMessage(Loc.GetString("advanced-health-field-table"));
        root.AddChild(tableHint);
        OnClose += () => _chipWindow?.Close();
        SelectPart(_entities.TryGetComponent<AimTargetComponent>(medic, out var aim) ? aim.CurrentTarget : BodyPartTarget.Auto);
        Refresh();
    }

    public void SelectPart(BodyPartTarget part)
    {
        _part = part;
        Refresh();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _refresh -= args.DeltaSeconds;
        if (_refresh > 0) return;
        _refresh = 0.25f;
        if (!_entities.EntityExists(_medic) || !_entities.EntityExists(_patient)
            || _medic != _patient && !_entities.System<SharedInteractionSystem>().InRangeUnobstructed(_medic, _patient))
        {
            Close();
            return;
        }
        Refresh();
    }

    private bool OnOperatingTable() => _entities.TryGetComponent<BuckleComponent>(_patient, out var buckle)
        && buckle.BuckledTo is { } table && _entities.HasComponent<OperatingTableComponent>(table);

    private string Bleed(BodyPartState part) => Loc.GetString("advanced-health-bleeding-" + BodyPartState.GetBleedingLevel(part.Wounds.Sum(w => w.ExternalBleedingRate)).ToString().ToLowerInvariant());

    private void Refresh()
    {
        if (!_entities.TryGetComponent<AdvancedHealthComponent>(_patient, out var health)) return;
        var injured = health.BodyParts.Values.Where(p => p.IsDestroyed || p.Wounds.Count > 0).ToArray();
        var level = BodyPartState.GetBleedingLevel(health.BodyParts.Values.Sum(p => p.Wounds.Sum(w => w.ExternalBleedingRate)));
        _summary.Text = Loc.GetString("advanced-health-field-summary", ("count", injured.Length),
            ("bleed", Loc.GetString("advanced-health-bleeding-" + level.ToString().ToLowerInvariant())));
        _injuries.RemoveAllChildren();
        foreach (var part in injured)
            _injuries.AddChild(new Label { Text = Loc.GetString("advanced-health-field-part",
                ("part", Loc.GetString("advanced-health-part-" + part.Slot.ToString().ToLowerInvariant())),
                ("count", part.Wounds.Count), ("bleed", Bleed(part))) });
        BodyPartState? selected = null;
        if (_part != BodyPartTarget.Auto) health.BodyParts.TryGetValue(_part.ToSlot(), out selected);
        _selected.Text = selected == null ? Loc.GetString("advanced-health-field-no-part")
            : Loc.GetString("advanced-health-part-" + selected.Slot.ToString().ToLowerInvariant());
        _chip.Disabled = !OnOperatingTable();
        if (_chip.Disabled) _chipWindow?.Close();
    }

}
