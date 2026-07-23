using Content.Client.Construction;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Construction.Prototypes;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.Placement.Modes;
using Robust.Client.Utility;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System.Numerics;
using static Robust.Client.Placement.PlacementManager;

namespace Content.Client._Eclipse.Industrial;

/// <summary>
/// Places item pipes on different layers within a tile, similar to atmos pipes.
/// </summary>
public sealed class AlignItemPipeLayers : SnapgridCenter
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;

    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _transformSystem;
    private readonly SharedItemPipeLayersSystem _pipeLayersSystem;
    private readonly SpriteSystem _spriteSystem;

    private const float SearchBoxSize = 2f;
    private EntityCoordinates _unalignedMouseCoords = default;
    private const float MouseDeadzoneRadius = 0.25f;

    private readonly Color _guideColor = new(0.9f, 0.45f, 0.05f, 0.85f);
    private const float GuideRadius = 0.1f;
    private const float GuideOffset = 0.21875f;

    public AlignItemPipeLayers(PlacementManager pMan) : base(pMan)
    {
        IoCManager.InjectDependencies(this);

        _mapSystem = _entityManager.System<SharedMapSystem>();
        _transformSystem = _entityManager.System<SharedTransformSystem>();
        _pipeLayersSystem = _entityManager.System<SharedItemPipeLayersSystem>();
        _spriteSystem = _entityManager.System<SpriteSystem>();
    }

    public override void Render(in OverlayDrawArgs args)
    {
        var gridUid = _entityManager.System<SharedTransformSystem>().GetGrid(MouseCoords);
        if (gridUid == null || Grid == null)
            return;

        if (pManager.PlacementType == PlacementTypes.None)
        {
            var gridRotation = _transformSystem.GetWorldRotation(gridUid.Value);
            var worldPosition = _mapSystem.LocalToWorld(gridUid.Value, Grid, MouseCoords.Position);
            var direction = (_eyeManager.CurrentEye.Rotation + gridRotation + Math.PI / 2).GetCardinalDir();
            var multi = direction is Direction.North or Direction.South ? -1f : 1f;

            args.WorldHandle.DrawCircle(worldPosition, GuideRadius, _guideColor);
            args.WorldHandle.DrawCircle(worldPosition + gridRotation.RotateVec(new Vector2(multi * GuideOffset, GuideOffset)), GuideRadius, _guideColor);
            args.WorldHandle.DrawCircle(worldPosition - gridRotation.RotateVec(new Vector2(multi * GuideOffset, GuideOffset)), GuideRadius, _guideColor);
        }

        base.Render(args);
    }

    public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
    {
        _unalignedMouseCoords = ScreenToCursorGrid(mouseScreen);
        base.AlignPlacementMode(mouseScreen);

        if (pManager.PlacementType != PlacementTypes.None)
            return;

        MouseCoords = _unalignedMouseCoords.AlignWithClosestGridTile(SearchBoxSize, _entityManager, _mapManager);

        var gridId = _transformSystem.GetGrid(MouseCoords);
        if (!_entityManager.TryGetComponent<MapGridComponent>(gridId, out var mapGrid))
            return;

        var gridRotation = _transformSystem.GetWorldRotation(gridId!.Value);
        CurrentTile = _mapSystem.GetTileRef(gridId.Value, mapGrid, MouseCoords);

        var tileSize = mapGrid.TileSize;
        GridDistancing = tileSize;

        MouseCoords = new EntityCoordinates(MouseCoords.EntityId,
            new Vector2(CurrentTile.X + tileSize / 2f + pManager.PlacementOffset.X,
                CurrentTile.Y + tileSize / 2f + pManager.PlacementOffset.Y));

        var mouseCoordsDiff = _unalignedMouseCoords.Position - MouseCoords.Position;
        var layer = ItemPipeLayer.Primary;

        if (mouseCoordsDiff.Length() > MouseDeadzoneRadius)
        {
            var direction = (new Angle(mouseCoordsDiff) + _eyeManager.CurrentEye.Rotation + gridRotation + Math.PI / 2).GetCardinalDir();
            layer = direction is Direction.North or Direction.East
                ? ItemPipeLayer.Secondary
                : ItemPipeLayer.Tertiary;
        }

        if (pManager.Hijack != null)
            UpdateHijackedPlacer(layer, mouseScreen);
        else
            UpdatePlacer(layer);
    }

    private void UpdateHijackedPlacer(ItemPipeLayer layer, ScreenCoordinates mouseScreen)
    {
        var constructionSystem = (pManager.Hijack as ConstructionPlacementHijack)?.CurrentConstructionSystem;
        var altPrototypes = (pManager.Hijack as ConstructionPlacementHijack)?.CurrentPrototype?.AlternativePrototypes;

        if (constructionSystem == null || altPrototypes == null || (int)layer >= altPrototypes.Length)
            return;

        var newProtoId = altPrototypes[(int)layer];
        if (!_protoManager.Resolve(newProtoId, out var newProto) || newProto.Type != ConstructionType.Structure)
            return;

        if (newProto.ID == (pManager.Hijack as ConstructionPlacementHijack)?.CurrentPrototype?.ID)
            return;

        pManager.BeginPlacing(new PlacementInformation
        {
            IsTile = false,
            PlacementOption = newProto.PlacementMode,
        }, new ConstructionPlacementHijack(constructionSystem, newProto));

        if (pManager.CurrentMode is AlignItemPipeLayers newMode)
            newMode.RefreshGrid(mouseScreen);
    }

    private void UpdatePlacer(ItemPipeLayer layer)
    {
        if (pManager.CurrentPermission?.EntityType == null)
            return;

        if (!_protoManager.TryIndex<EntityPrototype>(pManager.CurrentPermission.EntityType, out var currentProto))
            return;

        if (!currentProto.TryGetComponent<ItemPipeLayersComponent>(out var layers, _entityManager.ComponentFactory))
            return;

        if (!_pipeLayersSystem.TryGetAlternativePrototype(layers, layer, out var newProtoId))
            return;

        if (!_protoManager.TryIndex<EntityPrototype>(newProtoId, out var newProto))
            return;

        pManager.CurrentPermission.EntityType = newProtoId;

        if (newProto.TryGetComponent<SpriteComponent>(out var sprite, _entityManager.ComponentFactory))
        {
            var textures = new List<IDirectionalTextureProvider>();
            foreach (var spriteLayer in sprite.AllLayers)
            {
                if (spriteLayer.ActualRsi?.Path != null && spriteLayer.RsiState.Name != null)
                    textures.Add(_spriteSystem.RsiStateLike(new SpriteSpecifier.Rsi(spriteLayer.ActualRsi.Path, spriteLayer.RsiState.Name)));
            }

            pManager.CurrentTextures = textures;
        }
    }

    private void RefreshGrid(ScreenCoordinates mouseScreen)
    {
        base.AlignPlacementMode(mouseScreen);
    }
}
