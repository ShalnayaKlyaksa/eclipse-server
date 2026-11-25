using System.Linq;
using System.Numerics;
using Content.Client.MainMenu.UI;
using Content.Shared.Humanoid.Markings;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.Humanoid;

public sealed partial class MarkingPicker
{
    [Dependency] private readonly ILocalizationManager _localization = default!;

    private const float ColorEditorWidth = 400f;
    private readonly PanelContainer _colorEditor;
    private MarkingEntry? _colorEntry;

    private void CloseColorEditor()
    {
        _colorEntry = null;
        _colorEditor.Visible = false;
        _colorEditor.RemoveAllChildren();
    }

    private void ValidateColorEditor()
    {
        if (_colorEntry is { } entry &&
            (!CurrentZone.Layers.Contains(entry.Layer) ||
             _markingsModel is not { } model ||
             !model.IsMarkingSelected(entry.Organ, entry.Layer, entry.Prototype.ID) ||
             !model.IsMarkingColorCustomizable(entry.Organ, entry.Layer, entry.Prototype.ID)))
            CloseColorEditor();
    }

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
        button.OnPressed += _ => OpenColorEditor(entry, model);
        return button;
    }

    private void OpenColorEditor(MarkingEntry entry, MarkingsViewModel model)
    {
        var alreadyOpen = _colorEntry == entry;
        CloseColorEditor();
        if (alreadyOpen || model.GetMarking(entry.Organ, entry.Layer, entry.Prototype.ID) is not { } marking)
            return;

        _colorEntry = entry;
        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            Margin = new Thickness(10),
        };
        _colorEditor.AddChild(content);
        var header = new BoxContainer { SeparationOverride = 6 };
        var markingName = GetMarkingName(entry.Prototype);
        header.AddChild(new Label { Text = markingName, ToolTip = markingName, ClipText = true, HorizontalExpand = true });
        var close = new Button
        {
            Text = Loc.GetString("marking-colors-close"),
            StyleIdentifier = MainMenuControl.StyleIdentifierCompactButton,
        };
        close.OnPressed += _ => CloseColorEditor();
        header.AddChild(close);
        content.AddChild(header);

        var steps = new OptionButton { HorizontalExpand = true, ClipText = true };
        steps.AddItem(Loc.GetString("marking-gradient-off"), 0);
        for (var i = MarkingGradient.MinSteps; i <= MarkingGradient.MaxSteps; i++)
            steps.AddItem(Loc.GetString("marking-gradient-steps", ("steps", i)), i);
        steps.SelectId(marking.Gradient?.Normalized().Steps ?? 0);
        content.AddChild(steps);

        var gradientSettings = marking.Gradient?.Normalized() ?? new MarkingGradient
        {
            TipColor = DefaultTipColor(marking.MarkingColors[0]),
        };
        var stops = gradientSettings.Stops?.ToList() ?? [];
        var geometry = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
        content.AddChild(geometry);
        var shape = new OptionButton { HorizontalExpand = true, ClipText = true };
        foreach (var value in Enum.GetValues<MarkingGradientShape>())
            shape.AddItem(Loc.GetString($"marking-gradient-shape-{value.ToString().ToLowerInvariant()}"), (int) value);
        shape.SelectId((int) gradientSettings.Shape);
        geometry.AddChild(shape);

        var range = new BoxContainer { SeparationOverride = 6 };
        range.AddChild(GradientNumberField("start", 0, 100, gradientSettings.EffectiveStart, out var start));
        range.AddChild(GradientNumberField("length", 1, 100, gradientSettings.EffectiveLength, out var length));
        var angleField = GradientNumberField("angle", 0, 359, gradientSettings.EffectiveAngle, out var angle);
        angle.LineEditControl.ToolTip = Loc.GetString("marking-gradient-angle-hint");
        range.AddChild(angleField);
        geometry.AddChild(range);

        var center = new BoxContainer { SeparationOverride = 6 };
        center.AddChild(GradientNumberField("center-x", 0, 100, gradientSettings.CenterX, out var centerX));
        center.AddChild(GradientNumberField("center-y", 0, 100, gradientSettings.CenterY, out var centerY));
        geometry.AddChild(center);

        var strip = new BoxContainer { MinHeight = 10 };
        var swatches = new StyleBoxFlat[20];
        for (var i = 0; i < swatches.Length; i++)
        {
            swatches[i] = new StyleBoxFlat(Color.Transparent);
            strip.AddChild(new PanelContainer { HorizontalExpand = true, PanelOverride = swatches[i] });
        }
        geometry.AddChild(strip);

        var targetRow = new BoxContainer { SeparationOverride = 4 };
        var colorTarget = new OptionButton { HorizontalExpand = true, ClipText = true };
        targetRow.AddChild(colorTarget);
        var addStop = new Button { Text = "+", ToolTip = Loc.GetString("marking-gradient-add-color") };
        var removeStop = new Button { Text = "−", ToolTip = Loc.GetString("marking-gradient-remove-color") };
        targetRow.AddChild(addStop);
        targetRow.AddChild(removeStop);
        content.AddChild(targetRow);
        var positionField = GradientNumberField("stop-position", 1, 99, 50, out var position);
        content.AddChild(positionField);
        var selector = new ColorSelectorSliders { SelectorType = ColorSelectorSliders.ColorSelectorType.Hsv };
        content.AddChild(selector);

        const int tipId = -1;
        int SelectedStop() => tipId - 1 - colorTarget.SelectedId;

        void RefreshSwatches()
        {
            if (steps.SelectedId == 0 || model.GetMarking(entry.Organ, entry.Layer, entry.Prototype.ID) is not { } current)
                return;

            var layer = Math.Max(colorTarget.SelectedId, 0);
            var ramp = new MarkingGradientBaker.GradientRamp(current.MarkingColors[layer], gradientSettings);
            for (var i = 0; i < swatches.Length; i++)
                swatches[i].BackgroundColor = ramp.Colors[ramp.GetBand((float) i / (swatches.Length - 1))];
        }

        void SaveGradient()
        {
            var enabled = steps.SelectedId != 0;
            gradientSettings.Stops = stops.Count > 0 ? stops.OrderBy(stop => stop.Position).ToArray() : null;
            model.TrySetMarkingGradient(entry.Organ, entry.Layer, entry.Prototype.ID, enabled ? gradientSettings : null);
            RefreshSwatches();
        }

        void UpdateTarget(int selected)
        {
            colorTarget.SelectId(selected);
            var stopIndex = SelectedStop();
            positionField.Visible = stopIndex >= 0;
            removeStop.Disabled = stopIndex < 0;
            if (stopIndex >= 0)
            {
                position.IsValid = value => value is > 0 and < 100 &&
                    !stops.Where((_, index) => index != stopIndex).Any(stop => stop.Position == value);
                position.OverrideValue(stops[stopIndex].Position);
                selector.Color = stops[stopIndex].Color;
            }
            else if (selected == tipId)
                selector.Color = gradientSettings.TipColor;
            else if (model.GetMarking(entry.Organ, entry.Layer, entry.Prototype.ID) is { } current)
                selector.Color = current.MarkingColors[selected];

            RefreshSwatches();
        }

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

            var enabled = steps.SelectedId != 0;
            geometry.Visible = enabled;
            center.Visible = gradientSettings.Shape == MarkingGradientShape.Radial;
            angleField.Visible = !center.Visible;
            addStop.Visible = removeStop.Visible = enabled;
            addStop.Disabled = stops.Count >= MarkingGradient.MaxExtraColors;
            if (enabled)
            {
                foreach (var (stop, index) in stops.Select((stop, index) => (stop, index)).OrderBy(pair => pair.stop.Position))
                    colorTarget.AddItem(StopLabel(stop.Position), tipId - 1 - index);
                colorTarget.AddItem(Loc.GetString("marking-gradient-tip"), tipId);
            }
            colorTarget.Disabled = colorTarget.ItemCount <= 1;
            UpdateTarget(selected);
        }

        UpdateColorTargets(0);
        shape.OnItemSelected += args =>
        {
            shape.SelectId(args.Id);
            gradientSettings.Shape = (MarkingGradientShape) args.Id;
            center.Visible = gradientSettings.Shape == MarkingGradientShape.Radial;
            angleField.Visible = !center.Visible;
            start.OverrideValue(gradientSettings.EffectiveStart);
            length.OverrideValue(gradientSettings.EffectiveLength);
            SaveGradient();
        };
        BindNumber(start, value => gradientSettings.Start = value);
        BindNumber(length, value => gradientSettings.Length = value);
        BindNumber(angle, value => gradientSettings.AngleDegrees = value);
        BindNumber(centerX, value => gradientSettings.CenterX = value);
        BindNumber(centerY, value => gradientSettings.CenterY = value);
        position.ValueChanged += args =>
        {
            var index = SelectedStop();
            if (index < 0 || position.Value != args.Value || stops[index].Position == args.Value)
                return;

            stops[index] = stops[index] with { Position = args.Value };
            colorTarget.SetItemText(colorTarget.GetIdx(colorTarget.SelectedId), StopLabel(args.Value));
            SaveGradient();
        };
        colorTarget.OnItemSelected += args => UpdateTarget(args.Id);
        selector.OnColorChanged += color =>
        {
            var index = SelectedStop();
            if (index >= 0)
            {
                stops[index] = stops[index] with { Color = color };
                SaveGradient();
            }
            else if (colorTarget.SelectedId == tipId)
            {
                gradientSettings.TipColor = color;
                SaveGradient();
            }
            else
            {
                model.TrySetMarkingColor(entry.Organ, entry.Layer, entry.Prototype.ID, colorTarget.SelectedId, color);
                RefreshSwatches();
            }
        };
        addStop.OnPressed += _ =>
        {
            var root = model.GetMarking(entry.Organ, entry.Layer, entry.Prototype.ID)?.MarkingColors[0] ?? Color.White;
            var colors = stops.OrderBy(stop => stop.Position)
                .Prepend(new MarkingGradientStop { Position = 0, Color = root })
                .Append(new MarkingGradientStop { Position = 100, Color = gradientSettings.TipColor }).ToArray();
            var largestGap = 0;
            for (var i = 1; i < colors.Length - 1; i++)
            {
                if (colors[i + 1].Position - colors[i].Position > colors[largestGap + 1].Position - colors[largestGap].Position)
                    largestGap = i;
            }

            var from = colors[largestGap];
            var to = colors[largestGap + 1];
            stops.Add(new MarkingGradientStop
            {
                Position = (from.Position + to.Position) / 2,
                Color = Color.InterpolateBetween(from.Color, to.Color, 0.5f),
            });
            SaveGradient();
            UpdateColorTargets(tipId - stops.Count);
        };
        removeStop.OnPressed += _ =>
        {
            stops.RemoveAt(SelectedStop());
            UpdateColorTargets(tipId);
            SaveGradient();
        };
        steps.OnItemSelected += args =>
        {
            var wasEnabled = steps.SelectedId != 0;
            steps.SelectId(args.Id);
            var enabled = args.Id != 0;
            if (enabled)
                gradientSettings.Steps = args.Id;
            SaveGradient();
            if (model.GetMarking(entry.Organ, entry.Layer, entry.Prototype.ID) is not { } current)
                return;

            marking = current;
            if (wasEnabled != enabled)
                UpdateColorTargets(enabled ? tipId : 0);
        };

        _colorEditor.Visible = true;

        void BindNumber(SpinBox field, Action<int> update)
        {
            field.ValueChanged += args =>
            {
                if (field.Value != args.Value)
                    return;
                update(args.Value);
                SaveGradient();
            };
        }
    }

    private static string StopLabel(int position) => Loc.GetString("marking-gradient-color-stop", ("position", position));

    private static BoxContainer GradientNumberField(string key, int min, int max, int value, out SpinBox input)
    {
        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        box.AddChild(new Label { Text = Loc.GetString($"marking-gradient-{key}"), ClipText = true });
        input = new SpinBox { IsValid = number => number >= min && number <= max };
        input.OverrideValue(value);
        input.InitDefaultButtons();
        box.AddChild(input);
        return box;
    }

    private static Color DefaultTipColor(Color color)
    {
        var target = Color.ToHsv(color).Z < 0.6f ? Color.White : Color.Black;
        return new Color(Vector4.Lerp(color.RGBA, target.RGBA, 0.5f));
    }
}
