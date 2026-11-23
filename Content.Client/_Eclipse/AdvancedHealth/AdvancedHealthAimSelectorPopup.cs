using System.Numerics;
using System.Linq;
using Robust.Client.Player;
using Content.Client.HealthAnalyzer.UI;
using Content.Shared._Eclipse.AdvancedHealth;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>Soft circular highlight drawn in local space (avoids fullscreen draw artifacts).</summary>
internal sealed class AdvancedHealthAimHaloControl : Control
{
    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        if (box.Width <= 0 || box.Height <= 0)
            return;

        var center = box.Center;
        var radius = Math.Min(box.Width, box.Height) * 0.5f;
        handle.DrawCircle(center, radius, Color.FromHex("#ffffff16"));
        handle.DrawCircle(center, radius, Color.FromHex("#ffffff28"), filled: false);
    }
}

/// <summary>Fullscreen aim overlay with a mannequin centered on the cursor.</summary>
public sealed class AdvancedHealthAimSelectorPopup : LayoutContainer
{
    private const float DollSize = 420f;
    private const float HaloSize = DollSize * 1.24f;

    public BodyPartTarget Selected { get; private set; } = BodyPartTarget.Chest;

    private readonly PanelContainer _backdrop;
    private readonly AdvancedHealthAimHaloControl _halo;
    private readonly AdvancedHealthBodyDollControl _doll;
    private readonly PanelContainer _tooltip;
    private readonly Label _tooltipLabel;
    private readonly Button _treat;
    private BodyPartSlot? _actionSlot;
    private float _actionLinger;
    public event Action? OnTreatmentStarted;

    public AdvancedHealthAimSelectorPopup(BodyPartTarget initial = BodyPartTarget.Chest)
    {
        if (initial is BodyPartTarget.Neck or BodyPartTarget.Pelvis)
            initial = BodyPartTarget.Chest;

        Selected = initial;
        MouseFilter = MouseFilterMode.Stop;

        _backdrop = new PanelContainer
        {
            MouseFilter = MouseFilterMode.Stop,
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#00000099") },
        };

        _halo = new AdvancedHealthAimHaloControl
        {
            MouseFilter = MouseFilterMode.Ignore,
            SetSize = new Vector2(HaloSize, HaloSize),
            MinSize = new Vector2(HaloSize, HaloSize),
        };

        _doll = new AdvancedHealthBodyDollControl
        {
            SetSize = new Vector2(DollSize, DollSize),
            MinSize = new Vector2(DollSize, DollSize),
            MouseFilter = MouseFilterMode.Stop,
        };
        _doll.EnableAimSelection();
        _doll.OnPartHovered += slot => Selected = slot.ToTarget();
        _doll.OnPartSelected += slot => Selected = slot.ToTarget();
        _treat = new Button { Name = "MedicalPartToolAction", Visible = false };
        _treat.OnPressed += _ =>
        {
            if (_actionSlot is not { } part || IoCManager.Resolve<IPlayerManager>().LocalEntity is not { } user) return;
            IoCManager.Resolve<IEntityManager>().System<AdvancedHealthClientSystem>().UseHeldItemOnPart(user, user, part);
            OnTreatmentStarted?.Invoke();
        };

        _tooltipLabel = new Label
        {
            ModulateSelfOverride = Color.FromHex("#d8f5d8"),
            Margin = new Thickness(6, 4),
        };
        _tooltip = new PanelContainer
        {
            Visible = false,
            MouseFilter = MouseFilterMode.Ignore,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#101010f5"),
                BorderColor = Color.FromHex("#4be06e99"),
                BorderThickness = new Thickness(1),
            },
        };
        _tooltip.AddChild(_tooltipLabel);

        AddChild(_backdrop);
        AddChild(_halo);
        AddChild(_doll);
        AddChild(_tooltip);
        AddChild(_treat);

