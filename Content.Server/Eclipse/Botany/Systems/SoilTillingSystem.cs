using System.Linq;
using Content.Server.Botany.Components;
using Content.Server.Popups;
using Content.Shared.Botany;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.Eclipse.Botany.Systems;

public sealed class SoilTillingSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    private static readonly EntProtoId SoilBed = "GardenIrrigatedSoil";

    public override void Initialize()
    {
        SubscribeLocalEvent<SoilTillingToolComponent, AfterInteractEvent>(OnTill);
        SubscribeLocalEvent<SoilTillingToolComponent, TillSoilDoAfterEvent>(OnTillComplete);
    }

    private void OnTill(Entity<SoilTillingToolComponent> hoe, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target != null ||
            !_turf.TryGetTileRef(args.ClickLocation, out var tile) ||
            !TryComp<MapGridComponent>(tile.Value.GridUid, out var grid))
        {
            return;
        }

        if (GetTillingError(grid, tile.Value) is { } error)
        {
            _popup.PopupCursor(Loc.GetString(error), args.User);
            args.Handled = true;
            return;
        }

        var till = new TillSoilDoAfterEvent(GetNetEntity(tile.Value.GridUid), tile.Value.GridIndices);
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, 2f, till, hoe, used: hoe)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnTillComplete(Entity<SoilTillingToolComponent> _, ref TillSoilDoAfterEvent args)
    {
        var gridUid = GetEntity(args.Grid);
        if (args.Handled || args.Cancelled ||
            !TryComp<MapGridComponent>(gridUid, out var grid) ||
            !_map.TryGetTileRef(gridUid, grid, args.Indices, out var tile))
        {
            return;
        }

        var center = _turf.GetTileCenter(tile);
        if (!_interaction.InRangeUnobstructed(args.User, center))
            return;

        if (GetTillingError(grid, tile) is { } error)
        {
            _popup.PopupCursor(Loc.GetString(error), args.User);
            return;
        }

        Spawn(SoilBed, center);
        args.Handled = true;
    }

    private LocId? GetTillingError(MapGridComponent grid, TileRef tile)
    {
        var suitable = _turf.GetContentTileDefinition(tile).ID is
            "FloorDirt" or "FloorPlanetDirt" or "FloorPlanetGrass" or
            "FloorGrass" or "FloorGrassJungle" or "FloorGrassDark" or "FloorGrassLight" or
            "FloorAstroGrass" or "FloorMowedAstroGrass" or "FloorJungleAstroGrass" or
            "FloorDarkAstroGrass" or "FloorLightAstroGrass";
        if (!suitable)
            return "garden-tilling-unsuitable-floor";

        if (_turf.IsTileBlocked(tile, CollisionGroup.MobMask) ||
            _map.GetAnchoredEntities(tile.GridUid, grid, tile.GridIndices).Any(entity => HasComp<PlantHolderComponent>(entity)))
        {
            return "garden-tilling-occupied";
        }

        return null;
    }
}
