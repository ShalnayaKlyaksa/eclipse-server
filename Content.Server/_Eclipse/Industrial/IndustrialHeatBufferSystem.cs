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

public sealed class IndustrialHeatBufferSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;

    private const float TransferMolesPerTick = 50f;
    private const float AmbientTemperature = 293.15f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IndustrialHeatBufferComponent, ComponentInit>(OnBufferInit);
        SubscribeLocalEvent<IndustrialHeatBufferComponent, MapInitEvent>(OnBufferMapInit);
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

        var inletPlasma = inlet.Air.GetMoles(Gas.Plasma);
        var outletPlasma = outlet.Air.GetMoles(Gas.Plasma);
        comp.PlasmaFlowing = inletPlasma + outletPlasma >= comp.MinPlasmaMoles;

        if (comp.PlasmaFlowing && inlet.Air.TotalMoles > 0)
        {
            var transferMoles = TransferMolesPerTick * args.dt;
            var ratio = MathF.Min(1f, transferMoles / inlet.Air.TotalMoles);
            var removed = inlet.Air.RemoveRatio(ratio);
            _atmosphere.Merge(outlet.Air, removed);

            if (TryComp<TemperatureComponent>(ent, out var bufferTemp))
            {
                var gasTemp = MathF.Max(inlet.Air.Temperature, outlet.Air.Temperature);
                var target = MathF.Max(comp.OperatingTemperature, gasTemp);
                bufferTemp.CurrentTemperature = MathF.Min(
                    target,
                    bufferTemp.CurrentTemperature + comp.HeatTransferRate * args.dt * 0.05f);
            }
        }
        else if (TryComp<TemperatureComponent>(ent, out var coolingTemp))
        {
            coolingTemp.CurrentTemperature = MathF.Max(
                AmbientTemperature,
                coolingTemp.CurrentTemperature - 20f * args.dt);
        }

        if (comp.PlasmaFlowing)
            TransferHeatToLinkedProcessors(ent, comp, args.dt);

        if (wasFlowing != comp.PlasmaFlowing)
            UpdateBufferAppearance(ent, comp);

        Dirty(ent, comp);
    }

    private void TransferHeatToLinkedProcessors(EntityUid buffer, IndustrialHeatBufferComponent comp, float dt)
    {
        foreach (var (processor, face) in IndustrialHeatLinkHelper.GetLinkedProcessors(buffer, EntityManager, _map))
        {
            if (!IndustrialHeatLinkHelper.IsHeatLinked(processor, buffer, face, EntityManager, _map))
                continue;

            if (!TryComp<TemperatureComponent>(processor, out var procTemp) ||
                !HasComp<IndustrialHeatPoweredComponent>(processor))
            {
                continue;
            }

            if (procTemp.CurrentTemperature >= comp.OperatingTemperature)
                continue;

            procTemp.CurrentTemperature = MathF.Min(
                comp.OperatingTemperature,
                procTemp.CurrentTemperature + comp.HeatTransferRate * dt / MathF.Max(procTemp.SpecificHeat, 1f));
        }
    }

    private void UpdateBufferAppearance(EntityUid uid, IndustrialHeatBufferComponent comp)
    {
        _appearance.SetData(uid, IndustrialHeatBufferVisuals.PlasmaFlowing, comp.PlasmaFlowing);
    }
}
