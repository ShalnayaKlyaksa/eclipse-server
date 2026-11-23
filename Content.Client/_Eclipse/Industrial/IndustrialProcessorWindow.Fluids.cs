using System.Linq;
using Content.Client.Stylesheets;
using Content.Client.Resources;
using System.Numerics;
using Content.Shared._Eclipse.Industrial;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Eclipse.Industrial;

public sealed partial class IndustrialProcessorWindow
{
    private BoxContainer _fluidPanel = default!;
    private readonly List<(string Id, float Required, Label Label)> _fluidCounters = new();
    private Texture? _fluidMarker;

    private BoxContainer CreateFluidPanel()
    {
        _fluidPanel = Column();
        _fluidPanel.Name = "FluidTanks";
        _fluidPanel.Visible = false;
        return _fluidPanel;
    }

    private TextureRect FluidSwatch(Color color, int size)
    {
        // Reuse the actual pipe indicator, cropped to its four white pixels. No generated textures.
        _fluidMarker ??= new AtlasTexture(_resources.GetTexture(
            "/Textures/_Eclipse/Structures/Piping/liquid_pipe_parts/content.png"), new UIBox2(14, 14, 18, 18));
        return new TextureRect
        {
            Texture = _fluidMarker, ModulateSelfOverride = color.WithAlpha(1f),
            MinSize = new Vector2(size), MaxSize = new Vector2(size), Stretch = TextureRect.StretchMode.Scale,
            VerticalAlignment = VAlignment.Center, HorizontalAlignment = HAlignment.Center,
        };
    }

    private void UpdateFluidTanks(IndustrialProcessorBoundUserInterfaceState state)
    {
        _fluidPanel.Visible = state.UsesFluids;
        if (!state.UsesFluids)
            return;
        _fluidPanel.RemoveAllChildren();
        var tanks = Row();
        AddTank(tanks, state.FluidInput, state.FluidInputCapacity, true);
        if (state.FluidOutputCapacity > 0 && !state.IsBuffer &&
            (state.FluidOutput.Length > 0 || state.Recipes.Any(r => r.FluidOutputs.Length > 0)))
            AddTank(tanks, state.FluidOutput, state.FluidOutputCapacity, false);
        _fluidPanel.AddChild(tanks);
        _fluidPanel.ToolTip = Loc.GetString("industrial-ui-fluid-help");
        _fluidPanel.AddChild(Divider());
    }

    private void AddTank(BoxContainer row, IndustrialFluidSlotState[] contents, float capacity, bool input)
    {
        var tank = Column();
        tank.SeparationOverride = 4;
        var total = contents.Sum(c => c.Amount);
        var heading = Row();
        var caption = Caption(_state is { IsBuffer: true } ? "industrial-ui-fluid-tank" : input ? "industrial-ui-fluid-input" : "industrial-ui-fluid-output");
        caption.HorizontalExpand = true;
        heading.AddChild(caption);
        heading.AddChild(new Label { Text = $"{total:0.#} / {capacity:0.#} u", FontColorOverride = Muted, StyleClasses = { StyleClass.FontSmall } });
        tank.AddChild(heading);
        tank.AddChild(new ProgressBar
        {
            MinHeight = 4, MaxValue = MathF.Max(1, capacity), Value = total,
            BackgroundStyleBoxOverride = new StyleBoxFlat(Color.FromHex("#111519")),
            ForegroundStyleBoxOverride = new StyleBoxFlat(contents.FirstOrDefault()?.Color ?? Muted),
        });
        foreach (var reagent in contents)
        {
            var line = Row();
            line.SeparationOverride = 6;
            line.AddChild(FluidSwatch(reagent.Color, 8));
            line.AddChild(new Label { Text = reagent.Name, ToolTip = reagent.Name, ClipText = true, HorizontalExpand = true, FontColorOverride = Ink, StyleClasses = { StyleClass.FontSmall } });
            line.AddChild(new Label { Text = $"{reagent.Amount:0.#} u", FontColorOverride = Muted, StyleClasses = { StyleClass.FontSmall } });
            tank.AddChild(line);
        }
        row.AddChild(tank);
    }

    private void AddFluidMaterial(BoxContainer list, IndustrialFluidSlotState reagent, bool input, bool bath, float extra)
    {
        var row = Row();
        row.SeparationOverride = 5;
        row.AddChild(FluidSwatch(reagent.Color, 8));
        var name = bath ? SentenceCase(Loc.GetString("industrial-ui-bath")) + ": " + reagent.Name : reagent.Name;
        row.ToolTip = name + $" ({reagent.Amount:0.#} u)" + (bath ? "\n" + Loc.GetString("industrial-ui-bath-help") : "");
        row.AddChild(new Label { Text = name, ClipText = true, HorizontalExpand = true, FontColorOverride = Ink, StyleClasses = { StyleClass.FontSmall } });
        var amount = new Label { Text = $"{reagent.Amount:0.#} u", FontColorOverride = Ink, StyleClasses = { "industrial-number" } };
        row.AddChild(amount);
        list.AddChild(row);
        if (input)
            _fluidCounters.Add((reagent.Id, reagent.Amount + extra, amount));
    }

    private void UpdateFluidCounters()
    {
        if (_state == null)
            return;
        foreach (var (id, required, label) in _fluidCounters)
        {
            var stored = _state.FluidInput.Where(f => f.Id == id).Sum(f => f.Amount);
            label.Text = $"{stored:0.#} / {required:0.#} u";
            label.FontColorOverride = stored >= required ? Ready : Amber;
        }
    }
}
