using System.Linq;
using Content.Server.Botany.Components;
using Content.Shared._Eclipse.Beekeeping;
using Content.Shared.Examine;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Eclipse.Botany.Systems;

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
            var frame = hive.FrameDurability > 0 ? hive.FrameKind : "None";
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
                hive.Nectar = Math.Min(200, hive.Nectar + 0.4f * ApiaryFrameRules.ForageMultiplier(frame));
                // Explosive and hostile crops do not become a source of extra weapons through bees.
                if (seed.PacketPrototype != "BombomatoSeeds" && seed.ProductPrototypes.All(id => id != "MobTomatoKiller"))
                    bed.BeePollinated = true;
                if (seed.ApiaryComb is { } comb)
                    hive.ForageSamples[comb] = hive.ForageSamples.GetValueOrDefault(comb) + 1;
            }
        }
    }

    public static EntProtoId? PredictComb(ApiaryComponent hive) => hive.ForageSamples
        .OrderByDescending(sample => sample.Value)
        .ThenBy(sample => sample.Key.Id, StringComparer.Ordinal)
        .Select(sample => (EntProtoId?) sample.Key)
        .FirstOrDefault();

    private void OnHarvest(Entity<ApiaryComponent> ent, ref ApiaryHarvestEvent args)
    {
        var comb = PredictComb(ent.Comp);
        ent.Comp.ForageSamples.Clear();
        if (comb == null)
            return;

        var count = ApiaryFrameRules.ForageMultiplier(args.FrameKind);
        for (var i = 0; i < count; i++)
            _containers.Insert(SpawnNextToOrDrop(comb.Value, ent), ent.Comp.Output);
    }

    private void OnExamine(Entity<ApiaryComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushText(Loc.GetString("garden-apiary-forage-hint"));
    }
}
