using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.Generation.Teg;
using Content.Shared._Eclipse.Power;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;

namespace Content.Server._Eclipse.Power;

public sealed partial class NuclearReactorSystem
{
    // Game-scale phase model: one reagent unit of water becomes this many atmos moles.
    private const float SteamMolesPerUnit = 50;
    private const float PhaseTemperature = 400;
    private const float PhaseEnergyPerUnit = 250000;

    private float CoolWithSteam(NuclearReactorComponent core, float dt)
    {
        core.Flow = core.Pressure = core.SteamWaterLevel = 0;
        core.NoWaterFlowTime = MathF.Min(2, core.NoWaterFlowTime + dt);
        if (core.Inlet is not { } inlet || core.Outlet is not { } outlet ||
            TerminatingOrDeleted(outlet) || !Transform(outlet).Anchored ||
            !_solutions.TryGetSolution(outlet, "output", out var tank, out var water))
            return 0;

        if (TryTakeWater(inlet, outlet, ReactorWaterRate * dt, out var source, out var destination, out var feed) &&
            ReturnWater(source, destination, feed, feed.Temperature))
        {
            core.InletTemperature = feed.Temperature;
            core.Flow = feed.Volume.Float() / dt;
        }

        core.SteamWaterLevel = water.GetTotalPrototypeQuantity(Water).Float();
        if (core.SteamWaterLevel <= 0)
            return 0;

        var capacity = core.SteamWaterLevel * WaterHeatCapacity;
        var heat = ExchangeHeat(core, water.Temperature, capacity, dt);
        _solutions.SetTemperature(tank.Value, water.Temperature + heat / capacity);
        if (_nodes.TryGetNode(outlet, "steam", out PipeNode? node) && node.NodeGroup is { Nodes.Count: > 1 })
        {
            var steam = node.Air;
            core.Pressure = steam.Pressure;
            if (!steam.Immutable && IsSteam(steam))
            {
                core.SteamFlow = BoilWater(tank.Value, water, steam, core.MaximumPressure) * SteamMolesPerUnit / dt;
                core.Pressure = steam.Pressure;
                core.SteamWaterLevel = water.GetTotalPrototypeQuantity(Water).Float();
                if (core.SteamWaterLevel >= 1)
                    core.NoWaterFlowTime = 0;
            }
        }

        core.OutletTemperature = water.Temperature;
        return heat;
    }

    private float BoilWater(Entity<SolutionComponent> tank, Solution water, GasMixture steam, float maxPressure)
    {
        var excess = (water.Temperature - PhaseTemperature) * water.Volume.Float() * WaterHeatCapacity;
        if (excess <= 0)
            return 0;

        var pressureRoom = (maxPressure * 1.02f - steam.Pressure) * steam.Volume /
                           (Atmospherics.R * MathF.Max(PhaseTemperature, steam.Temperature));
        var amount = FixedPoint2.Min(water.Volume, FixedPoint2.New(MathF.Max(0,
            MathF.Min(excess / PhaseEnergyPerUnit, pressureRoom / SteamMolesPerUnit))));
        if (amount <= 0)
            return 0;

        var removed = _solutions.SplitSolution(tank, amount);
        var units = removed.Volume.Float();
        var packet = new GasMixture { Temperature = PhaseTemperature };
        packet.SetMoles(Gas.WaterVapor, units * SteamMolesPerUnit);
        var sensibleHeat = MathF.Max(0, excess - units * PhaseEnergyPerUnit);
        if (water.Volume > 0)
            _solutions.SetTemperature(tank, PhaseTemperature + sensibleHeat / (water.Volume.Float() * WaterHeatCapacity));
        else
            packet.Temperature += sensibleHeat / _atmosphere.GetHeatCapacity(packet, true);
        _atmosphere.Merge(steam, packet);
        return units;
    }

    private void UpdateSteamEquipment(float dt)
    {
        var turbines = EntityQueryEnumerator<NuclearSteamTurbineComponent, PowerSupplierComponent>();
        while (turbines.MoveNext(out var uid, out var turbine, out var supplier))
        {
            if (TerminatingOrDeleted(uid))
                continue;
            supplier.MaxSupply = ExpandSteam(uid, turbine, supplier, dt) / dt;
            supplier.Enabled = supplier.MaxSupply > 0;
            _appearance.SetData(uid, NuclearReactorVisuals.Active, supplier.Enabled);
        }

        var condensers = EntityQueryEnumerator<NuclearSteamCondenserComponent>();
        while (condensers.MoveNext(out var uid, out var condenser))
        {
            if (!TerminatingOrDeleted(uid))
                CondenseSteam(uid, condenser, dt);
        }
    }

