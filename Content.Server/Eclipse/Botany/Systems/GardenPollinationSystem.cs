using System.Linq;
using Content.Server.Botany.Components;
using Content.Shared._Eclipse.Beekeeping;
using Content.Shared.Examine;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server.Eclipse.Botany.Systems;

[RegisterComponent]
public sealed partial class GardenForageComponent : Component
{
    [DataField] public string? Honeycomb;
}

/// <summary>Optional mutual benefit: flowering beds feed a working hive, bees add one fruit per harvest.</summary>
public sealed class GardenPollinationSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ApiaryComponent, ApiaryHarvestEvent>(OnHarvest);
        SubscribeLocalEvent<ApiaryComponent, ExaminedEvent>(OnExamine);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(20);
        Pollinate();
    }

    public void Pollinate()
    {
        // One bed can feed only one hive per pass, even if hives overlap.
        var visited = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<ApiaryComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var hive, out var xform))
        {
            if (!hive.Enabled || hive.Mother == null || Paused(uid) || !xform.Anchored || xform.GridUid == null
                || hive.Status is not ("apiary-status-working" or "apiary-status-feed"))
                continue;
            foreach (var plant in _lookup.GetEntitiesInRange<PlantHolderComponent>(xform.Coordinates, 4)
                         .OrderBy(ent => ent.Owner.Id))
            {
                var bed = plant.Comp;
                var seed = bed.Seed;
                var bedXform = Transform(plant.Owner);
                if (seed == null || bed.Dead || bed.Health <= 0 || bed.Age < seed.Maturation
                    || bed.WaterLevel <= 10 || bed.NutritionLevel <= 5 || Paused(plant.Owner)
                    || !bedXform.Anchored || bedXform.GridUid != xform.GridUid || !visited.Add(plant.Owner))
                    continue;
                hive.Nectar = Math.Min(200, hive.Nectar + 0.4f);
                // Explosive and hostile crops do not become a source of extra weapons through bees.
                if (seed.PacketPrototype != "BombomatoSeeds" && seed.ProductPrototypes.All(id => id != "MobTomatoKiller"))
                    bed.BeePollinated = true;
                var comb = seed.PacketPrototype switch
                {
                    "EnergyFlowerSeeds" => "GardenChargedHoneycomb",
                    "DemonicWatermelonSeeds" => "GardenSpicedHoneycomb",
                    "NectarBerrySeeds" => "GardenNectarHoneycomb",
                    _ => null,
                };
                if (comb != null)
                    EnsureComp<GardenForageComponent>(uid).Honeycomb = comb;
            }
        }
    }

    private void OnHarvest(Entity<ApiaryComponent> ent, ref ApiaryHarvestEvent args)
    {
        if (!TryComp<GardenForageComponent>(ent, out var forage) || forage.Honeycomb == null)
            return;
        var product = SpawnNextToOrDrop(forage.Honeycomb, ent);
        _containers.Insert(product, ent.Comp.Output);
        forage.Honeycomb = null;
    }

    private void OnExamine(Entity<ApiaryComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushText(Loc.GetString("garden-apiary-forage-hint"));
    }
}
