using System.Linq;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.ActionBlocker;
using Robust.Shared.Timing;

namespace Content.Server._Eclipse.AdvancedHealth;

/// <summary>Bad manipulations injure the actual wound immediately; closing a procedure never refunds tissue or blood.</summary>
public sealed class AdvancedTreatmentInjurySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _actions = default!;
    [Dependency] private readonly SharedMedicalConditionSystem _functions = default!;
    private readonly Dictionary<(EntityUid User, EntityUid Target, BodyPartSlot Slot), TimeSpan> _lastInjury = new();
    private float _cleanup;

    public override void Initialize() => SubscribeNetworkEvent<AdvancedHealthTreatmentInjuryEvent>(OnInjury);
    private void OnInjury(AdvancedHealthTreatmentInjuryEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } user)
            ApplyInjury(user, GetEntity(ev.Target), ev.Slot, ev.Treatment, ev.Tool is { } tool ? GetEntity(tool) : null, ev.Severity);
    }

    public bool ApplyInjury(EntityUid user, EntityUid target, BodyPartSlot slot, AdvancedTreatmentType treatment, EntityUid? tool, float severity)
    {
        if (!float.IsFinite(severity) || severity <= 0 || !Exists(user) || !Exists(target)
            || !TryComp<AdvancedHealthComponent>(target, out var health) || !health.BodyParts.TryGetValue(slot, out var part)
            || part.IsDestroyed || !_actions.CanInteract(user, target)
            || user != target && !_interaction.InRangeUnobstructed(user, target)) return false;
        var treatments = EntityManager.System<AdvancedTreatmentSystem>();
        if (!treatments.CanTreat(user, tool)) return false;
        if (tool is { } item)
        {
            if (!Exists(item) || !_hands.IsHolding(user, item) || !treatments.MatchesTreatmentItem(item, treatment)) return false;
        }
        else if (treatment != AdvancedTreatmentType.ForeignBodyRemoval || _hands.GetActiveItem(user) != null) return false;
        if (!AdvancedTreatmentRules.IsNeeded(part, treatment)) return false;
        var key = (user, target, slot);
        if (_lastInjury.TryGetValue(key, out var previous) && _timing.CurTime - previous < TimeSpan.FromSeconds(0.35)) return false;
        var wound = treatment == AdvancedTreatmentType.ForeignBodyRemoval
            ? part.Wounds.FirstOrDefault(w => w.HasForeignBody)
            : part.Wounds.OrderByDescending(w => w.ExternalBleedingRate).FirstOrDefault();
        if (wound == null) return false;
        _lastInjury[key] = _timing.CurTime;
        severity = Math.Clamp(severity, 0.5f, 2);
        var pain = 10 + severity * 5;
        wound.Pain += pain;
        health.Pain = Math.Clamp(health.Pain + pain * 0.7f, 0, 100);
        health.TraumaLoad = Math.Min(100, health.TraumaLoad + severity * 1.5f);
        if (treatment is AdvancedTreatmentType.ForeignBodyRemoval or AdvancedTreatmentType.Suture or AdvancedTreatmentType.SurgicalClosure)
        {
            part.SkinIntegrity = Math.Max(0, part.SkinIntegrity - 5 * severity);
            part.MuscleIntegrity = Math.Max(0, part.MuscleIntegrity - 2 * severity);
            if (health.HasBlood)
            {
                part.VesselIntegrity = Math.Max(0, part.VesselIntegrity - 3 * severity);
                wound.ExternalBleedingRate = Math.Min(30, wound.ExternalBleedingRate * (1 + severity * 0.5f) + 0.35f * severity);
                part.IsBleeding = true;
            }
            wound.IsSutured = wound.IsBandaged = false;
            part.IsBandaged = false;
            wound.IsDirty = true;
            wound.InfectionRisk = Math.Min(1, wound.InfectionRisk + severity * 0.05f);
        }
        if (treatment is AdvancedTreatmentType.SurgicalAccess or AdvancedTreatmentType.InternalHemostasis)
        {
            part.MuscleIntegrity = Math.Max(0, part.MuscleIntegrity - 2 * severity);
            if (health.HasBlood)
            {
                part.VesselIntegrity = Math.Max(0, part.VesselIntegrity - severity);
                wound.InternalBleedingRate = Math.Min(30, wound.InternalBleedingRate + 0.1f * severity);
                part.IsBleeding = true;
            }
        }
        Dirty(target, health);
        _functions.Refresh((target, health));
        return true;
    }

    public override void Update(float frameTime)
    {
        _cleanup -= frameTime;
        if (_cleanup > 0) return;
        _cleanup = 10;
        foreach (var (key, time) in _lastInjury.ToArray())
            if (_timing.CurTime - time > TimeSpan.FromSeconds(10)) _lastInjury.Remove(key);
    }
}
