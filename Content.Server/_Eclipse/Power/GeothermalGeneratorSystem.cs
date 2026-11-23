using Content.Server.Power.Generator;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Eclipse.Power;

/// <summary>A passable floor reservoir; all plants above it draw from one finite heat reserve.</summary>
[RegisterComponent]
public sealed partial class GeothermalDepositComponent : Component
{
    [DataField] public float RemainingEnergy = 300000000;
}

[RegisterComponent]
public sealed partial class GeothermalGeneratorAdapterComponent : Component
{
    [DataField] public float EnergyPerFuel = 3000000;
}

public sealed class GeothermalGeneratorSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<GeothermalGeneratorAdapterComponent, GeneratorGetFuelEvent>(GetFuel);
        SubscribeLocalEvent<GeothermalGeneratorAdapterComponent, GeneratorUseFuel>(UseFuel);
    }
    public bool FindDeposit(EntityUid uid, out GeothermalDepositComponent? deposit)
    {
        deposit = null;
        var transform = Transform(uid);
        if (!transform.Anchored || transform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid)) return false;
        var tile = _map.TileIndicesFor(gridUid, grid, transform.Coordinates);
        var entities = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (entities.MoveNext(out var entity))
            if (TryComp(entity, out deposit) && deposit.RemainingEnergy > 0) return true;
        return false;
    }
    private void GetFuel(Entity<GeothermalGeneratorAdapterComponent> ent, ref GeneratorGetFuelEvent args)
    {
        if (ent.Comp.EnergyPerFuel > 0 && FindDeposit(ent, out var deposit)) args.Fuel = deposit!.RemainingEnergy / ent.Comp.EnergyPerFuel;
    }
    private void UseFuel(Entity<GeothermalGeneratorAdapterComponent> ent, ref GeneratorUseFuel args)
    {
        if (FindDeposit(ent, out var deposit)) deposit!.RemainingEnergy = MathF.Max(0, deposit.RemainingEnergy - args.FuelUsed * ent.Comp.EnergyPerFuel);
    }
}
