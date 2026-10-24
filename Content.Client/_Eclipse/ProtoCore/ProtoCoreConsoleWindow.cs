using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Eclipse.ProtoCore;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Eclipse.ProtoCore;

public sealed class ProtoCoreConsoleWindow : FancyWindow
{
    private readonly Label _stateValue;
    private readonly Label _timeValue;
    private readonly Label _powerOutputValue;
    private readonly Label _storedEnergyValue;
    private readonly Button _startButton;
    private readonly Button _stabilizeButton;

    public event Action<ProtoCoreConsoleAction>? OnAction;

    public ProtoCoreConsoleWindow()
    {
        Title = Loc.GetString("proto-core-ui-title");
        MinSize = SetSize = new Vector2(360, 210);

        var root = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Margin = new Thickness(8),
        };

        _stateValue = AddStatusRow(root, "proto-core-ui-state");
        _timeValue = AddStatusRow(root, "proto-core-ui-time");
        _powerOutputValue = AddStatusRow(root, "proto-core-ui-power-output");
        _storedEnergyValue = AddStatusRow(root, "proto-core-ui-stored-energy");

        root.AddChild(new Control { MinSize = new Vector2(1, 10) });

        _startButton = AddActionButton(root, "proto-core-verb-start", ProtoCoreConsoleAction.Start);
        _stabilizeButton = AddActionButton(root, "proto-core-verb-stabilize", ProtoCoreConsoleAction.Stabilize);

        ContentsContainer.AddChild(root);
    }

    public void UpdateState(ProtoCoreConsoleBoundUserInterfaceState state)
    {
        _stateValue.Text = Loc.GetString($"proto-core-state-{state.State.ToString().ToLowerInvariant()}");
        _timeValue.Text = state.RemainingTime;
        _powerOutputValue.Text = state.PowerOutput;
        _storedEnergyValue.Text = state.StoredEnergy;

        _startButton.Disabled = !state.CanStart;
        _stabilizeButton.Disabled = !state.CanStabilize;
        _stabilizeButton.Text = Loc.GetString(state.NoSmesRemaining
            ? "proto-core-verb-defuse"
            : "proto-core-verb-stabilize");
    }

    private static Label AddStatusRow(BoxContainer root, string label)
    {
        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };

        row.AddChild(new Label
        {
            Text = Loc.GetString(label),
            MinSize = new Vector2(150, 0),
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

    private Button AddActionButton(BoxContainer root, string label, ProtoCoreConsoleAction action)
    {
        var button = new Button
        {
            Text = Loc.GetString(label),
            HorizontalExpand = true,
            TextAlign = Label.AlignMode.Center,
        };
        button.OnPressed += _ => OnAction?.Invoke(action);
        root.AddChild(button);
        return button;
    }
}
