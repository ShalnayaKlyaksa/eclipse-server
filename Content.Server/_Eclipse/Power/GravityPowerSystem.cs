using System.Numerics;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.Generator;
using Content.Server.Singularity.Components;
using Content.Server.Singularity.EntitySystems;
using Content.Shared._Eclipse.Power;
using Content.Shared.Construction.Components;
using Content.Shared.Examine;
using Content.Shared.Power.Generator;
using Robust.Server.GameObjects;

namespace Content.Server._Eclipse.Power;

public sealed partial class GravityPowerSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly GeneratorSystem _generators = default!;
    private readonly List<(EntityUid Uid, GravityPowerSourceComponent Source, PowerPlantComponent Plant, TransformComponent Transform, Vector2 Position)> _sources = new();
    private readonly List<(EntityUid Uid, GravitationalKineticGeneratorComponent Generator, PowerSupplierComponent Supplier, int Source, float Capacity)> _receivers = new();
    private readonly List<float> _capacities = new();
    private float _pulseElapsed;

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
        InitializeHorizon();
    }

    private void OnFuelUsed(Entity<GravityPowerSourceComponent> ent, ref GeneratorUseFuel args)
        => ent.Comp.PendingPower = args.ProducedPower;

    public override void Update(float frameTime)
    {
        _sources.Clear();
        _receivers.Clear();
        _capacities.Clear();
        _pulseElapsed += frameTime;
        var pulse = _pulseElapsed >= .1f ? MathF.Min(_pulseElapsed, .2f) : 0;
        if (pulse > 0)
            _pulseElapsed = 0;
        var residuals = EntityQueryEnumerator<ResidualMicroBlackHoleComponent, TransformComponent>();
        while (residuals.MoveNext(out var uid, out var residual, out var xform))
        {
            if (Paused(uid)) continue;
            if (!TerminatingOrDeleted(uid))
                UpdateEscaped(uid, residual, xform, pulse);
        }
        var query = EntityQueryEnumerator<GravityPowerSourceComponent, PowerPlantComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var source, out var plant, out var xform))
        {
            source.AvailablePower = !Paused(uid) && xform.Anchored && !plant.Tripped && !plant.Failed
                ? MathF.Max(0, source.PendingPower) : 0;
            source.PendingPower = 0; // Each fuel payment can be distributed only once.
            source.HarvestedPower = 0;
            source.Collectors = 0;
            if (Paused(uid) || TerminatingOrDeleted(uid)) continue;
            var position = _transform.GetWorldPosition(uid);
            UpdateContained(uid, source, plant, xform, frameTime, pulse);
            if (source.AvailablePower > 0)
            {
                _sources.Add((uid, source, plant, xform, position));
                _capacities.Add(0);
            }
        }

        var collectors = EntityQueryEnumerator<GravitationalKineticGeneratorComponent, PowerSupplierComponent, TransformComponent>();
        while (collectors.MoveNext(out var uid, out var generator, out var supplier, out var xform))
        {
            supplier.MaxSupply = generator.AvailablePower = 0;
            generator.LocalPull = 0;
            generator.Source = null;
            var selected = -1;
            var capacity = 0f;
            if (!Paused(uid) && xform.Anchored && supplier.Net != null && xform.GridUid != null)
            {
                var position = _transform.GetWorldPosition(uid);
                for (var i = 0; i < _sources.Count; i++)
                {
                    var source = _sources[i];
                    if (source.Source.AvailablePower <= 0 || source.Transform.GridUid != xform.GridUid) continue;
                    var distance = Vector2.Distance(position, source.Position);
                    var horizon = ActiveHorizon(source.Source.Mass);
                    if (distance <= horizon || distance < source.Source.MinimumHarvestRange ||
                        distance - horizon > source.Source.HarvestRange)
                        continue;
                    var pull = PullAt(distance, source.Source.Mass, source.Source.ResidualActivity, false);
                    if (pull <= generator.LocalPull)
                        continue;
                    selected = i;
                    generator.LocalPull = pull;
                    capacity = generator.RatedPower * Math.Clamp(pull / RatedPull, 0, 1);
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
            var paid = MathF.Min(source.Source.StoredEnergy,
                MathF.Max(0, source.Source.HarvestedPower - source.Source.FuelPower) * frameTime);
            source.Source.StoredEnergy -= paid;
            source.Plant.Temperature += MathF.Max(0, source.Source.FuelPower - source.Source.HarvestedPower) *
                frameTime / source.Plant.HeatCapacity;
        }
    }

    private void OnUnanchor(Entity<GravityPowerSourceComponent> ent, ref UnanchorAttemptEvent args)
    {
        if (ent.Comp.ResidualActivity > .001f) args.Cancel();
    }

    private void OnExamined(Entity<GravitationalKineticGeneratorComponent> ent, ref ExaminedEvent args)
        => args.PushMarkup(Loc.GetString("gravity-kinetic-examine", ("power", (int) (ent.Comp.AvailablePower / 1000)),
            ("speed", (int) (ent.Comp.RotorSpeed * 100)), ("pull", MathF.Round(ent.Comp.LocalPull, 2))));
}
