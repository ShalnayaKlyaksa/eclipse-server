using System.Numerics;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.Generator;
using Content.Server.Singularity.Components;
using Content.Server.Singularity.EntitySystems;
using Content.Shared._Eclipse.Power;
using Content.Shared.Camera;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Examine;
using Content.Shared.Item;
using Content.Shared.Power.Generator;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.Server._Eclipse.Power;

public sealed class GravityPowerSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _camera = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    private readonly List<(EntityUid Uid, GravityPowerSourceComponent Source, PowerPlantComponent Plant, TransformComponent Transform, Vector2 Position)> _sources = new();
    private readonly List<(EntityUid Uid, GravitationalKineticGeneratorComponent Generator, PowerSupplierComponent Supplier, int Source, float Capacity)> _receivers = new();
    private readonly List<float> _capacities = new();
    private readonly HashSet<EntityUid> _nearby = new();
    private float _hazardElapsed;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(GeneratorSystem));
        UpdatesAfter.Add(typeof(PowerPlantSystem));
        UpdatesBefore.Add(typeof(PowerNetSystem));
        UpdatesBefore.Add(typeof(GravityWellSystem));
        SubscribeLocalEvent<GravityPowerSourceComponent, GeneratorUseFuel>(OnFuelUsed);
        SubscribeLocalEvent<GravityPowerSourceComponent, EntityTerminatingEvent>(OnDestroyed);
        SubscribeLocalEvent<GravityPowerSourceComponent, UnanchorAttemptEvent>(OnUnanchor);
        SubscribeLocalEvent<GravitationalKineticGeneratorComponent, ExaminedEvent>(OnExamined);
    }

    private void OnFuelUsed(Entity<GravityPowerSourceComponent> ent, ref GeneratorUseFuel args)
        => ent.Comp.PendingPower = args.ProducedPower;

    public override void Update(float frameTime)
    {
        _sources.Clear();
        _receivers.Clear();
        _capacities.Clear();
        _hazardElapsed += frameTime;
        var hazardStep = _hazardElapsed >= .5f ? MathF.Min(_hazardElapsed, 1) : 0;
        if (hazardStep > 0) _hazardElapsed = 0;
        var residuals = EntityQueryEnumerator<ResidualMicroBlackHoleComponent, TransformComponent>();
        while (residuals.MoveNext(out var uid, out var residual, out var xform))
        {
            if (Paused(uid)) continue;
            residual.Activity = MathF.Max(0, residual.Activity - frameTime * .12f);
            UpdateEffects(uid, residual.Activity, 1, 35, xform, _transform.GetWorldPosition(uid), hazardStep);
            if (residual.Activity <= 0) QueueDel(uid);
        }
        var query = EntityQueryEnumerator<GravityPowerSourceComponent, PowerPlantComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var source, out var plant, out var xform))
        {
            source.AvailablePower = !Paused(uid) && xform.Anchored && !plant.Tripped && !plant.Failed
                ? MathF.Max(0, source.PendingPower) : 0;
            source.PendingPower = 0; // Each fuel payment can be distributed only once.
            source.HarvestedPower = 0;
            source.Collectors = 0;
            if (Paused(uid)) continue;
            var position = _transform.GetWorldPosition(uid);
            UpdateWell(uid, source, plant, xform, position, frameTime, hazardStep);
            _sources.Add((uid, source, plant, xform, position));
            _capacities.Add(0);
        }

        var collectors = EntityQueryEnumerator<GravitationalKineticGeneratorComponent, PowerSupplierComponent, TransformComponent>();
        while (collectors.MoveNext(out var uid, out var generator, out var supplier, out var xform))
        {
            supplier.MaxSupply = generator.AvailablePower = 0;
            generator.Source = null;
            var selected = -1;
            var capacity = 0f;
            var closest = float.MaxValue;
            if (!Paused(uid) && xform.Anchored && supplier.Net != null && xform.GridUid != null)
            {
                var position = _transform.GetWorldPosition(uid);
                for (var i = 0; i < _sources.Count; i++)
                {
                    var source = _sources[i];
                    if (source.Source.AvailablePower <= 0 || source.Transform.GridUid != xform.GridUid) continue;
                    var distance = Vector2.Distance(position, source.Position);
                    if (distance < source.Source.MinimumHarvestRange || distance > source.Source.HarvestRange || distance >= closest) continue;
                    selected = i;
                    closest = distance;
                    capacity = generator.RatedPower * MathF.Pow(source.Source.MinimumHarvestRange / distance, 2);
                }
            }
            generator.RotorSpeed = Math.Clamp(generator.RotorSpeed + (selected >= 0 ? frameTime : -frameTime) * .25f, 0, 1);
            if (selected < 0)
            {
                supplier.Enabled = false;
                _appearance.SetData(uid, GeneratorVisuals.Running, false);
                continue;
            }
            capacity *= generator.RotorSpeed;
            _capacities[selected] += capacity;
            _receivers.Add((uid, generator, supplier, selected, capacity));
        }
        foreach (var receiver in _receivers)
        {
            var source = _sources[receiver.Source];
            var power = receiver.Capacity * MathF.Min(1, source.Source.AvailablePower / MathF.Max(1, _capacities[receiver.Source]));
            power = MathF.Min(power, MathF.Max(0, source.Source.AvailablePower - source.Source.HarvestedPower));
            receiver.Supplier.Enabled = true;
            receiver.Supplier.MaxSupply = receiver.Generator.AvailablePower = power;
            receiver.Generator.Source = source.Uid;
            _appearance.SetData(receiver.Uid, GeneratorVisuals.Running, power > 1);
            source.Source.HarvestedPower += power;
            source.Source.Collectors++;
        }
        foreach (var source in _sources)
        {
            // Uncollected mechanical work is dissipated at the source, not duplicated as electricity.
            source.Plant.Temperature += MathF.Max(0, source.Source.AvailablePower - source.Source.HarvestedPower) *
                frameTime / source.Plant.HeatCapacity;
        }
    }

    private void UpdateWell(EntityUid uid, GravityPowerSourceComponent source, PowerPlantComponent plant,
        TransformComponent xform, Vector2 position, float frameTime, float hazardStep)
    {
        var target = source.AvailablePower > 0 ? MathF.Max(.1f, plant.Warmup) : 0;
        var loss = plant.Condition == PowerPlantCondition.MissingField || plant.FieldCount < plant.RequiredFields;
        source.ResidualActivity = target > source.ResidualActivity ? target :
            MathF.Max(target, source.ResidualActivity - frameTime * (loss ? .12f : .5f));
        var activity = source.ResidualActivity;
        var instability = activity <= .001f ? 0 : MathF.Max(loss ? 1 : 0,
            Math.Clamp((plant.Temperature - plant.ShutdownTemperature * .8f) / (plant.ShutdownTemperature * .2f), 0, 1));
        UpdateEffects(uid, activity, instability, source.StablePull + (source.UnstablePull - source.StablePull) * instability,
            xform, position, hazardStep);
    }

    private void UpdateEffects(EntityUid uid, float activity, float instability, float pull,
        TransformComponent xform, Vector2 position, float hazardStep)
    {
        var well = EnsureComp<GravityWellComponent>(uid);
        well.MaxRange = activity > .001f ? 5 + instability * 5 : -1;
        well.MinRange = .25f;
        well.BaseRadialAcceleration = activity * pull;
        well.BaseTangentialAcceleration = well.BaseRadialAcceleration * (.12f + instability * .25f);
        if (TryComp<MicroBlackHoleVisualComponent>(uid, out var visual) &&
            (MathF.Abs(visual.Activity - activity) > .01f || MathF.Abs(visual.Instability - instability) > .01f))
        {
            visual.Activity = activity;
            visual.Instability = instability;
            Dirty(uid, visual);
        }
        if (hazardStep <= 0 || activity <= .001f) return;
        _nearby.Clear();
        _lookup.GetEntitiesInRange(xform.MapID, position, well.MaxRange, _nearby,
            LookupFlags.Dynamic | LookupFlags.Sundries | LookupFlags.Static);
        foreach (var entity in _nearby)
        {
            if (entity == uid || !TryComp<PhysicsComponent>(entity, out _) || Paused(entity)) continue;
            var delta = position - _transform.GetWorldPosition(entity);
            var distance = delta.Length();
            if (distance < .35f && !Transform(entity).Anchored && HasComp<ItemComponent>(entity))
            {
                QueueDel(entity);
                continue;
            }
            if (instability > .1f && distance > .01f)
                _camera.KickCamera(entity, delta / distance * (instability * activity * .025f / MathF.Max(1, distance)));
            if (distance < .8f + instability * 1.2f && HasComp<DamageableComponent>(entity))
                _damage.TryChangeDamage(entity, new DamageSpecifier { DamageDict = { ["Blunt"] = (8 + instability * 42) * activity * hazardStep } }, origin: uid);
        }
    }

    private void OnDestroyed(Entity<GravityPowerSourceComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.ResidualActivity <= .001f) return;
        var xform = Transform(ent);
        if (xform.GridUid is not { } grid || TerminatingOrDeleted(grid)) return;
        var residual = Spawn("ResidualMicroBlackHole", xform.Coordinates);
        Comp<ResidualMicroBlackHoleComponent>(residual).Activity = ent.Comp.ResidualActivity;
        var visual = Comp<MicroBlackHoleVisualComponent>(residual);
        visual.Activity = ent.Comp.ResidualActivity;
        visual.Instability = 1;
        Dirty(residual, visual);
    }

    private void OnUnanchor(Entity<GravityPowerSourceComponent> ent, ref UnanchorAttemptEvent args)
    {
        if (ent.Comp.ResidualActivity > .001f) args.Cancel();
    }

    private void OnExamined(Entity<GravitationalKineticGeneratorComponent> ent, ref ExaminedEvent args)
        => args.PushMarkup(Loc.GetString("gravity-kinetic-examine", ("power", (int) (ent.Comp.AvailablePower / 1000)),
            ("speed", (int) (ent.Comp.RotorSpeed * 100))));
}
