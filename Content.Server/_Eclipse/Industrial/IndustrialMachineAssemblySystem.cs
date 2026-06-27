using Content.Server.Popups;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Interaction;
using Content.Shared.Placeable;

namespace Content.Server._Eclipse.Industrial;

public sealed partial class IndustrialMachineAssemblySystem : SharedIndustrialMachineAssemblySystem
{
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IndustrialProcessorSystem _processors = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlaceableSurfaceComponent, InteractUsingEvent>(OnTableInteractUsing);
    }

    private void OnTableInteractUsing(Entity<PlaceableSurfaceComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!_processors.IsPortConfigurator(args.Used))
            return;

        if (!IsWorkbenchTable(ent))
            return;

        if (!TryFindAdjacentWorkbenchTable(ent, out var partner, out var direction))
        {
            _popup.PopupCursor(Loc.GetString("industrial-workbench-need-two-tables"), args.User);
            return;
        }

        args.Handled = true;
        TryAssembleWorkbench(args.User, ent, partner, direction);
    }

    protected override void ApplyUpgradeModule(
        Entity<IndustrialMachineChassisComponent> chassis,
        EntityUid user,
        EntityUid module,
        IndustrialUpgradeModuleComponent moduleComp)
    {
        var coords = Transform(chassis).Coordinates;
        var mapCoords = _transform.ToMapCoordinates(coords);

        Del(chassis);
        Del(module);

        var machine = Spawn(moduleComp.ResultMachine, mapCoords);        _transform.AnchorEntity(machine);

        _popup.PopupPredicted(
            Loc.GetString("industrial-chassis-upgraded", ("machine", MetaData(machine).EntityName)),
            user,
            machine);
    }
}
