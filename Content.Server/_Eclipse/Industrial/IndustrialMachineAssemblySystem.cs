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
    protected override void ApplyUpgradeModule(
        Entity<IndustrialMachineChassisComponent> chassis,
        EntityUid user,
        EntityUid module,
        IndustrialUpgradeModuleComponent moduleComp)
    {
        if (!Transform(chassis).Anchored)
        {
            _popup.PopupCursor(Loc.GetString("industrial-chassis-anchor-first"), user);
            return;
        }
        var machine = Spawn(moduleComp.ResultMachine, Transform(chassis).Coordinates);
        if (!Transform(machine).Anchored && !_transform.AnchorEntity(machine))
        {
            Del(machine);
            return;
        }
        Del(chassis);
        Del(module);
        _popup.PopupPredicted(Loc.GetString("industrial-chassis-upgraded", ("machine", MetaData(machine).EntityName)), user, machine);
    }

    public void TryUpgradeMachine(Entity<IndustrialProcessorComponent> ent, EntityUid user,
        EntityUid moduleUid, IndustrialUpgradeModuleComponent module)
    {
        if (module.ProcessorType != ent.Comp.ProcessorType || module.Tier <= ent.Comp.Tier)
        {
            _popup.PopupCursor(Loc.GetString("industrial-upgrade-incompatible"), user);
            return;
        }
        _processors.UpgradeMachine(ent, module.Tier, module.ResultMachine);
        Del(moduleUid);
    }
}
