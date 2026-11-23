using System.Linq;
using Content.Server.Botany.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Power.Components;
using Robust.Shared.Timing;

namespace Content.Server.Eclipse.Botany.Systems;

/// <summary>Shares a small, finite wireless supply among nearby anchored machines.</summary>
public sealed class EnergyFlowerSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private readonly HashSet<EntityUid> _supplied = new();
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(PowerNetSystem));
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        RefreshSupply();
    }

    public void RefreshSupply()
    {
        foreach (var uid in _supplied)
        {
            if (TryComp<ApcPowerReceiverComponent>(uid, out var receiver))
                receiver.BotanicalPower = 0;
        }
        _supplied.Clear();

        var query = EntityQueryEnumerator<PlantHolderComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var plant, out var xform))
        {
            if (plant.Seed is not { PowerProduction: > 0 } seed || plant.Dead || Paused(uid)
                || plant.Health <= 0 || plant.Age < seed.Maturation || !xform.Anchored || xform.GridUid == null)
                continue;

            var budget = seed.PowerProduction * Math.Clamp(seed.Potency / 30f, 0.5f, 1.5f);
            // Smaller appliances get first choice; a flower cannot run an entire department.
            foreach (var target in _lookup.GetEntitiesInRange<ApcPowerReceiverComponent>(xform.Coordinates, 2.5f)
                         .OrderBy(ent => ent.Comp.Load).ThenBy(ent => ent.Owner.Id))
            {
                var targetXform = Transform(target.Owner);
                if (!targetXform.Anchored || targetXform.GridUid != xform.GridUid || Paused(target.Owner)
                    || target.Comp.PowerDisabled || !target.Comp.NeedsPower
                    || HasComp<ApcPowerReceiverBatteryComponent>(target.Owner))
                    continue;
                var missing = Math.Max(0, target.Comp.Load - target.Comp.PowerReceived);
                var supplied = Math.Min(missing, budget);
                if (supplied <= 0)
                    continue;
                target.Comp.BotanicalPower += supplied;
                _supplied.Add(target.Owner);
                budget -= supplied;
                if (budget <= 0)
                    break;
            }
        }
    }
}
