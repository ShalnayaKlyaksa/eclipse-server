using Content.Client.Pinpointer.UI;
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

    public ActivationKeyTrackerUiFragment()
    {
        _entityManager = IoCManager.Resolve<IEntityManager>();

        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;

        _map = new NavMapControl
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            ShowControls = false,
            AutoUpdate = false,
            TrackedCoordinatesBlink = false,
        };

        AddChild(_map);
    }

    public void UpdateState(ActivationKeyTrackerUiState state)
    {
        var grid = _entityManager.GetEntity(state.Grid);
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