        LayoutContainer.SetAnchorAndMarginPreset(_backdrop, LayoutPreset.Wide);
        LayoutContainer.SetAnchorPreset(_halo, LayoutPreset.TopLeft);
        LayoutContainer.SetAnchorPreset(_doll, LayoutPreset.TopLeft);
        LayoutContainer.SetAnchorPreset(_tooltip, LayoutPreset.TopLeft);
    }

    public void OpenAtCursor(Vector2 cursorUi)
    {
        if (Parent == null)
        {
            UserInterfaceManager.WindowRoot.AddChild(this);
            LayoutContainer.SetAnchorAndMarginPreset(this, LayoutPreset.Wide);
        }

        LayoutOverlay(cursorUi);
        Measure(Vector2Helpers.Infinity);
        InvalidateArrange();
        Visible = true;
    }

    public void Close()
    {
        Orphan();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _actionLinger -= args.DeltaSeconds;
        UpdateTooltip();
        UpdateTreatment();
    }

    private void UpdateTreatment()
    {
        var entities = IoCManager.Resolve<IEntityManager>();
        if (IoCManager.Resolve<IPlayerManager>().LocalEntity is not { } user
            || !entities.TryGetComponent<AdvancedHealthComponent>(user, out var health)) { _treat.Visible = false; return; }
        _doll.SetAimHealth(health.BodyParts.Values.Select(p => p.ToUiState(p.Wounds.Sum(w => w.ExternalBleedingRate))));
        var overButton = _treat.Visible && new UIBox2(_treat.GlobalPosition, _treat.GlobalPosition + _treat.Size)
            .Contains(UserInterfaceManager.MousePositionScaled.Position);
        if (_doll.HoveredSlot is { } slot
            && entities.System<AdvancedHealthClientSystem>().TryGetPartAction(user, user, slot, out _, out var label))
        {
            _actionSlot = slot;
            _actionLinger = 0.3f;
            _treat.Text = label;
        }
        else if (!overButton && _actionLinger <= 0) { _treat.Visible = false; _actionSlot = null; return; }
        if (_actionSlot is not { } selected) return;
        _treat.Measure(Vector2Helpers.Infinity);
        var anchor = _doll.Position + _doll.GetSlotCentroidLocal(selected);
        var size = _treat.DesiredSize;
        LayoutContainer.SetPosition(_treat, anchor - new Vector2(size.X / 2, size.Y + 8));
        _treat.Visible = true;
    }

    private void LayoutOverlay(Vector2 center)
    {
        LayoutContainer.SetPosition(_doll, center - new Vector2(DollSize / 2f));
        LayoutContainer.SetPosition(_halo, center - new Vector2(HaloSize / 2f));
        _tooltip.Visible = false;
    }

    private void UpdateTooltip()
    {
        if (_doll.HoveredSlot is not { } slot)
        {
            _tooltip.Visible = false;
            return;
        }

        _tooltipLabel.Text = Loc.GetString("advanced-health-part-" + slot.ToString().ToLowerInvariant()) + " · "
            + Loc.GetString("advanced-health-aim-penalty", ("value", AdvancedHealthAimPenalties.GetDefaultPenalty(slot)));
        _tooltip.Measure(Vector2Helpers.Infinity);

        var tipSize = _tooltip.DesiredSize;
        var anchor = _doll.Position + _doll.GetSlotCentroidLocal(slot);
        var pos = anchor + new Vector2(14f, -tipSize.Y * 0.5f);

        if (pos.X + tipSize.X > Size.X - 8f)
            pos.X = anchor.X - tipSize.X - 14f;
        pos.Y = Math.Clamp(pos.Y, 8f, Size.Y - tipSize.Y - 8f);

        LayoutContainer.SetPosition(_tooltip, pos);
        _tooltip.Visible = true;
    }
}

/// <summary>Shared aim penalty lookup for client UI (mirrors server).</summary>
public static class AdvancedHealthAimPenalties
{
    public static int GetDefaultPenalty(BodyPartSlot slot) => slot switch
    {
        BodyPartSlot.Chest => 0, BodyPartSlot.Abdomen => -12, BodyPartSlot.Pelvis => -18,
        BodyPartSlot.Head => -48, BodyPartSlot.Neck => -62,
        BodyPartSlot.LeftUpperArm or BodyPartSlot.RightUpperArm => -28,
        BodyPartSlot.LeftForearm or BodyPartSlot.RightForearm => -38,
        BodyPartSlot.LeftHand or BodyPartSlot.RightHand => -55,
        BodyPartSlot.LeftThigh or BodyPartSlot.RightThigh => -22,
        BodyPartSlot.LeftShin or BodyPartSlot.RightShin => -34,
        _ => -50,
    };
}
