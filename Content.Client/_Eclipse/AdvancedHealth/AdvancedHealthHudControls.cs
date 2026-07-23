using System.Numerics;
using System.Linq;
using Content.Client.HealthAnalyzer.UI;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Shared._Eclipse.AdvancedHealth;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._Eclipse.AdvancedHealth;

public static class AdvancedHealthHudData
{
    public static BodyPartUiState[] GetBodyParts(AdvancedHealthComponent health)
    {
        return health.BodyParts.Values
            .Where(part => health.BodyPartHitWeights.GetValueOrDefault(part.Slot, 1f) > 0)
            .OrderBy(part => part.Slot)
            .Select(part => part.ToUiState())
            .ToArray();
    }

    public static int BloodPercentage(AdvancedHealthComponent health)
        => health.HasBlood
            ? (int) Math.Clamp(MathF.Round(health.BloodVolume / Math.Max(1f, health.MaxBloodVolume) * 100f), 0, 100)
            : 100;

    public static int PainPercentage(AdvancedHealthComponent health)
        => health.HasPain ? (int) Math.Clamp(MathF.Round(health.Pain), 0, 100) : 0;

    public static int ConsciousnessPercentage(AdvancedHealthComponent health)
        => (int) Math.Clamp(MathF.Round(health.Consciousness), 0, 100);
}

/// <summary>
/// Compact hover card supplied by the HumanHealth HUD alert.
/// </summary>
public sealed class AdvancedHealthAlertTooltip : PanelContainer
{
    private readonly IEntityManager _entities;
    private readonly IUserInterfaceManager _uiManager;
    private readonly EntityUid _target;
    private readonly AdvancedHealthBodyDollControl _bodyDoll;
    private readonly Label _blood;
    private readonly Label _pain;
    private readonly Label _consciousness;
    private float _refreshAccumulator;

    public AdvancedHealthAlertTooltip(EntityUid target)
    {
        _entities = IoCManager.Resolve<IEntityManager>();
        _uiManager = IoCManager.Resolve<IUserInterfaceManager>();
        _target = target;
        Stylesheet = IoCManager.Resolve<IStylesheetManager>().SheetSystem;
        SetOnlyStyleClass(StyleClass.TooltipPanel);

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(12),
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        AddChild(row);

        var values = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8, 10),
            VerticalAlignment = VAlignment.Center,
            HorizontalExpand = true,
            SeparationOverride = 5,
        };
        row.AddChild(values);

        _blood = CreateValueLabel();
        _pain = CreateValueLabel();
        _consciousness = CreateValueLabel();
        values.AddChild(_blood);
        values.AddChild(_pain);
        values.AddChild(_consciousness);

        _bodyDoll = new AdvancedHealthBodyDollControl
        {
            SetSize = new Vector2(280, 280),
            MinSize = new Vector2(280, 280),
            Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = VAlignment.Center,
        };
        row.AddChild(_bodyDoll);

        Refresh();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _refreshAccumulator += args.DeltaSeconds;
        if (_refreshAccumulator < 0.5f)
            return;
        _refreshAccumulator = 0;
        Refresh();
    }

    private void Refresh()
    {
        if (!_entities.TryGetComponent<AdvancedHealthComponent>(_target, out var health))
            return;

        PositionUnderChat();
        _bodyDoll.SetState(AdvancedHealthHudData.GetBodyParts(health));
        _blood.Text = $"BP: {AdvancedHealthHudData.BloodPercentage(health)}%";
        _pain.Text = $"PP: {AdvancedHealthHudData.PainPercentage(health)}%";
        _consciousness.Text = $"CP: {AdvancedHealthHudData.ConsciousnessPercentage(health)}%";
    }

    public void PositionUnderChat()
    {
        var chat = _uiManager.ActiveScreen?.GetWidget<ChatBox>() ??
                   _uiManager.ActiveScreen?.GetWidget<ResizableChatBox>();
        if (chat == null || chat.Size.X <= 1f)
            return;

        const float gap = 4f;
        var top = chat.GlobalPosition.Y + chat.Size.Y + gap;
        var screenSize = _uiManager.WindowRoot.Size;
        var availableWidth = Math.Max(320f, screenSize.X - chat.GlobalPosition.X - 8f);
        var width = Math.Clamp(chat.Size.X, 320f, availableWidth);
        var availableHeight = screenSize.Y - top - 8f;
        var height = Math.Clamp(availableHeight, 260f, 420f);
        var maxDollByWidth = Math.Max(190f, width - 170f);
        var dollSize = Math.Clamp(Math.Min(height - 36f, maxDollByWidth), 190f, 360f);

        MinWidth = width;
        MaxWidth = width;
        MinHeight = height;
        MaxHeight = height;
        _bodyDoll.SetSize = new Vector2(dollSize, dollSize);
        _bodyDoll.MinSize = new Vector2(dollSize, dollSize);

        if (Parent != null)
            LayoutContainer.SetPosition(this, new Vector2(chat.GlobalPosition.X, top));
    }

    private static Label CreateValueLabel() => new()
    {
        MinWidth = 110,
        Margin = new Thickness(0, 3),
        StyleClasses = { StyleClass.LabelHeading },
    };
}
