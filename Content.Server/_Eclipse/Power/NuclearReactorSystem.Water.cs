using Content.Server.Power.Components;
using Content.Shared._Eclipse.Power;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.Power;

public sealed partial class NuclearReactorSystem
{
    // Thermal scale for the reagent units used by Eclipse liquid pipes.
    private const float WaterHeatCapacity = 6000;
    private const float ReactorWaterRate = 12;
    private static readonly ProtoId<ReagentPrototype> Water = "Water";

    private float CoolWithWater(NuclearReactorComponent core, float dt)
    {
        core.Flow = 0;
        core.Pressure = 0;
        core.NoWaterFlowTime = MathF.Min(core.NoWaterFlowTime + dt, 2);
        if (core.Inlet is not { } inlet || core.Outlet is not { } outlet ||
            !TryTakeWater(inlet, outlet, ReactorWaterRate * dt, out var source, out var destination, out var water))
            return 0;

        var inletTemperature = water.Temperature;
        var capacity = water.Volume.Float() * WaterHeatCapacity;
        var previousCoreTemperature = core.Temperature;
        var heat = ExchangeHeat(core, water.Temperature, capacity, dt);
        if (!ReturnWater(source, destination, water, water.Temperature + heat / capacity))
        {
            core.Temperature = previousCoreTemperature;
            return 0;
        }

        core.InletTemperature = inletTemperature;
        core.OutletTemperature = water.Temperature;
        core.Flow = water.Volume.Float() / dt;
        core.NoWaterFlowTime = 0;
        return heat;
    }

    private void UpdateWaterTurbines(float dt)
    {
        var turbines = EntityQueryEnumerator<NuclearWaterTurbineComponent, PowerSupplierComponent>();
        while (turbines.MoveNext(out var uid, out var turbine, out var supplier))
        {
            supplier.MaxSupply = ExchangeTurbineHeat(uid, turbine, supplier, dt) / dt;
            supplier.Enabled = supplier.MaxSupply > 0;
            _appearance.SetData(uid, NuclearReactorVisuals.Active, supplier.Enabled);
        }
    }

    private float ExchangeTurbineHeat(EntityUid uid, NuclearWaterTurbineComponent turbine, PowerSupplierComponent supplier, float dt)
    {
        if (!TryTakeWater(uid, uid, turbine.FlowRate * dt, out var source, out var destination, out var water))
            return 0;

        var air = _atmosphere.GetContainingMixture(uid, false, true);
        var ambientTemperature = air?.Temperature ?? Atmospherics.TCMB;
        var capacity = water.Volume.Float() * WaterHeatCapacity;
        var heat = Math.Clamp((water.Temperature - MathF.Max(Atmospherics.T20C, ambientTemperature) - 12) * capacity,
            0, turbine.MaximumHeatTransfer * dt);
        var electrical = supplier.Net is { IsConnectedNetwork: true }
            ? MathF.Min(heat * turbine.Efficiency, turbine.NominalPower * dt)
            : 0;
        if (!ReturnWater(source, destination, water, water.Temperature - heat / capacity))
            return 0;

        if (air is { Immutable: false, TotalMoles: > 0 })
            _atmosphere.AddHeat(air, heat - electrical);

        return electrical;
    }

    private bool TryTakeWater(EntityUid inlet, EntityUid outlet, float limit,
        out Entity<SolutionComponent> source, out Entity<SolutionComponent> destination, out Solution water)
    {
        source = default;
        destination = default;
        water = default!;
        if (TerminatingOrDeleted(inlet) || TerminatingOrDeleted(outlet) ||
            !Transform(inlet).Anchored || !Transform(outlet).Anchored ||
            !_solutions.TryGetSolution(inlet, "tank", out var src, out var input) ||
            !_solutions.TryGetSolution(outlet, "output", out var dst, out var output) ||
            input.GetTotalPrototypeQuantity(Water) != input.Volume)
            return false;

        var amount = FixedPoint2.Min(FixedPoint2.New(limit), FixedPoint2.Min(input.Volume, output.AvailableVolume));
        if (amount <= 0)
            return false;

        source = src.Value;
        destination = dst.Value;
        water = _solutions.SplitSolution(source, amount);
        return true;
    }

    private bool ReturnWater(Entity<SolutionComponent> source, Entity<SolutionComponent> destination,
        Solution water, float temperature)
    {
        var originalTemperature = water.Temperature;
        water.Temperature = temperature;
        if (_solutions.TryAddSolution(destination, water))
            return true;

        water.Temperature = originalTemperature;
        _solutions.TryAddSolution(source, water);
        return false;
    }
}
