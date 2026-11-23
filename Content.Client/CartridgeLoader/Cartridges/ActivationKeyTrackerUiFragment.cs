using Content.Client.Pinpointer.UI;
using Content.Client.PDA;
using Content.Shared.CartridgeLoader.Cartridges;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;

namespace Content.Client.CartridgeLoader.Cartridges;

public sealed class ActivationKeyTrackerUiFragment : BoxContainer
{
    private static readonly Color KeyColor = Color.FromHex("#ff8b24");
    private readonly IEntityManager _entityManager;
    private readonly NavMapControl _map;
    private EntityUid? _currentGrid;
    private readonly Label _status = new() { ClipText = true, StyleClasses = { "terminal-muted" } };
    private readonly Button _center = PdaAppUi.Action(Loc.GetString("navmap-recenter"));
    private EntityCoordinates? _keyCoordinates;

    public ActivationKeyTrackerUiFragment()
    {
        _entityManager = IoCManager.Resolve<IEntityManager>();

        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        SeparationOverride = 4;
        _status.HorizontalExpand = true;
        AddChild(new BoxContainer { SeparationOverride = 4, Children = { _status, _center } });

        _map = new NavMapControl
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            ShowControls = false,
            AutoUpdate = false,
            TrackedCoordinatesBlink = false,
        };

        var mapPanel = PdaAppUi.Card(_map); mapPanel.HorizontalExpand = mapPanel.VerticalExpand = true; AddChild(mapPanel);
        _center.OnPressed += _ => { if (_keyCoordinates is { } coordinates) _map.CenterToCoordinates(coordinates); };
    }

    public void UpdateState(ActivationKeyTrackerUiState state)
    {
        var grid = _entityManager.GetEntity(state.Grid);
        _status.Text = Loc.GetString(state.KeyPosition != null ? "pda-tracker-found" : "pda-tracker-missing");
        _keyCoordinates = grid is { } trackedGrid && state.KeyPosition is { } position ? new EntityCoordinates(trackedGrid, position) : null;
        _center.Disabled = _keyCoordinates == null;
        if (_currentGrid != grid)
        {
            _currentGrid = grid;
            _map.MapUid = grid;
            _map.ForceNavMapUpdate();
        }

        _map.TrackedCoordinates.Clear();

        if (grid is { } mapGrid && state.KeyPosition is { } keyPosition)
        {
            _map.TrackedCoordinates.Add(
                new EntityCoordinates(mapGrid, keyPosition),
                (true, KeyColor));
        }
    }
}
