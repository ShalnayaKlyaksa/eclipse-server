using System.Linq;
using Content.Client.MainMenu.UI;
using Content.Client.Stylesheets;
using Content.Shared._Eclipse.Lobby;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

public sealed class LobbyRoadmapBoard : ScrollContainer
{
    private const float StackBelowWidth = 900;
    private const int ColumnGap = 14;
    private const float ScrollBarRoom = 12;
    private static readonly Color ColumnBackground = Color.FromHex("#141414");
    private static readonly Color ColumnBorder = Color.FromHex("#333333");

    private static readonly ProtoId<EclipseLobbyRoadmapPrototype> RoadmapId = "EclipseLobbyRoadmap";
    private readonly IReadOnlyList<LobbyRoadmapMilestone> _milestones;
    private readonly BoxContainer _columns = new() { SeparationOverride = ColumnGap };

    public string ProgressText => Loc.GetString("eclipse-roadmap-progress",
        ("done", _milestones.Count(milestone => milestone.Status == LobbyRoadmapStatus.Available)),
        ("total", _milestones.Count));

    public LobbyRoadmapBoard()
    {
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        _milestones = prototypes.TryIndex(RoadmapId, out var roadmap) ? roadmap.Milestones : [];
        HScrollEnabled = false;
        AddChild(_columns);
        _columns.AddChild(BuildColumn(LobbyRoadmapStatus.Available, "eclipse-roadmap-available", Color.FromHex("#87D0AB")));
        _columns.AddChild(BuildColumn(LobbyRoadmapStatus.InProgress, "eclipse-roadmap-in-progress", Color.FromHex("#F0B16C")));
        _columns.AddChild(BuildColumn(LobbyRoadmapStatus.Planned, "eclipse-roadmap-planned", Color.FromHex("#A6C2E2")));
    }

    protected override void Resized()
    {
        base.Resized();
        var stacked = Width < StackBelowWidth;
        _columns.Orientation = stacked
            ? BoxContainer.LayoutOrientation.Vertical
            : BoxContainer.LayoutOrientation.Horizontal;

        var columnWidth = (Width - ScrollBarRoom - ColumnGap * (_columns.ChildCount - 1)) / _columns.ChildCount;
        foreach (var column in _columns.Children)
            column.MaxWidth = stacked ? float.PositiveInfinity : columnWidth;
    }

    private Control BuildColumn(LobbyRoadmapStatus status, string heading, Color accent)
    {
        var milestones = _milestones.Where(milestone => milestone.Status == status).ToArray();
        var column = new PanelContainer
        {
            PanelOverride = ForgeStyle.Panel(ColumnBackground, ColumnBorder, 12, 12),
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Top,
        };
        var stack = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        column.AddChild(stack);

        var header = new BoxContainer { SeparationOverride = 8, Margin = new Thickness(2, 0, 2, 4) };
        header.AddChild(new PanelContainer
        {
            SetSize = new(3, 16),
            VerticalAlignment = VAlignment.Center,
            PanelOverride = ForgeStyle.Panel(accent, Color.Transparent),
        });
        header.AddChild(new Label
        {
            Text = Loc.GetString(heading),
            StyleIdentifier = MainMenuControl.StyleIdentifierRoadmapItemTitle,
            FontColorOverride = Color.FromHex("#F0F0F0"),
            HorizontalExpand = true,
            ClipText = true,
        });
        header.AddChild(new Label
        {
            Text = milestones.Length.ToString(),
            StyleIdentifier = MainMenuControl.StyleIdentifierGoldSmall,
            FontColorOverride = accent,
        });
        stack.AddChild(header);

        foreach (var milestone in milestones)
            stack.AddChild(BuildCard(milestone));

        return column;
    }

    private static Control BuildCard(LobbyRoadmapMilestone milestone)
    {
        var card = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        var title = Loc.GetString($"eclipse-roadmap-{milestone.Id}-title");
        var button = new Button
        {
            StyleIdentifier = MainMenuControl.StyleIdentifierSecondary,
            MinHeight = 48,
            ToggleMode = true,
            ToolTip = title,
        };
        var header = new BoxContainer
        {
            Margin = new Thickness(10, 0),
            SeparationOverride = 8,
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };
        header.AddChild(new Label { Text = title, ClipText = true, HorizontalExpand = true, MouseFilter = MouseFilterMode.Ignore });
        var arrow = new Label { Text = "›", FontColorOverride = Color.FromHex("#BEBEBE"), MouseFilter = MouseFilterMode.Ignore };
        header.AddChild(arrow);
        button.AddChild(header);
        card.AddChild(button);

        var details = new PanelContainer
        {
            Visible = false,
            PanelOverride = ForgeStyle.Panel(ColumnBackground, ColumnBorder, 10, 10),
        };
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        var description = new RichTextLabel();
        description.SetMessage(Loc.GetString($"eclipse-roadmap-{milestone.Id}-description"));
        body.AddChild(description);
        if (milestone.LongTerm)
            body.AddChild(DetailLabel(Loc.GetString("eclipse-roadmap-long-term")));
        if (milestone.Previous is { } previous)
        {
            body.AddChild(DetailLabel(Loc.GetString("eclipse-roadmap-previous",
                ("title", Loc.GetString($"eclipse-roadmap-{previous}-title")))));
        }
        details.AddChild(body);
        card.AddChild(details);

        button.OnToggled += args =>
        {
            details.Visible = args.Pressed;
            arrow.Text = args.Pressed ? "⌄" : "›";
        };
        return card;
    }

    private static RichTextLabel DetailLabel(string text)
    {
        var label = new RichTextLabel();
        label.SetMessage(text, Color.FromHex("#B3B3B3"));
        return label;
    }
}
