using System.Linq;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Lightning;
using Content.Server.Power.Components;
using Content.Server.Power.NodeGroups;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Atmos.Components;
using Content.Shared.Emp;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._Eclipse.Industrial;

/// <summary>Finite external surge pulses travel along existing electrical node groups. Normal excess generation is harmless.</summary>
public sealed class IndustrialPowerSurgeSystem : EntitySystem
{
    [Dependency] private FlammableSystem _fire = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;
    private readonly Dictionary<INodeGroup, Pulse> _pulses = new();
    private readonly HashSet<EntityUid> _cooling = new();
    private float _elapsed;

    public override void Initialize()
    {
        SubscribeLocalEvent<NodeContainerComponent, HitByLightningEvent>(OnLightning);
        SubscribeLocalEvent<NodeContainerComponent, EmpPulseEvent>(OnEmp);
        SubscribeLocalEvent<PowerSurgeCableComponent, ExaminedEvent>(OnCableExamined);
        SubscribeLocalEvent<IndustrialSurgeProtectorComponent, ExaminedEvent>(OnProtectorExamined);
        SubscribeLocalEvent<IndustrialSurgeProtectorComponent, InteractUsingEvent>(OnReplaceFuse);
    }

    private void OnLightning(Entity<NodeContainerComponent> ent, ref HitByLightningEvent args)
    {
        // Tesla collectors intentionally receive lightning and already condition their output.
        if (HasComp<Content.Server.Tesla.Components.TeslaCoilComponent>(ent))
            return;
        if (HasComp<CableComponent>(ent) || HasComp<ApcComponent>(ent) ||
            HasComp<PowerNetworkBatteryComponent>(ent) || HasComp<IndustrialSurgeProtectorComponent>(ent))
            Inject(ent, 2f, 5f, 100000);
    }

    private void OnEmp(Entity<NodeContainerComponent> ent, ref EmpPulseEvent args)
    {
        if (args.EnergyConsumption <= 0)
            return;
        if (Inject(ent, 1.5f, Math.Clamp((float)args.Duration.TotalSeconds, 1, 5), args.EnergyConsumption))
            args.Affected = true;
    }

    /// <summary>Apply a finite pulse once per physical network. A radial EMP hitting many nodes does not multiply it.</summary>
    public bool Inject(EntityUid source, float peak, float duration, float energy)
    {
        if (!float.IsFinite(peak) || !float.IsFinite(duration) || !float.IsFinite(energy) ||
            peak <= 1 || duration <= 0 || energy <= 0 || !TryComp<NodeContainerComponent>(source, out var container))
            return false;
        var affected = false;
        foreach (var group in container.Nodes.Values.Select(n => n.NodeGroup).OfType<INodeGroup>().Distinct())
        {
            if (group is not IBasePowerNet)
                continue;
            if (_pulses.TryGetValue(group, out var existing) && existing.Tick == _timing.CurTick)
                continue;
            var cables = group.Nodes.Select(n => n.Owner).Distinct()
                .Where(uid => !TerminatingOrDeleted(uid) && HasComp<PowerSurgeCableComponent>(uid)).ToArray();
            if (cables.Length == 0)
                continue;
            var remaining = energy;
            foreach (var uid in group.Nodes.Select(n => n.Owner).Distinct().OrderBy(uid => uid))
            {
                if (!TryComp<IndustrialSurgeProtectorComponent>(uid, out var protector) || protector.RemainingCapacity <= 0)
                    continue;
                var absorbed = MathF.Min(protector.RemainingCapacity, remaining);
                protector.RemainingCapacity -= absorbed;
                remaining -= absorbed;
                if (remaining <= 0)
                    break;
            }
            // Keep even a fully absorbed pulse until expiry, so one EMP cannot spend every fuse in its radius.
            var residualPeak = 1 + (peak - 1) * remaining / energy;
            if (existing != null)
            {
                existing.Peak = MathF.Max(existing.Peak, residualPeak);
                existing.Remaining = MathF.Max(existing.Remaining, MathF.Min(duration, 10));
                existing.Tick = _timing.CurTick;
            }
            else
                _pulses.Add(group, new Pulse(cables, residualPeak, MathF.Min(duration, 10), _timing.CurTick));
            _popup.PopupEntity(Loc.GetString(residualPeak > 1.25f
                ? "industrial-power-surge-warning" : "industrial-power-surge-absorbed"), source);
            affected = true;
        }
        return affected;
    }

