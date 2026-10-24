using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Eclipse.Industrial;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Eclipse.Industrial;

public sealed class IndustrialProcessorWindow : FancyWindow
{
    private readonly Label _tierValue;
    private readonly Label _stateValue;
    private readonly Label _recipeValue;
    private readonly Label _powerValue;
    private readonly ProgressBar _progressBar;
    private readonly EntityPrototypeView _processingView;
    private readonly Label _processingLabel;
    private readonly GridContainer _inputGrid;
    private readonly GridContainer _outputGrid;
    private readonly Label _portNorth;
    private readonly Label _portSouth;
    private readonly Label _portEast;
    private readonly Label _portWest;

    private readonly List<Button> _inputButtons = new();
    private readonly List<Button> _outputButtons = new();

    public event Action<bool, int>? OnSlotPressed;

    public IndustrialProcessorWindow()
    {
        Title = Loc.GetString("industrial-processor-ui-title");
        MinSize = SetSize = new Vector2(420, 460);

        var root = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Margin = new Thickness(8),
            SeparationOverride = 6,
        };

        _tierValue = AddStatusRow(root, "industrial-processor-ui-tier");
        _stateValue = AddStatusRow(root, "industrial-processor-ui-state");
        _recipeValue = AddStatusRow(root, "industrial-processor-ui-recipe");
        _powerValue = AddStatusRow(root, "industrial-processor-ui-power");

        _progressBar = new ProgressBar
        {
            MinSize = new Vector2(0, 18),
            HorizontalExpand = true,
        };
        root.AddChild(_progressBar);

        root.AddChild(new Label { Text = Loc.GetString("industrial-processor-ui-processing-slot") });

        var processingRow = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 8,
        };

        _processingView = new EntityPrototypeView
        {
            MinSize = new Vector2(48, 48),
            MaxSize = new Vector2(48, 48),
            Visible = false,
        };
        processingRow.AddChild(_processingView);

        _processingLabel = new Label
        {
            VerticalAlignment = VAlignment.Center,
            HorizontalExpand = true,
            Text = Loc.GetString("industrial-processor-ui-processing-empty"),
        };
        processingRow.AddChild(_processingLabel);
        root.AddChild(processingRow);

        root.AddChild(new Label { Text = Loc.GetString("industrial-processor-ui-input-slots") });
        _inputGrid = new GridContainer { Columns = 4, HSeparationOverride = 4, VSeparationOverride = 4 };
        root.AddChild(_inputGrid);

        root.AddChild(new Label { Text = Loc.GetString("industrial-processor-ui-output-slots") });
        _outputGrid = new GridContainer { Columns = 4, HSeparationOverride = 4, VSeparationOverride = 4 };
        root.AddChild(_outputGrid);

        root.AddChild(new Label { Text = Loc.GetString("industrial-processor-ui-ports") });
        _portNorth = AddPortRow(root, "industrial-direction-north");
        _portSouth = AddPortRow(root, "industrial-direction-south");
        _portEast = AddPortRow(root, "industrial-direction-east");
        _portWest = AddPortRow(root, "industrial-direction-west");

        ContentsContainer.AddChild(root);
    }

    public void UpdateState(IndustrialProcessorBoundUserInterfaceState state)
    {
        Title = state.MachineName;
        _tierValue.Text = state.TierName;
        _stateValue.Text = Loc.GetString(state.StateKey);
        _recipeValue.Text = state.CurrentRecipeName ?? Loc.GetString("industrial-processor-ui-no-recipe");
        _powerValue.Text = state.UsesHeat
            ? Loc.GetString(state.HasSufficientHeat
                ? "industrial-processor-ui-heated"
                : "industrial-processor-ui-cold")
            : Loc.GetString(state.IsPowered
                ? "industrial-processor-ui-powered"
                : "industrial-processor-ui-unpowered");
        _progressBar.Value = state.Progress;

        UpdateProcessingSlot(state.ProcessingSlot);

        RebuildSlots(_inputGrid, _inputButtons, state.InputSlots, state.MaxInputSlots, true);
        RebuildSlots(_outputGrid, _outputButtons, state.OutputSlots, state.MaxOutputSlots, false);

        _portNorth.Text = SharedIndustrialProcessorSystem.GetFacePortName(state.NorthFacePort);
        _portSouth.Text = SharedIndustrialProcessorSystem.GetFacePortName(state.SouthFacePort);
        _portEast.Text = SharedIndustrialProcessorSystem.GetFacePortName(state.EastFacePort);
        _portWest.Text = SharedIndustrialProcessorSystem.GetFacePortName(state.WestFacePort);
    }

    private void UpdateProcessingSlot(IndustrialProcessorSlotState slot)
    {
        if (slot.PrototypeId == null)
        {
            _processingView.Visible = false;
            _processingView.SetPrototype(null);
            _processingLabel.Text = Loc.GetString("industrial-processor-ui-processing-empty");
            return;
        }

        _processingView.Visible = true;
        _processingView.SetPrototype(slot.PrototypeId);
        _processingLabel.Text = slot.Count > 1
            ? $"{slot.DisplayName} ×{slot.Count}"
            : slot.DisplayName;
    }

    private void RebuildSlots(
        GridContainer grid,
        List<Button> buttons,
        IndustrialProcessorSlotState[] slots,
        int maxSlots,
        bool isInput)
    {
        if (buttons.Count == maxSlots)
        {
            for (var i = 0; i < maxSlots; i++)
                UpdateSlotButton(buttons[i], slots[i]);
            return;
        }

        grid.RemoveAllChildren();
        buttons.Clear();

        for (var i = 0; i < maxSlots; i++)
        {
            var index = i;
            var button = new Button
            {
                MinSize = new Vector2(88, 36),
                TextAlign = Label.AlignMode.Center,
            };
            button.OnPressed += _ => OnSlotPressed?.Invoke(isInput, index);
            UpdateSlotButton(button, slots[i]);
            grid.AddChild(button);
            buttons.Add(button);
        }
    }

    private static void UpdateSlotButton(Button button, IndustrialProcessorSlotState slot)
    {
        if (slot.PrototypeId == null)
        {
            button.Text = "—";
            button.Disabled = false;
            return;
        }

        button.Text = slot.Count > 1 ? $"{slot.DisplayName}\n×{slot.Count}" : slot.DisplayName;
    }

    private static Label AddStatusRow(BoxContainer root, string labelKey)
    {
        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        row.AddChild(new Label
        {
            Text = Loc.GetString(labelKey),
            MinSize = new Vector2(120, 0),
        });

        var value = new Label
        {
            HorizontalExpand = true,
            Align = Label.AlignMode.Right,
        };
        row.AddChild(value);
        root.AddChild(row);
        return value;
    }

    private static Label AddPortRow(BoxContainer root, string directionKey)
    {
        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        row.AddChild(new Label
        {
            Text = Loc.GetString(directionKey),
            MinSize = new Vector2(120, 0),
        });

        var value = new Label
        {
            HorizontalExpand = true,
            Align = Label.AlignMode.Right,
        };
        row.AddChild(value);
        root.AddChild(row);
        return value;
    }
}
