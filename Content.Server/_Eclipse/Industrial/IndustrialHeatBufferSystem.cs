using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Examine;
using Content.Shared.Temperature.Components;
using Robust.Server.GameObjects;

namespace Content.Server._Eclipse.Industrial;

public sealed partial class IndustrialHeatBufferSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private NodeContainerSystem _nodeContainer = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private AppearanceSystem _appearance = default!;

    private const float TransferMolesPerTick = 50f;
    [Dependency] private Content.Server.Temperature.Systems.TemperatureSystem _temperature = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IndustrialHeatBufferComponent, ComponentInit>(OnBufferInit);
        SubscribeLocalEvent<IndustrialHeatBufferComponent, MapInitEvent>(OnBufferMapInit);
        SubscribeLocalEvent<IndustrialHeatBufferComponent, AnchorStateChangedEvent>(OnBufferAnchorChanged);
        SubscribeLocalEvent<IndustrialHeatBufferComponent, AtmosDeviceUpdateEvent>(OnBufferAtmosUpdate);
        SubscribeLocalEvent<IndustrialHeatBufferComponent, ExaminedEvent>(OnBufferExamined);
    }

    private void OnBufferInit(Entity<IndustrialHeatBufferComponent> ent, ref ComponentInit args)
    {
        EntityManager.System<SharedIndustrialHeatConnectSystem>().TryAutoBindAdjacentProcessors(ent);
    }

    private void OnBufferMapInit(Entity<IndustrialHeatBufferComponent> ent, ref MapInitEvent args)
    {
        EntityManager.System<SharedIndustrialHeatConnectSystem>().TryAutoBindAdjacentProcessors(ent);
    }

    private void OnBufferAnchorChanged(Entity<IndustrialHeatBufferComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
            EntityManager.System<SharedIndustrialHeatConnectSystem>().TryAutoBindAdjacentProcessors(ent);
    }

    private void OnBufferExamined(Entity<IndustrialHeatBufferComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString(ent.Comp.PlasmaFlowing
            ? "industrial-heat-buffer-examine-active"
            : "industrial-heat-buffer-examine-idle"));
    }

    private void OnBufferAtmosUpdate(Entity<IndustrialHeatBufferComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        var comp = ent.Comp;
        var wasFlowing = comp.PlasmaFlowing;
        comp.PlasmaFlowing = false;

        if (!_nodeContainer.TryGetNodes(ent.Owner, comp.InletNodeName, comp.OutletNodeName, out PipeNode? inlet, out PipeNode? outlet)
            || inlet == null || outlet == null)
        {
            UpdateBufferAppearance(ent, comp);
            return;
        }

        if (TryComp<TemperatureComponent>(ent, out var body) &&
            inlet.Air.GetMoles(Gas.Plasma) >= comp.MinPlasmaMoles && inlet.Air.TotalMoles > 0)
        {
            var ratio = MathF.Min(1f, TransferMolesPerTick * args.dt / inlet.Air.TotalMoles);
            var packet = inlet.Air.RemoveRatio(ratio);
            comp.PlasmaFlowing = packet.TotalMoles > 0;
            var gasCapacity = _atmosphere.GetHeatCapacity(packet, false);
            var bodyCapacity = _temperature.GetHeatCapacity(ent, body);
            if (gasCapacity > Atmospherics.MinimumHeatCapacity && bodyCapacity > Atmospherics.MinimumHeatCapacity)
            {
                var equilibriumEnergy = (packet.Temperature - body.CurrentTemperature) /
                                        (1f / gasCapacity + 1f / bodyCapacity);
                var energy = Math.Clamp(equilibriumEnergy, -comp.HeatTransferRate * args.dt,
                    comp.HeatTransferRate * args.dt);
                packet.Temperature -= energy / gasCapacity;
                _temperature.ChangeHeat(ent, energy, true, body);
            }
            _atmosphere.Merge(outlet.Air, packet);
        }

        // Stored heat remains usable without flow, but each joule delivered is removed from the buffer.
        TransferHeatToLinkedProcessors(ent, comp, args.dt);

        if (wasFlowing != comp.PlasmaFlowing)
            UpdateBufferAppearance(ent, comp);

        Dirty(ent, comp);
    }

    private void TransferHeatToLinkedProcessors(EntityUid buffer, IndustrialHeatBufferComponent comp, float dt)
    {
        if (!TryComp<TemperatureComponent>(buffer, out var body))
            return;
        var budget = comp.HeatTransferRate * dt;
        var bodyCapacity = _temperature.GetHeatCapacity(buffer, body);
        foreach (var (processor, face) in IndustrialHeatLinkHelper.GetLinkedProcessors(buffer, EntityManager, _map))
        {
            if (budget <= 0 || !IndustrialHeatLinkHelper.IsHeatLinked(processor, buffer, face, EntityManager, _map) ||
                !TryComp<TemperatureComponent>(processor, out var target) ||
                !HasComp<IndustrialHeatPoweredComponent>(processor))
                continue;
            var targetCapacity = _temperature.GetHeatCapacity(processor, target);
            if (bodyCapacity <= Atmospherics.MinimumHeatCapacity || targetCapacity <= Atmospherics.MinimumHeatCapacity)
                continue;
            var difference = MathF.Min(body.CurrentTemperature, comp.OperatingTemperature) - target.CurrentTemperature;
            if (difference <= 0)
                continue;
            var energy = MathF.Min(budget, difference / (1f / bodyCapacity + 1f / targetCapacity));
            _temperature.ChangeHeat(buffer, -energy, true, body);
            _temperature.ChangeHeat(processor, energy, true, target);
            budget -= energy;
        }
    }

    private void UpdateBufferAppearance(EntityUid uid, IndustrialHeatBufferComponent comp)
    {
        _appearance.SetData(uid, IndustrialHeatBufferVisuals.PlasmaFlowing, comp.PlasmaFlowing);
    }
}