    private void OnSteamTurbineExamined(Entity<NuclearSteamTurbineComponent> turbine, ref ExaminedEvent args)
    {
        if (TryGetGasPorts(turbine, "steam_in", "steam_out", out var inlet, out var exhaust))
            args.PushMarkup(Loc.GetString("nuclear-turbine-pressure", ("inlet", MathF.Round(inlet.Pressure, 1)),
                ("exhaust", MathF.Round(exhaust.Pressure, 1))));

        if (!TryComp<PowerSupplierComponent>(turbine, out var supply))
            return;
        args.PushMarkup(supply.Net is { IsConnectedNetwork: true }
            ? Loc.GetString("nuclear-turbine-output", ("current", supply.CurrentSupply), ("available", supply.MaxSupply))
            : Loc.GetString("nuclear-turbine-no-hv"));
    }

    private float ExpandSteam(EntityUid uid, NuclearSteamTurbineComponent turbine, PowerSupplierComponent supplier, float dt)
    {
        if (!TryGetGasPorts(uid, "steam_in", "steam_out", out var inlet, out var exhaust) ||
            !IsSteam(inlet) || !IsSteam(exhaust))
            return 0;

        var pressure = inlet.Pressure;
        var pressureDrop = pressure - exhaust.Pressure;
        if (pressureDrop <= 0)
            return 0;

        // A restricted steam path preserves an operating pressure difference across the rotor.
        var pressurePerMole = Atmospherics.R * (inlet.Temperature / inlet.Volume + exhaust.Temperature / exhaust.Volume);
        var pressureEqualization = 1 - MathF.Exp(-turbine.SteamConductance * pressurePerMole * dt);
        var packet = inlet.Remove(pressureDrop / pressurePerMole * pressureEqualization);
        if (packet.TotalMoles <= 0)
            return 0;

        var capacity = _atmosphere.GetHeatCapacity(packet, true);
        var availableWork = capacity * MathF.Max(0, packet.Temperature - 310) *
                            Math.Clamp(pressureDrop / pressure, 0, 1);
        var electrical = supplier.Net is { IsConnectedNetwork: true }
            ? MathF.Min(availableWork * turbine.Efficiency, turbine.NominalPower * dt)
            : 0;
        packet.Temperature -= electrical / capacity;
        _atmosphere.Merge(exhaust, packet);
        return electrical;
    }

    private void CondenseSteam(EntityUid uid, NuclearSteamCondenserComponent condenser, float dt)
    {
        if (!TryGetGasPorts(uid, "coolant_in", "coolant_out", out var inlet, out var outlet) ||
            !_nodes.TryGetNode(uid, "steam_in", out PipeNode? node) || node.Air is not { Immutable: false } steam ||
            ReferenceEquals(steam, inlet) || ReferenceEquals(steam, outlet) || !IsSteam(steam) ||
            !_solutions.TryGetSolution(uid, "output", out var tank, out var water))
            return;

        var (coolant, _) = TegSystem.GetCirculatorAirTransfer(inlet, outlet);
        if (coolant.TotalMoles <= 0)
            return;

        var capacity = _atmosphere.GetHeatCapacity(coolant, true);
        var condensateTemperature = MathF.Max(coolant.Temperature + 15, Atmospherics.T20C);
        var coldLimit = MathF.Min(PhaseTemperature, steam.Temperature);
        var sink = MathF.Min(condenser.MaximumHeatTransfer * dt,
            capacity * MathF.Max(0, coldLimit - condensateTemperature));
        var heatPerUnit = PhaseEnergyPerUnit + WaterHeatCapacity * (PhaseTemperature - condensateTemperature) +
                          _atmosphere.GasMolarHeatCapacities[(int) Gas.WaterVapor] * SteamMolesPerUnit *
                          (steam.Temperature - PhaseTemperature);
        var available = MathF.Min(steam.GetMoles(Gas.WaterVapor) / SteamMolesPerUnit, water.AvailableVolume.Float());
        if (heatPerUnit > 0)
            available = MathF.Min(available, sink / heatPerUnit);
        var amount = FixedPoint2.New(available);
        if (amount > 0 && sink > 0)
        {
            var condensate = new Solution { Temperature = condensateTemperature };
            condensate.AddReagent(Water.Id, amount);
            if (_solutions.TryAddSolution(tank.Value, condensate))
            {
                steam.AdjustMoles(Gas.WaterVapor, -amount.Float() * SteamMolesPerUnit);
                coolant.Temperature += amount.Float() * heatPerUnit / capacity;
            }
        }

        _atmosphere.Merge(outlet, coolant);
    }

    private bool TryGetGasPorts(EntityUid uid, string inPort, string outPort, out GasMixture inlet, out GasMixture outlet)
    {
        inlet = outlet = default!;
        if (!Transform(uid).Anchored || !_nodes.TryGetNode(uid, inPort, out PipeNode? input) ||
            !_nodes.TryGetNode(uid, outPort, out PipeNode? output))
            return false;
        inlet = input.Air;
        outlet = output.Air;
        return !inlet.Immutable && !outlet.Immutable && !ReferenceEquals(inlet, outlet);
    }

    private static bool IsSteam(GasMixture gas) => gas.GetMoles(Gas.WaterVapor) >= gas.TotalMoles * .99f;
}
