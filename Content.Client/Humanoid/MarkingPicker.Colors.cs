using System.Numerics;
using Content.Client.MainMenu.UI;
using Content.Shared.Humanoid.Markings;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.Humanoid;

public sealed partial class MarkingPicker
{
    [Dependency] private readonly ILocalizationManager _localization = default!;

    private Popup? _colorPopup;

    private Button? CreateColorButton(MarkingEntry entry)
    {
        if (_markingsModel is not { } model ||
            model.GetMarking(entry.Organ, entry.Layer, entry.Prototype.ID) is not { } marking ||
            marking.MarkingColors.Count == 0 ||
            !model.IsMarkingColorCustomizable(entry.Organ, entry.Layer, entry.Prototype.ID))
            return null;

        var button = new Button
        {
            Text = Loc.GetString("marking-colors-button"),
            StyleIdentifier = MainMenuControl.StyleIdentifierCompactButton,
            VerticalAlignment = VAlignment.Center,
        };
        button.OnPressed += _ => OpenColorPopup(entry, model, button);
        return button;
    }

    private void OpenColorPopup(MarkingEntry entry, MarkingsViewModel model, Button button)
    {
        if (Root is not { } root || model.GetMarking(entry.Organ, entry.Layer, entry.Prototype.ID) is not { } marking)
            return;

        _colorPopup?.Close();
        var popup = new Popup();
        _colorPopup = popup;
        popup.OnPopupHide += () =>
        {
            popup.Orphan();
            _colorPopup = null;
        };

        var panel = CreateCard(false);
        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(10),
        };
        panel.AddChild(content);
        popup.AddChild(panel);
        content.AddChild(new Label { Text = GetMarkingName(entry.Prototype), ClipText = true });

        var steps = new OptionButton { HorizontalExpand = true };
        steps.AddItem(Loc.GetString("marking-gradient-off"), 0);
        for (var i = MarkingGradient.MinSteps; i <= MarkingGradient.MaxSteps; i++)
            steps.AddItem(Loc.GetString("marking-gradient-steps", ("steps", i)), i);
        steps.SelectId(marking.Gradient?.Normalized().Steps ?? 0);
        content.AddChild(steps);

        var colorTarget = new OptionButton { HorizontalExpand = true };
        content.AddChild(colorTarget);
        var selector = new ColorSelectorSliders { SelectorType = ColorSelectorSliders.ColorSelectorType.Hsv };
        content.AddChild(selector);

        const int tipId = -1;
        var tipColor = marking.Gradient?.TipColor ?? DefaultTipColor(marking.MarkingColors[0]);

        void UpdateColorTargets(int selected)
        {
            colorTarget.Clear();
            for (var i = 0; i < marking.MarkingColors.Count; i++)
            {
                var label = Loc.GetString("marking-color-base");
                if (marking.MarkingColors.Count > 1)
                {
                    label = Loc.GetString("marking-color-layer", ("layer", i + 1));
                    if (i < entry.Prototype.Sprites.Count && entry.Prototype.Sprites[i] is SpriteSpecifier.Rsi rsi &&
                        _localization.TryGetString($"marking-{entry.Prototype.ID}-{rsi.RsiState}", out var layerName))
                        label = layerName;
                }

                colorTarget.AddItem(label, i);
            }

            if (marking.Gradient != null)
                colorTarget.AddItem(Loc.GetString("marking-gradient-tip"), tipId);
            colorTarget.Disabled = colorTarget.ItemCount <= 1;
            colorTarget.SelectId(selected);
            selector.Color = selected == tipId ? tipColor : marking.MarkingColors[selected];
        }

        UpdateColorTargets(0);
        colorTarget.OnItemSelected += args =>
        {
            if (model.GetMarking(entry.Organ, entry.Layer, entry.Prototype.ID) is not { } current)
                return;

            colorTarget.SelectId(args.Id);
            selector.Color = args.Id == tipId ? tipColor : current.MarkingColors[args.Id];
        };
        selector.OnColorChanged += color =>
        {
            if (colorTarget.SelectedId == tipId)
            {
                tipColor = color;
                model.TrySetMarkingGradient(entry.Organ, entry.Layer, entry.Prototype.ID,
                    new MarkingGradient { TipColor = tipColor, Steps = steps.SelectedId });
            }
            else
            {
                model.TrySetMarkingColor(entry.Organ, entry.Layer, entry.Prototype.ID, colorTarget.SelectedId, color);
            }
        };
        steps.OnItemSelected += args =>
        {
            var wasEnabled = steps.SelectedId != 0;
            steps.SelectId(args.Id);
            MarkingGradient? gradient = args.Id == 0 ? null : new MarkingGradient { TipColor = tipColor, Steps = args.Id };
            model.TrySetMarkingGradient(entry.Organ, entry.Layer, entry.Prototype.ID, gradient);
            if (model.GetMarking(entry.Organ, entry.Layer, entry.Prototype.ID) is not { } current)
                return;

            marking = current;
            if (wasEnabled != gradient.HasValue)
                UpdateColorTargets(gradient.HasValue ? tipId : 0);
        };

        root.ModalRoot.AddChild(popup);
        var position = button.GlobalPosition + new Vector2(0, button.Height + 2);
        popup.Open(UIBox2.FromDimensions(position, new Vector2(290, 1)));
    }

    private static Color DefaultTipColor(Color color)
    {
        var target = Color.ToHsv(color).Z < 0.6f ? Color.White : Color.Black;
        return new Color(Vector4.Lerp(color.RGBA, target.RGBA, 0.5f));
    }
}
