using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Power.NodeGroups;
using Content.Shared.NodeContainer;
using Robust.Shared.Timing;

namespace Content.Server.Power.EntitySystems;

public sealed class AutomaticTransferPowerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PowerNetSystem _powerNet = default!;

    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(PowerNetSystem));

        SubscribeLocalEvent<AutomaticTransferPowerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<AutomaticTransferPowerComponent, NodeGroupsRebuilt>(OnNodeGroupsRebuilt);
    }

    private void OnStartup(EntityUid uid, AutomaticTransferPowerComponent component, ComponentStartup args)
    {
        UpdateTransfer(uid, component, force: true);
    }

    private void OnNodeGroupsRebuilt(EntityUid uid, AutomaticTransferPowerComponent component, ref NodeGroupsRebuilt args)
    {
        UpdateTransfer(uid, component, force: true);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<AutomaticTransferPowerComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            UpdateTransfer(uid, component, force: false);
        }
    }

    private void UpdateTransfer(EntityUid uid, AutomaticTransferPowerComponent component, bool force)
    {
        if (!TryComp(uid, out BatteryChargerComponent? charger))
            return;

        if (!force && _timing.CurTime < component.NextSwitchTime)
            return;

        var desiredNode = HasSupply(uid, component.MainNode, component.SupplyThreshold)
            ? component.MainNode
            : component.EmergencyNode;

        if (charger.NodeId == desiredNode)
        {
            component.ActiveNode = desiredNode;
            return;
        }

        charger.ClearNet();
        charger.NodeId = desiredNode;
        charger.TryFindAndSetNet();

        component.ActiveNode = desiredNode;
        component.NextSwitchTime = _timing.CurTime + component.SwitchCooldown;
    }

    private bool HasSupply(EntityUid uid, string nodeId, float threshold)
    {
        if (!TryComp(uid, out NodeContainerComponent? container))
            return false;

        if (!container.Nodes.TryGetValue(nodeId, out var node))
            return false;

        return node.NodeGroup is IPowerNet powerNet
               && _powerNet.HasAvailableSupply(powerNet.NetworkNode, threshold);
    }
}
