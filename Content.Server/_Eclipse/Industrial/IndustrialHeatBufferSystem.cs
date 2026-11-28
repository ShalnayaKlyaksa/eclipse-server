using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Components;
using Content.Shared.Examine;
using Content.Shared.Temperature.Components;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using System.Diagnostics.CodeAnalysis;

namespace Content.Server._Eclipse.Industrial;

public sealed partial class IndustrialHeatBufferSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private NodeContainerSystem _nodeContainer = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private AppearanceSystem _appearance = default!;

    private const float TransferMolesPerSecond = 50f;
    [Dependency] private Content.Server.Temperature.Systems.TemperatureSystem _temperature = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IndustrialHeatBufferComponent, ComponentInit>(OnBufferInit);
        SubscribeLocalEvent<IndustrialHeatBufferComponent, MapInitEvent>(OnBufferMapInit);
        SubscribeLocalEvent<IndustrialHeatBufferComponent, AnchorStateChangedEvent>(OnBufferAnchorChanged);
        SubscribeLocalEvent<IndustrialHeatBufferComponent, AtmosDeviceUpdateEvent>(OnBufferAtmosUpdate);
        SubscribeLocalEvent<IndustrialHeatBufferComponent, AtmosDeviceDisabledEvent>(OnBufferAtmosDisabled);
        SubscribeLocalEvent<IndustrialHeatBufferComponent, ExaminedEvent>(OnBufferExamined);
        SubscribeLocalEvent<IndustrialHeatBufferComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
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

        if (TryGetGasPorts(ent, out var inlet, out var outlet))
        {
            args.PushMarkup(Loc.GetString("industrial-heat-buffer-examine-ports",
                ("inlet", SharedIndustrialProcessorSystem.GetDirectionName(inlet.CurrentPipeDirection.ToDirection())),
                ("outlet", SharedIndustrialProcessorSystem.GetDirectionName(outlet.CurrentPipeDirection.ToDirection()))));
        }

        if (TryComp<TemperatureComponent>(ent, out var body))
            args.PushMarkup(Loc.GetString("industrial-heat-buffer-examine-temperature", ("temperature", (int) body.CurrentTemperature)));

        args.PushMarkup(Loc.GetString(ent.Comp.SharedGasCircuit
            ? "industrial-heat-buffer-examine-shared"
            : ent.Comp.PlasmaFlowing ? "industrial-heat-buffer-examine-active" : "industrial-heat-buffer-examine-idle"));
    }

    private void OnGetVerbs(Entity<IndustrialHeatBufferComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract)
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("industrial-heat-buffer-reverse"),
            Category = VerbCategory.Rotate,
            DoContactInteraction = true,
            Act = () =>
            {
                ent.Comp.Reversed = !ent.Comp.Reversed;
                SetGasState(ent, false, ent.Comp.SharedGasCircuit);
            },
        });
    }

    private bool TryGetGasPorts(Entity<IndustrialHeatBufferComponent> ent,
        [NotNullWhen(true)] out PipeNode? inlet, [NotNullWhen(true)] out PipeNode? outlet)
    {
        var comp = ent.Comp;
        return _nodeContainer.TryGetNodes(ent.Owner,
            comp.Reversed ? comp.OutletNodeName : comp.InletNodeName,
            comp.Reversed ? comp.InletNodeName : comp.OutletNodeName,
            out inlet, out outlet);
    }

    private void OnBufferAtmosUpdate(Entity<IndustrialHeatBufferComponent> ent, ref AtmosDeviceUpdateEvent args)
    {
        if (!Transform(ent).Anchored || args.dt <= 0f || !TryComp<TemperatureComponent>(ent, out var body))
            return;

        var (flowing, sharedCircuit) = ExchangePlasmaHeat(ent, body, args.dt);
        SetGasState(ent, flowing, sharedCircuit);
        TransferHeatToLinkedProcessors(ent, body, args.dt);
    }

    private void OnBufferAtmosDisabled(Entity<IndustrialHeatBufferComponent> ent, ref AtmosDeviceDisabledEvent args)
        => SetGasState(ent, false, false);

    private (bool Flowing, bool SharedCircuit) ExchangePlasmaHeat(
        Entity<IndustrialHeatBufferComponent> ent, TemperatureComponent body, float dt)
    {
        var comp = ent.Comp;
        if (!TryGetGasPorts(ent, out var inlet, out var outlet) ||
            inlet.ReachableNodes.Count == 0 || outlet.ReachableNodes.Count == 0)
            return (false, false);

        var input = inlet.Air;
        var output = outlet.Air;
        var sharedCircuit = ReferenceEquals(input, output);
        if (input.GetMoles(Gas.Plasma) < comp.MinPlasmaMoles)
            return (false, false);

        var amount = TransferMolesPerSecond * dt;
        if (!sharedCircuit)
        {
            // The exchanger is passive. Limit flow to the pressure drop supplied by the circulation pump.
            var pressureDrop = input.Pressure - output.Pressure;
            if (pressureDrop <= 0.01f)
                return (false, false);

            var outletTemperature = MathF.Max(input.Temperature, body.CurrentTemperature);
            var pressurePerMole = Atmospherics.R * (input.Temperature / input.Volume + outletTemperature / output.Volume);
            amount = MathF.Min(amount, pressureDrop / pressurePerMole);
        }

        var packet = input.Remove(amount);
        var gasCapacity = _atmosphere.GetHeatCapacity(packet, false);
        var bodyCapacity = _temperature.GetHeatCapacity(ent, body);
        if (gasCapacity > Atmospherics.MinimumHeatCapacity && bodyCapacity > Atmospherics.MinimumHeatCapacity)
        {
            var equilibriumEnergy = (packet.Temperature - body.CurrentTemperature) / (1f / gasCapacity + 1f / bodyCapacity);
            var limit = comp.HeatTransferRate * dt;
            var energy = Math.Clamp(equilibriumEnergy, -limit, limit);
            packet.Temperature -= energy / gasCapacity;
            _temperature.ChangeHeat(ent, energy, true, body);
        }

        _atmosphere.Merge(output, packet);
        return (!sharedCircuit && packet.TotalMoles > 0, sharedCircuit);
    }

    private void TransferHeatToLinkedProcessors(Entity<IndustrialHeatBufferComponent> buffer, TemperatureComponent body, float dt)
    {
        var comp = buffer.Comp;
        var bodyCapacity = _temperature.GetHeatCapacity(buffer, body);
        if (bodyCapacity <= Atmospherics.MinimumHeatCapacity)
            return;

        var consumers = new Dictionary<EntityUid, TemperatureComponent>();
        void AddConsumers(EntityUid source)
        {
            foreach (var (processor, face) in IndustrialHeatLinkHelper.GetAdjacentProcessors(source, EntityManager, _map))
            {
                if (processor.Comp.GetFacePort(face) == FacePortState.HeatInput &&
                    TryComp<TemperatureComponent>(processor, out var target) &&
                    target.CurrentTemperature < MathF.Min(body.CurrentTemperature, comp.OperatingTemperature))
                {
                    consumers.TryAdd(processor, target);
                }
            }
        }

        AddConsumers(buffer);
        if (_nodeContainer.TryGetNode<AdjacentNode>(buffer.Owner, IndustrialHeatBufferComponent.HeatNodeName, out var node) &&
            node.NodeGroup is { Remaking: false } network)
        {
            foreach (var member in network.Nodes)
            {
                if (HasComp<IndustrialHeatPipeComponent>(member.Owner) && member.Connectable(EntityManager))
                    AddConsumers(member.Owner);
            }
        }

        var budget = comp.HeatTransferRate * dt;
        var remaining = consumers.Count;
        foreach (var (processor, target) in consumers)
        {
            var share = budget / remaining--;
            var targetCapacity = _temperature.GetHeatCapacity(processor, target);
            if (targetCapacity <= Atmospherics.MinimumHeatCapacity)
                continue;
            var difference = body.CurrentTemperature - target.CurrentTemperature;
            if (difference <= 0)
                continue;
            var equilibriumEnergy = difference / (1f / bodyCapacity + 1f / targetCapacity);
            var ceiling = (comp.OperatingTemperature - target.CurrentTemperature) * targetCapacity;
            var energy = MathF.Min(share, MathF.Min(equilibriumEnergy, ceiling));
            _temperature.ChangeHeat(buffer, -energy, true, body);
            _temperature.ChangeHeat(processor, energy, true, target);
            budget -= energy;
        }
    }

    private void SetGasState(Entity<IndustrialHeatBufferComponent> ent, bool flowing, bool sharedCircuit)
    {
        var comp = ent.Comp;
        var active = flowing || sharedCircuit;
        if (active != (comp.PlasmaFlowing || comp.SharedGasCircuit))
            _appearance.SetData(ent, IndustrialHeatBufferVisuals.PlasmaFlowing, active);

        comp.PlasmaFlowing = flowing;
        comp.SharedGasCircuit = sharedCircuit;
    }
}