    public override void Update(float frameTime)
    {
        if (_pulses.Count == 0 && _cooling.Count == 0)
        {
            _elapsed = 0;
            return;
        }
        _elapsed += frameTime;
        if (_elapsed < .5f)
            return;
        var elapsed = MathF.Min(_elapsed, 1);
        _elapsed = 0;
        foreach (var uid in _cooling.ToArray())
        {
            if (!TryComp<PowerSurgeCableComponent>(uid, out var cable) || TerminatingOrDeleted(uid))
            {
                _cooling.Remove(uid);
                continue;
            }
            cable.Stress = MathF.Max(0, cable.Stress - cable.RecoveryPerSecond * elapsed);
            if (cable.Stress == 0)
                _cooling.Remove(uid);
        }
        foreach (var (group, pulse) in _pulses.ToArray())
        {
            var step = MathF.Min(elapsed, pulse.Remaining);
            pulse.Remaining -= step;
            foreach (var uid in pulse.Cables)
            {
                if (TerminatingOrDeleted(uid) || !Transform(uid).Anchored || Paused(uid) ||
                    !TryComp<PowerSurgeCableComponent>(uid, out var cable) || pulse.Peak <= cable.VoltageTolerance)
                    continue;
                cable.Stress += (pulse.Peak - cable.VoltageTolerance) * step;
                cable.LastPeak = pulse.Peak;
                _cooling.Add(uid);
                if (cable.Stress < cable.BurnThreshold || !TryComp<FlammableComponent>(uid, out var flammable))
                    continue;
                _fire.AdjustFireStacks(uid, 3, flammable);
                _fire.Ignite(uid, uid, flammable);
                // The first failing link ends this pulse; a single fault must not ignite the entire station simultaneously.
                pulse.Remaining = 0;
                break;
            }
            if (pulse.Remaining <= 0)
                _pulses.Remove(group);
        }
    }

    private void OnCableExamined(Entity<PowerSurgeCableComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
        {
            args.PushMarkup(Loc.GetString("industrial-power-cable-insulation", ("percent", (int)Math.Clamp(100 * ent.Comp.Stress / ent.Comp.BurnThreshold, 0, 100))));
            if (ent.Comp.LastPeak > 1)
                args.PushMarkup(Loc.GetString("industrial-power-last-surge", ("peak", ent.Comp.LastPeak.ToString("0.##"))));
        }
    }

    private void OnProtectorExamined(Entity<IndustrialSurgeProtectorComponent> ent, ref ExaminedEvent args)
        => args.PushMarkup(Loc.GetString("industrial-power-protector-capacity", ("remaining", (int)(ent.Comp.RemainingCapacity / 1000)), ("maximum", (int)(ent.Comp.Capacity / 1000))));

    private void OnReplaceFuse(Entity<IndustrialSurgeProtectorComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || Prototype(args.Used)?.ID != "IndustrialSurgeFuse" || ent.Comp.RemainingCapacity >= ent.Comp.Capacity)
            return;
        ent.Comp.RemainingCapacity = ent.Comp.Capacity;
        Del(args.Used);
        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("industrial-power-fuse-replaced"), ent, args.User);
    }

    private sealed class Pulse(EntityUid[] cables, float peak, float remaining, GameTick tick)
    {
        public readonly EntityUid[] Cables = cables;
        public float Peak = peak;
        public float Remaining = remaining;
        public GameTick Tick = tick;
    }
}
