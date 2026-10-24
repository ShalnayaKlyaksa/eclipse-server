using Content.Shared.Climbing.Components;
using Content.Shared.Interaction;
using Content.Shared.Placeable;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
namespace Content.Shared._Eclipse.Industrial;

public abstract class SharedIndustrialMachineAssemblySystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private static readonly ProtoId<TagPrototype> ForceFixRotationsTag = "ForceFixRotations";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IndustrialMachineChassisComponent, InteractUsingEvent>(OnChassisInteractUsing);
    }

    private void OnChassisInteractUsing(Entity<IndustrialMachineChassisComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<IndustrialUpgradeModuleComponent>(args.Used, out var module))
            return;

        args.Handled = true;
        ApplyUpgradeModule(ent, args.User, args.Used, module);
    }

    protected abstract void ApplyUpgradeModule(
        Entity<IndustrialMachineChassisComponent> chassis,
        EntityUid user,
        EntityUid module,
        IndustrialUpgradeModuleComponent moduleComp);

    protected bool IsWorkbenchTable(EntityUid uid)
    {
        if (!HasComp<PlaceableSurfaceComponent>(uid) || !HasComp<ClimbableComponent>(uid))
            return false;

        if (!_tags.HasTag(uid, ForceFixRotationsTag))
            return false;

        var meta = MetaData(uid);
        if (meta.EntityPrototype?.ID.Contains("Frame") == true)
            return false;

        return true;
    }

    protected bool TryFindAdjacentWorkbenchTable(
        EntityUid table,
        out EntityUid partner,
        out Direction directionFromTableToPartner)
    {
        partner = default;
        directionFromTableToPartner = default;

        if (!TryComp(table, out TransformComponent? tableXform) ||
            !tableXform.Anchored ||
            tableXform.GridUid is not EntityUid gridUid ||
            !TryComp(gridUid, out MapGridComponent? grid))
        {
            return false;
        }

        var origin = _map.TileIndicesFor(gridUid, grid, tableXform.Coordinates);

        foreach (var direction in SharedIndustrialProcessorSystem.CardinalDirections)
        {
            var enumerator = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, origin.Offset(direction));
            while (enumerator.MoveNext(out var entity) && entity != null)
            {
                if (entity.Value == table || !IsWorkbenchTable(entity.Value))
                    continue;

                partner = entity.Value;
                directionFromTableToPartner = direction;
                return true;
            }
        }

        return false;
    }

    protected bool TryAssembleWorkbench(EntityUid user, EntityUid tableA, EntityUid tableB, Direction directionFromAToB)
    {
        if (!TryComp(tableA, out TransformComponent? xformA))
            return false;

        var spawnCoords = xformA.Coordinates;
        var spawn = Spawn("IndustrialWorkbench", spawnCoords);

        if (TryComp(spawn, out TransformComponent? workbenchXform))
        {
            if (directionFromAToB is Direction.North or Direction.South)
                _transform.SetLocalRotation(spawn, Angle.FromDegrees(90));

            _transform.AnchorEntity(spawn, workbenchXform);
        }

        Del(tableA);
        Del(tableB);

        _popup.PopupPredicted(
            Loc.GetString("industrial-workbench-assembled"),
            user,
            spawn);

        return true;
    }
}
