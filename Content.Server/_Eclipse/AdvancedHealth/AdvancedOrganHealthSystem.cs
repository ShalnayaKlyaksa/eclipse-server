using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Body;
using Content.Shared.Rejuvenate;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Eclipse.AdvancedHealth;

public sealed class AdvancedOrganHealthSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<OrganComponent, MapInitEvent>(OnOrganInit, after: [typeof(InitialBodySystem)]);
        SubscribeLocalEvent<AdvancedHealthComponent, MapInitEvent>(OnBodyInit, after: [typeof(InitialBodySystem)]);
        SubscribeLocalEvent<AdvancedHealthComponent, OrganInsertedIntoEvent>(OnInserted);
        SubscribeLocalEvent<AdvancedHealthComponent, OrganRemovedFromEvent>(OnRemoved);
        SubscribeLocalEvent<AdvancedHealthComponent, AdvancedBrainRepairEvent>(OnBrainRepair);
    }

    private void OnOrganInit(Entity<OrganComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Category is not { } category || AdvancedOrganAnatomy.Region(category.Id) is not { } region) return;
        var state = EnsureComp<AdvancedOrganHealthComponent>(ent);
        state.Region = region;
        Publish((ent.Owner, state));
    }

    private void OnBodyInit(Entity<AdvancedHealthComponent> ent, ref MapInitEvent args) => RefreshBody(ent);
    private void OnInserted(Entity<AdvancedHealthComponent> ent, ref OrganInsertedIntoEvent args) => RefreshBody(ent);
    private void OnRemoved(Entity<AdvancedHealthComponent> ent, ref OrganRemovedFromEvent args) => RefreshBody(ent);
    private void OnBrainRepair(Entity<AdvancedHealthComponent> ent, ref AdvancedBrainRepairEvent args)
    {
        foreach (var organ in Organs(ent))
            if (Comp<OrganComponent>(organ).Category?.Id == "Brain") Repair(organ, args.Amount);
    }

    public IEnumerable<Entity<AdvancedOrganHealthComponent>> Organs(EntityUid body)
    {
        if (!_containers.TryGetContainer(body, BodyComponent.ContainerID, out var container)) yield break;
        foreach (var uid in container.ContainedEntities)
            if (TryComp<AdvancedOrganHealthComponent>(uid, out var organ)) yield return (uid, organ);
    }

    public float Function(EntityUid body, string category, float fallback = 1f, bool excludeNutritionalDeficits = false)
    {
        var total = 0f;
        var count = 0;
        foreach (var organ in Organs(body))
            if (Comp<OrganComponent>(organ).Category?.Id == category)
            {
                total += excludeNutritionalDeficits ? CalculateFunction(organ.Comp, true) : organ.Comp.Function;
                count++;
            }
        if (count > 0) return total / count;
        return TryComp<AdvancedHealthComponent>(body, out var health) && health.ExpectedInternalOrgans.Contains(category) ? 0f : fallback;
    }

    public void DamageRegion(EntityUid body, BodyPartSlot region, float amount)
    {
        var candidates = new List<Entity<AdvancedOrganHealthComponent>>();
        foreach (var organ in Organs(body))
            if (organ.Comp.Region == region) candidates.Add(organ);
        if (candidates.Count > 0) Damage(_random.Pick(candidates), amount);
    }

    public void Damage(Entity<AdvancedOrganHealthComponent> organ, float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0) return;
        organ.Comp.Integrity = Math.Max(0, organ.Comp.Integrity - amount);
        organ.Comp.SurgicallyStabilized = false;
        organ.Comp.ExaminedIntegrity = null;
        if (!SetCondition(organ, "AdvancedOrganTrauma", 1f - organ.Comp.Integrity / 100f))
        {
            Publish(organ);
            RefreshOwner(organ);
        }
    }

    public void Repair(Entity<AdvancedOrganHealthComponent> organ, float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0) return;
        organ.Comp.Integrity = Math.Min(100, organ.Comp.Integrity + amount);
        organ.Comp.ExaminedIntegrity = null;
        if (!SetCondition(organ, "AdvancedOrganTrauma", 1f - organ.Comp.Integrity / 100f))
        {
            Publish(organ);
            RefreshOwner(organ);
        }
    }

    /// <summary>Medical systems add prototype-defined conditions; unknown diagnoses stay server-side.</summary>
    public bool SetCondition(Entity<AdvancedOrganHealthComponent> organ, ProtoId<MedicalConditionPrototype> prototype, float severity)
    {
        if (!float.IsFinite(severity) || !_prototypes.HasIndex(prototype)) return false;
        severity = Math.Clamp(severity, 0, 1);
        var existing = organ.Comp.Conditions.Find(condition => condition.Prototype == prototype);
        if (severity <= 0.001f)
        {
            if (existing == null) return false;
            organ.Comp.Conditions.Remove(existing);
            organ.Comp.Discovered.Remove(prototype.Id);
        }
        else if (existing != null)
        {
            if (Math.Abs(existing.Severity - severity) < 0.001f) return false;
            existing.Severity = severity;
        }
        else organ.Comp.Conditions.Add(new MedicalCondition { Prototype = prototype, Severity = severity });
        Publish(organ);
        RefreshOwner(organ);
        return true;
    }

    /// <summary>Only authoritative completed medical procedures may call this; no client reveal message exists.</summary>
    public void Examine(Entity<AdvancedOrganHealthComponent> organ, MedicalDetection level)
    {
        foreach (var condition in organ.Comp.Conditions)
            if (_prototypes.Index(condition.Prototype).Detection <= level) organ.Comp.Discovered.Add(condition.Prototype.Id);
        if (level >= MedicalDetection.Examination) organ.Comp.ExaminedIntegrity = organ.Comp.Integrity;
        Publish(organ);
    }

    public void ExamineBody(EntityUid body, MedicalDetection level)
    {
        foreach (var organ in Organs(body)) Examine(organ, level);
    }

    private float CalculateFunction(AdvancedOrganHealthComponent organ, bool excludeNutritionalDeficits = false)
    {
        var function = organ.Integrity / 100f;
        foreach (var condition in organ.Conditions)
        {
            // Reversible lack of fuel is not structural injury or a digestive/renal disease.
            if (excludeNutritionalDeficits && condition.Prototype.Id is "AdvancedOrganMalnutrition" or "AdvancedRenalDehydration")
                continue;
            var prototype = _prototypes.Index(condition.Prototype);
            function *= Math.Max(0f, 1f + (prototype.FunctionMultiplier - 1f) * condition.Severity);
        }
        return Math.Clamp(function, 0, 1);
    }

    private void Publish(Entity<AdvancedOrganHealthComponent> organ)
    {
        organ.Comp.Function = CalculateFunction(organ.Comp);
        organ.Comp.KnownConditions.Clear();
        foreach (var condition in organ.Comp.Conditions)
        {
            var prototype = _prototypes.Index(condition.Prototype);
            if (prototype.Detection == MedicalDetection.Chip || organ.Comp.Discovered.Contains(prototype.ID))
                organ.Comp.KnownConditions.Add(new MedicalCondition { Prototype = condition.Prototype, Severity = condition.Severity });
        }
        Dirty(organ);
    }

    private void RefreshOwner(Entity<AdvancedOrganHealthComponent> organ)
    {
        if (TryComp<OrganComponent>(organ, out var anatomy) && anatomy.Body is { } body
            && TryComp<AdvancedHealthComponent>(body, out var health)) RefreshBody((body, health));
    }

    public void RefreshBody(Entity<AdvancedHealthComponent> body)
    {
        var present = new HashSet<string>();
        var impaired = false;
        var viableBrain = false;
        foreach (var organ in Organs(body))
        {
            if (Comp<OrganComponent>(organ).Category is not { } category) continue;
            present.Add(category.Id);
            if (category.Id == "Brain" && organ.Comp.Integrity > 0) viableBrain = true;
            if (category.Id is not ("Uterus" or "OvaryLeft" or "OvaryRight" or "VaginalCanal" or "Prostate"))
                body.Comp.ExpectedInternalOrgans.Add(category.Id);
            impaired |= organ.Comp.Function < 0.999f;
        }
        impaired |= !body.Comp.ExpectedInternalOrgans.IsSubsetOf(present);
        var lungs = Function(body, "Lungs");
        var brain = Function(body, "Brain");
        var brainDestroyed = body.Comp.ExpectedInternalOrgans.Contains("Brain") && !viableBrain;
        if (body.Comp.HasOrganDysfunction == impaired && Math.Abs(body.Comp.PulmonaryFunction - lungs) < 0.001f
            && Math.Abs(body.Comp.BrainFunction - brain) < 0.001f && body.Comp.IsBrainDestroyed == brainDestroyed) return;
        body.Comp.HasOrganDysfunction = impaired;
        body.Comp.PulmonaryFunction = lungs;
        body.Comp.BrainFunction = brain;
        body.Comp.IsBrainDestroyed = brainDestroyed;
        Dirty(body);
    }

    public void RejuvenateBody(Entity<AdvancedHealthComponent> ent)
    {
        foreach (var organ in Organs(ent))
        {
            organ.Comp.Integrity = 100;
            organ.Comp.SurgicallyStabilized = false;
            organ.Comp.Conditions.Clear();
            organ.Comp.Discovered.Clear();
            organ.Comp.ExaminedIntegrity = null;
            Publish(organ);
        }
        RefreshBody(ent);
    }
}
