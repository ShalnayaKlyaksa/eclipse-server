using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Generator;
using Content.Shared.Atmos;

namespace Content.Server._Eclipse.Power;

[RegisterComponent]
public sealed partial class GasFuelGeneratorAdapterComponent : Component
{
    [DataField] public string Inlet = "fuel";
    [DataField] public string Outlet = "exhaust";
    [DataField] public Gas FuelGas = Gas.Plasma;
    [DataField] public Gas Oxidizer = Gas.Oxygen;
    [DataField] public Gas Exhaust = Gas.CarbonDioxide;
    [DataField] public float MolesPerFuel = .1f;
    [DataField] public float OxidizerRatio = 2;
    [DataField] public float MaximumExhaustPressure = 1000;
}

public sealed class GasFuelGeneratorSystem : EntitySystem
{
    [Dependency] private readonly NodeContainerSystem _nodes = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<GasFuelGeneratorAdapterComponent, GeneratorGetFuelEvent>(GetFuel);
        SubscribeLocalEvent<GasFuelGeneratorAdapterComponent, GeneratorUseFuel>(UseFuel);
    }
    private bool GetPorts(Entity<GasFuelGeneratorAdapterComponent> ent, out PipeNode? inlet, out PipeNode? outlet)
    {
        inlet = outlet = null;
        return _nodes.TryGetNode(ent.Owner, ent.Comp.Inlet, out inlet) && _nodes.TryGetNode(ent.Owner, ent.Comp.Outlet, out outlet) &&
               inlet != null && outlet != null && !ReferenceEquals(inlet.Air, outlet.Air) &&
               outlet.Air.Pressure < ent.Comp.MaximumExhaustPressure;
    }
    private void GetFuel(Entity<GasFuelGeneratorAdapterComponent> ent, ref GeneratorGetFuelEvent args)
    {
        if (!GetPorts(ent, out var inlet, out _) || ent.Comp.MolesPerFuel <= 0 || ent.Comp.OxidizerRatio <= 0) return;
        args.Fuel = MathF.Min(inlet!.Air[(int) ent.Comp.FuelGas], inlet.Air[(int) ent.Comp.Oxidizer] / ent.Comp.OxidizerRatio) / ent.Comp.MolesPerFuel;
    }
    private void UseFuel(Entity<GasFuelGeneratorAdapterComponent> ent, ref GeneratorUseFuel args)
    {
        if (!GetPorts(ent, out var inlet, out var outlet)) return;
        var moles = args.FuelUsed * ent.Comp.MolesPerFuel;
        inlet!.Air.AdjustMoles(ent.Comp.FuelGas, -moles);
        inlet.Air.AdjustMoles(ent.Comp.Oxidizer, -moles * ent.Comp.OxidizerRatio);
        var exhaust = new GasMixture { Temperature = inlet.Air.Temperature };
        exhaust.SetMoles(ent.Comp.Exhaust, moles);
        _atmosphere.Merge(outlet!.Air, exhaust);
    }
}
