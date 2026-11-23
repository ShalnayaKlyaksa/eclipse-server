using System.Linq;
using System.Numerics;
using Content.Shared._Erida.Research;
using Content.Client.Resources;
using Robust.Client.ResourceManagement;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client._Erida.Research.UI;

/// <summary>Cached prerequisite routes, drawn behind the interactive research cards.</summary>
public sealed partial class ResearchesContainerPanel : LayoutContainer
{
    public Action<float>? ZoomRequested;
    public string? SelectedTechnology;
    public Vector2 GraphOrigin;
    public float GraphZoom = .85f;
    private readonly Font _font = IoCManager.Resolve<IResourceCache>().GetFont("/Fonts/RobotoMono/RobotoMono-Regular.ttf", 9);
    public IReadOnlyDictionary<string, Vector2> NodePositions = new Dictionary<string, Vector2>();
    private readonly List<(Vector2 Position, string Name)> _groups = new();
    private readonly List<(FancyResearchConsoleItem From, FancyResearchConsoleItem To)> _connections = new();
    private float _phase;
    private readonly Vector2[] _route = new Vector2[6];

    public void RebuildConnections()
    {
        _connections.Clear();
        var lookup = Children.OfType<FancyResearchConsoleItem>().ToDictionary(x => x.Prototype.ID);
        _groups.Clear();
        foreach (var group in lookup.Values.GroupBy(x => ResearchMapLayout.Group(x.Prototype).ToLowerInvariant()))
        {
            var position = new Vector2(group.Min(x => NodePositions[x.Prototype.ID].X), group.Min(x => NodePositions[x.Prototype.ID].Y));
            var name = group.Key switch
            {
                "factory" => Loc.GetString("research-map-group-factory"),
                "industrial" => Loc.GetString("research-map-group-engineering"),
                "civilianservices" => Loc.GetString("research-discipline-civilian-services"),
                _ => Loc.GetString("research-discipline-" + group.Key),
            };
            _groups.Add((position, name));
        }
        foreach (var item in lookup.Values)
        foreach (var prerequisite in item.Prototype.TechnologyPrerequisites)
            if (lookup.TryGetValue(prerequisite.Id, out var source)) _connections.Add((source, item));
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        ZoomRequested?.Invoke(args.Delta.Y);
        args.Handle();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _phase = (_phase + MathF.Min(args.DeltaSeconds, .1f) * .22f) % 1;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        handle.DrawRect(new UIBox2(Vector2.Zero, PixelSize), Color.FromHex("#060E0A"));
        var line = Color.FromHex("#244A3438");
        var step = MathF.Max(28, 256 * GraphZoom) * UIScale;
        var offset = GraphOrigin * UIScale;
        // Survey lines and edge graduations follow the same coordinates as the research graph.
        for (var x = (offset.X % step + step) % step; x < PixelWidth; x += step)
        {
            handle.DrawLine(new Vector2(x, 24 * UIScale), new Vector2(x, PixelHeight - 8 * UIScale), line);
            handle.DrawLine(new Vector2(x, 8 * UIScale), new Vector2(x, 16 * UIScale), Color.FromHex("#487657"));
        }
        for (var y = (offset.Y % step + step) % step; y < PixelHeight; y += step)
        {
            handle.DrawLine(new Vector2(20 * UIScale, y), new Vector2(PixelWidth - 8 * UIScale, y), line);
            handle.DrawLine(new Vector2(5 * UIScale, y), new Vector2(12 * UIScale, y), Color.FromHex("#487657"));
        }
        handle.DrawRect(new UIBox2(1, 1, PixelWidth - 1, PixelHeight - 1), Color.FromHex("#2E523C"), false);
        foreach (var (position, name) in _groups)
        {
            var anchor = (GraphOrigin + position * GraphZoom) * UIScale - new Vector2(0, 12 * UIScale);
            handle.DrawString(_font, anchor, name, UIScale, Color.FromHex("#A1C4A8"));
        }

        var route = _route;
        foreach (var (source, target) in _connections)
        {
            var start = source.ConnectionAnchor(true);
            var end = target.ConnectionAnchor(false);
            var selected = source.Prototype.ID == SelectedTechnology || target.Prototype.ID == SelectedTechnology;
            if (!selected && ResearchMapLayout.Group(source.Prototype) != ResearchMapLayout.Group(target.Prototype)) continue;
            var active = source.Availability == ResearchAvailability.Researched;
            var color = Color.FromHex(selected ? "#B2D79E" : active ? "#497C55" : "#35523E");
            route[0] = start;
            var count = 2;
            // Adjacent cards share a straight trace. Long routes use the empty gutters
            // between rows and columns, so a connection cannot run through a third card.
            if (MathF.Abs(start.Y - end.Y) < UIScale && end.X > start.X && end.X - start.X < 256 * GraphZoom * UIScale)
                route[1] = end;
            else
            {
                var gutter = MathF.Max(2, (256 * GraphZoom * UIScale - source.PixelWidth) * .45f);
                var row = source.PixelPosition.Y + source.PixelHeight + MathF.Max(2, (156 * GraphZoom * UIScale - source.PixelHeight) * .25f);
                route[1] = new Vector2(source.PixelPosition.X + source.PixelWidth + gutter, start.Y);
                route[2] = new Vector2(route[1].X, row);
                route[3] = new Vector2(target.PixelPosition.X - gutter, row);
                route[4] = new Vector2(route[3].X, end.Y);
                route[5] = end;
                count = 6;
            }
            var length = 0f;
            for (var i = 1; i < count; i++)
            {
                handle.DrawLine(route[i - 1], route[i], color);
                length += Vector2.Distance(route[i - 1], route[i]);
            }
            if (GraphZoom >= .3f)
            {
                handle.DrawLine(end + new Vector2(-4, -3) * UIScale, end, color);
                handle.DrawLine(end + new Vector2(-4, 3) * UIScale, end, color);
            }
            if (!active) continue;
            var distance = _phase * length;
            for (var i = 1; i < count; i++)
            {
                var segment = Vector2.Distance(route[i - 1], route[i]);
                if (distance > segment) { distance -= segment; continue; }
                var signal = Vector2.Lerp(route[i - 1], route[i], segment <= 0 ? 0 : distance / segment);
                handle.DrawRect(new UIBox2(signal - new Vector2(2), signal + new Vector2(2)), Color.FromHex("#9BC893"));
                break;
            }
        }
    }
}
