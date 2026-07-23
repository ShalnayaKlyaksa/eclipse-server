using Content.Shared.Alert;
using Content.Shared._Eclipse.AdvancedHealth;
using System.Linq;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Stacks;
using Content.Shared.StepTrigger.Components;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.AdvancedHealth;

public sealed class AdvancedHealthSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    /// <summary>Anatomy used when the entity's species has no anatomy prototype of its own.</summary>
    private static readonly ProtoId<AdvancedHealthAnatomyPrototype> FallbackAnatomy = "Human";

    public override void Initialize()
    {
        SubscribeLocalEvent<AdvancedHealthComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<AdvancedHealthComponent, BeforeAlertSeverityCheckEvent>(OnAlertSeverity);
    }

    private void OnAlertSeverity(EntityUid uid, AdvancedHealthComponent comp, BeforeAlertSeverityCheckEvent args)
    {
        if (args.CurrentAlert.Id != "HumanHealth")
            return;

        args.Severity = SeverityWithHysteresis(comp.SmoothedConditionBadness, comp.CachedAlertSeverity);
        comp.CachedAlertSeverity = args.Severity;
        args.CancelUpdate = true;
    }

    /// <summary>
    /// Applies a deadband around the level boundaries so the readout can't flicker between two
    /// severities when the condition score sits near a threshold.
    /// </summary>
    private static short SeverityWithHysteresis(float badness, short current)
    {
        const float margin = 0.05f;
        var raw = BadnessToSeverity(badness);
        if (raw == current)
            return current;

        // Only step to a worse level once clearly past the boundary; likewise for a better level.
        if (raw > current)
            return BadnessToSeverity(badness - margin) > current ? raw : current;
        return BadnessToSeverity(badness + margin) < current ? raw : current;
    }

    public static float ComputeInstantBadness(AdvancedHealthComponent h)
    {
        var bloodLoss = h.HasBlood
            ? 1f - Math.Clamp(h.BloodVolume / Math.Max(1f, h.MaxBloodVolume), 0f, 1f)
            : 0f;
        var consciousnessLoss = 1f - Math.Clamp(h.Consciousness / 100f, 0f, 1f);
        var painLevel = h.HasPain ? Math.Clamp(h.Pain / 100f, 0f, 1f) : 0f;

        return (bloodLoss + consciousnessLoss + painLevel) / 3f;
    }

    public static short BadnessToSeverity(float badness)
    {
        return badness switch
        {
            < 0.15f => 0,
            < 0.32f => 1,
            < 0.52f => 2,
            < 0.72f => 3,
            _ => 4,
        };
    }

    private void OnInit(Entity<AdvancedHealthComponent> ent, ref ComponentInit args)
    {
        foreach (var slot in Enum.GetValues<BodyPartSlot>())
        {
            if (!ent.Comp.BodyParts.ContainsKey(slot))
            {
                ent.Comp.BodyParts[slot] = new BodyPartState
                {
                    Slot = slot,
                    OrganIntegrity = slot.IsCore() ? 100f : 0f,
                };
            }
        }

        if (ent.Comp.BodyPartHitWeights.Count == 0)
        {
            ent.Comp.BodyPartHitWeights = new()
            {
                [BodyPartSlot.Head] = 8, [BodyPartSlot.Neck] = 3, [BodyPartSlot.Chest] = 22,
                [BodyPartSlot.Abdomen] = 18, [BodyPartSlot.Pelvis] = 8,
                [BodyPartSlot.LeftUpperArm] = 5, [BodyPartSlot.LeftForearm] = 4, [BodyPartSlot.LeftHand] = 2,
                [BodyPartSlot.RightUpperArm] = 5, [BodyPartSlot.RightForearm] = 4, [BodyPartSlot.RightHand] = 2,
                [BodyPartSlot.LeftThigh] = 6, [BodyPartSlot.LeftShin] = 4, [BodyPartSlot.LeftFoot] = 2,
                [BodyPartSlot.RightThigh] = 6, [BodyPartSlot.RightShin] = 4, [BodyPartSlot.RightFoot] = 2,
            };
        }

        if (ent.Comp.BodyPartAccuracyPenalties.Count == 0)
        {
            ent.Comp.BodyPartAccuracyPenalties = new()
            {
                [BodyPartSlot.Chest] = 0, [BodyPartSlot.Abdomen] = -10, [BodyPartSlot.Pelvis] = -15,
                [BodyPartSlot.Head] = -40, [BodyPartSlot.Neck] = -55,
                [BodyPartSlot.LeftUpperArm] = -25, [BodyPartSlot.RightUpperArm] = -25,
                [BodyPartSlot.LeftForearm] = -35, [BodyPartSlot.RightForearm] = -35,
                [BodyPartSlot.LeftHand] = -50, [BodyPartSlot.RightHand] = -50,
                [BodyPartSlot.LeftThigh] = -20, [BodyPartSlot.RightThigh] = -20,
                [BodyPartSlot.LeftShin] = -30, [BodyPartSlot.RightShin] = -30,
                [BodyPartSlot.LeftFoot] = -45, [BodyPartSlot.RightFoot] = -45,
            };
        }

        foreach (var type in Enum.GetValues<WoundType>())
        {
            ent.Comp.WoundBleedingRates.TryAdd(type, type switch
            {
                WoundType.Gunshot => 0.08f, WoundType.Puncture => 0.07f, WoundType.Cut => 0.06f,
                WoundType.Shrapnel => 0.065f, WoundType.OrganDamage => 0.04f, _ => 0f,
            });
            ent.Comp.WoundPainValues.TryAdd(type, type switch
            {
                WoundType.Fracture => 1.1f, WoundType.Burn => 0.9f, WoundType.Gunshot => 0.8f, _ => 0.7f,
            });
            ent.Comp.WoundTraumaValues.TryAdd(type, type switch
            {
                WoundType.Gunshot => 1f, WoundType.OrganDamage => 1.2f, WoundType.Fracture => 0.8f, _ => 0.6f,
            });
        }

        ApplyAnatomy(ent);

        if (ent.Comp.RandomizeBloodGroup && ent.Comp.BloodTypes.Count > 0)
        {
            ent.Comp.BloodType = _random.Pick(ent.Comp.BloodTypes);
        }

        ent.Comp.SmoothedConditionBadness = ComputeInstantBadness(ent.Comp);
        Dirty(ent);
    }

    private void ApplyAnatomy(Entity<AdvancedHealthComponent> ent)
    {
        var profileId = ent.Comp.AnatomyProfile?.ToString();
        if (profileId == null && TryComp<HumanoidProfileComponent>(ent, out var humanoid))
            profileId = humanoid.Species;

        if (profileId == null || !_prototypes.TryIndex<AdvancedHealthAnatomyPrototype>(profileId, out var anatomy))
            _prototypes.TryIndex(FallbackAnatomy, out anatomy);
        if (anatomy == null)
            return;

        ent.Comp.MaxBloodVolume = anatomy.MaxBloodVolume;
        ent.Comp.BloodVolume = anatomy.MaxBloodVolume;
        ent.Comp.HasBlood = anatomy.HasBlood;
        ent.Comp.HasPain = anatomy.HasPain;
        ent.Comp.NeedsOxygen = anatomy.NeedsOxygen;
        ent.Comp.CanFracture = anatomy.CanFracture;
        ent.Comp.BleedingModifier = anatomy.BleedingModifier;
        ent.Comp.PainModifier = anatomy.PainModifier;
        ent.Comp.TraumaModifier = anatomy.TraumaModifier;
        ent.Comp.OxygenationModifier = anatomy.OxygenationModifier;
        ent.Comp.BodyFluid = anatomy.BodyFluid;
        ent.Comp.FluidColor = anatomy.FluidColor;
        ent.Comp.BloodTypes = anatomy.BloodTypes;
        ent.Comp.IncompatibleTransfusionPopup = anatomy.IncompatibleTransfusionPopup;
        if (ent.Comp.BloodTypes.Count > 0 && !ent.Comp.BloodTypes.Contains(ent.Comp.BloodType))
            ent.Comp.BloodType = ent.Comp.BloodTypes[0];

        foreach (var slot in Enum.GetValues<BodyPartSlot>())
        {
            anatomy.BodyParts.TryGetValue(slot, out var partOverride);
            if (partOverride is { Enabled: false })
            {
                ent.Comp.BodyPartHitWeights[slot] = 0;
                continue;
            }

            var skin = partOverride?.SkinIntegrity ?? anatomy.DefaultSkinIntegrity;
            var muscle = partOverride?.MuscleIntegrity ?? anatomy.DefaultMuscleIntegrity;
            var bone = anatomy.CanFracture
                ? partOverride?.BoneIntegrity ?? anatomy.DefaultBoneIntegrity
                : 0f;
            var vessel = partOverride?.VesselIntegrity ?? anatomy.DefaultVesselIntegrity;
            var nerve = partOverride?.NerveIntegrity ?? anatomy.DefaultNerveIntegrity;
            var organ = slot.IsCore()
                ? partOverride?.OrganIntegrity ?? anatomy.DefaultOrganIntegrity
                : 0f;

            ent.Comp.BodyParts[slot] = new BodyPartState
            {
                Slot = slot,
                SkinIntegrity = skin, MaxSkinIntegrity = skin,
                MuscleIntegrity = muscle, MaxMuscleIntegrity = muscle,
                BoneIntegrity = bone, MaxBoneIntegrity = MathF.Max(1f, bone),
                VesselIntegrity = vessel, MaxVesselIntegrity = vessel,
                NerveIntegrity = nerve, MaxNerveIntegrity = nerve,
                OrganIntegrity = organ, MaxOrganIntegrity = MathF.Max(1f, organ),
            };

            if (partOverride != null && ent.Comp.BodyPartHitWeights.TryGetValue(slot, out var weight))
                ent.Comp.BodyPartHitWeights[slot] = weight * partOverride.HitWeightModifier;
        }
    }
}

public sealed class DamageToWoundSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;

    private const float MinWoundDamage = 4f;
    private const float MinStepWoundDamage = 1f;
    private const int MaxWoundsPerPart = 12;

    public override void Initialize()
    {
        SubscribeLocalEvent<AdvancedHealthComponent, DamageModifyEvent>(OnDamageModify, after: [typeof(InventorySystem)]);
        SubscribeLocalEvent<AdvancedHealthComponent, DamageChangedEvent>(OnDamage);
    }

    private void OnDamageModify(Entity<AdvancedHealthComponent> ent, ref DamageModifyEvent args)
    {
        var ctx = EnsureComp<AdvancedHealthHitContextComponent>(ent);
        var original = DamageSpecifier.GetPositive(args.OriginalDamage);
        var modified = DamageSpecifier.GetPositive(args.Damage);

        var dominant = original.DamageDict
            .Where(x => x.Value > FixedPoint2.Zero)
            .OrderByDescending(x => x.Value)
            .FirstOrDefault();

        ctx.DominantDamageType = dominant.Key;
        ctx.DominantOriginal = dominant.Value.Float();
        ctx.DominantModified = modified.DamageDict.GetValueOrDefault(dominant.Key).Float();
    }

    private void OnDamage(Entity<AdvancedHealthComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta is null)
            return;

        var positive = DamageSpecifier.GetPositive(args.DamageDelta);
        var residual = positive.GetTotal().Float();
        var minDamage = MinWoundDamage;

        TryComp<AdvancedHealthHitContextComponent>(ent, out var hitCtx);

        // Physiological damage (blood loss, asphyxiation, poison, radiation) is fully modelled by
        // AdvancedHealth itself. Turning it into mechanical wounds would make the two systems feed
        // back into each other, so only real trauma becomes a wound.
        if (IsPhysiologicalDamage(hitCtx?.DominantDamageType))
            return;

        var isStepDamage = args.Origin != null && HasComp<StepTriggerComponent>(args.Origin.Value);
        var source = args.Origin;
        if (isStepDamage)
            minDamage = MinStepWoundDamage;
        else if (source != null && TryComp<ProjectileComponent>(source, out var projectile))
            source = projectile.Shooter;

        if (residual < minDamage)
            return;

        var original = hitCtx?.DominantOriginal ?? residual;
        var penetration = GetPenetrationFactor(hitCtx);

        var slot = isStepDamage ? PickFootSlot() : SelectSlot(ent.Comp, source);
        var baseType = GetWoundType(hitCtx?.DominantDamageType);
        var (type, amount) = ResolveArmoredWound(baseType, penetration, original, residual);
        if (amount < minDamage)
            return;

        var severity = amount switch
        {
            < 15 => WoundSeverity.Minor,
            < 30 => WoundSeverity.Moderate,
            < 55 => WoundSeverity.Severe,
            _ => WoundSeverity.Critical,
        };

        if (type is WoundType.Gunshot or WoundType.Puncture or WoundType.Shrapnel && penetration < 0.7f)
            severity = (WoundSeverity) Math.Max(0, (int) severity - 1);

        CreateWound(ent, slot, type, severity, amount);
    }

    private static float GetPenetrationFactor(AdvancedHealthHitContextComponent? ctx)
    {
        if (ctx == null || ctx.DominantOriginal <= 0f)
            return 1f;

        return Math.Clamp(ctx.DominantModified / ctx.DominantOriginal, 0f, 1f);
    }

    private (WoundType type, float amount) ResolveArmoredWound(
        WoundType baseType, float penetration, float originalAmount, float residualAmount)
    {
        if (baseType is not (WoundType.Gunshot or WoundType.Puncture or WoundType.Shrapnel))
            return (baseType, residualAmount);

        if (penetration < 0.10f && residualAmount < 8f)
            return (baseType, 0f);

        if (penetration < 0.10f)
            return (WoundType.Bruise, Math.Max(MinWoundDamage, originalAmount * 0.18f));

        if (penetration >= 0.65f)
        {
            if (penetration < 0.82f && baseType == WoundType.Gunshot && _random.Prob(0.2f))
                return (WoundType.Puncture, residualAmount * 0.9f);
            return (baseType, residualAmount);
        }

        if (penetration >= 0.40f)
        {
            var roll = _random.NextFloat();
            if (roll < 0.50f)
                return (WoundType.Gunshot, residualAmount * 0.9f);
            if (roll < 0.82f)
                return (WoundType.Puncture, residualAmount * 0.75f);
            return (WoundType.Bruise, Math.Max(MinWoundDamage, originalAmount * 0.32f));
        }

        if (penetration >= 0.20f)
        {
            if (_random.Prob(0.28f))
                return (WoundType.Puncture, residualAmount * 0.55f);
            return (WoundType.Bruise, Math.Max(MinWoundDamage, originalAmount * 0.35f));
        }

        return (WoundType.Bruise, Math.Max(MinWoundDamage, originalAmount * 0.22f));
    }

    private BodyPartSlot PickFootSlot()
    {
        var slots = new[] { BodyPartSlot.LeftFoot, BodyPartSlot.RightFoot, BodyPartSlot.LeftShin, BodyPartSlot.RightShin };
        return _random.Pick(slots);
    }

    private BodyPartSlot SelectSlot(AdvancedHealthComponent health, EntityUid? source)
    {
        if (source != null &&
            TryComp<AimTargetComponent>(source, out var aim) &&
            aim.AdvancedTargetingEnabled &&
            aim.CurrentTarget != BodyPartTarget.Auto)
        {
            var desired = aim.CurrentTarget.ToSlot();
            var penalty = AimTargetSystem.GetDefaultPenalty(desired);
            var roll = _random.NextFloat(0, 100) + penalty;
            if (roll >= 25)
                return desired;
            if (roll >= 5)
                return _random.Pick(GetAdjacent(desired));
        }

        var total = health.BodyPartHitWeights.Values.Sum();
        var selection = _random.NextFloat(0, total);
        foreach (var (slot, weight) in health.BodyPartHitWeights)
        {
            selection -= weight;
            if (selection <= 0)
                return slot;
        }

        return BodyPartSlot.Chest;
    }

    private void CreateWound(Entity<AdvancedHealthComponent> ent, BodyPartSlot slot, WoundType type,
        WoundSeverity severity, float amount)
    {
        slot = slot switch
        {
            BodyPartSlot.Neck => BodyPartSlot.Chest,
            BodyPartSlot.Pelvis => BodyPartSlot.Abdomen,
            _ => slot,
        };

        var part = ent.Comp.BodyParts[slot];
        var severityScale = 1f + (int) severity * 0.45f;
        var isPenetrating = type is WoundType.Puncture or WoundType.Gunshot or WoundType.Shrapnel;
        var external = ent.Comp.HasBlood
            ? amount * ent.Comp.WoundBleedingRates.GetValueOrDefault(type) * severityScale * ent.Comp.BleedingModifier
            : 0f;
        var internalBleeding = ent.Comp.HasBlood && slot.IsCore() && isPenetrating
            ? amount * 0.035f * severityScale * ent.Comp.BleedingModifier
            : 0f;
        var trauma = GetTrauma(slot, type, amount, severity) *
                     ent.Comp.WoundTraumaValues.GetValueOrDefault(type, 1f) *
                     ent.Comp.TraumaModifier;

        part.SkinIntegrity = Math.Max(0, part.SkinIntegrity - amount * (type == WoundType.Burn ? 1.2f : 0.8f));
        part.MuscleIntegrity = Math.Max(0, part.MuscleIntegrity - amount * (isPenetrating ? 1f : 0.55f));
        part.VesselIntegrity = Math.Max(0, part.VesselIntegrity - (external + internalBleeding) * 3f);
        if (ent.Comp.CanFracture)
            part.BoneIntegrity = Math.Max(0, part.BoneIntegrity - amount *
                (type is WoundType.Fracture or WoundType.Gunshot ? 0.75f : 0.15f));
        part.NerveIntegrity = Math.Max(0, part.NerveIntegrity - amount * (isPenetrating ? 0.35f : 0.1f));
        if (slot.IsCore())
            part.OrganIntegrity = Math.Max(0, part.OrganIntegrity - amount * (isPenetrating ? 0.75f : 0.25f));

        var wound = new Wound
        {
            Type = type,
            Severity = severity,
            BodyPart = slot,
            ExternalBleedingRate = external,
            InternalBleedingRate = internalBleeding,
            Pain = ent.Comp.HasPain
                ? amount * ent.Comp.WoundPainValues.GetValueOrDefault(type, 0.7f) * severityScale * ent.Comp.PainModifier
                : 0f,
            Trauma = trauma,
            InfectionRisk = isPenetrating ? 0.15f * severityScale : 0.05f,
            IsDirty = type is WoundType.Gunshot or WoundType.Shrapnel,
            HasForeignBody = type is WoundType.Gunshot or WoundType.Shrapnel,
        };

        if (wound.HasForeignBody)
            part.ForeignBodyCount = (byte) Math.Min(99, part.ForeignBodyCount + 1);

        var existing = part.Wounds.FirstOrDefault(w => w.Type == type && w.Severity == severity);
        if (existing != null)
        {
            MergeWound(existing, wound);
        }
        else if (part.Wounds.Count >= MaxWoundsPerPart)
        {
            var target = part.Wounds.FirstOrDefault(w => w.Type == type) ?? part.Wounds[0];
            MergeWound(target, wound);
        }
        else
        {
            part.Wounds.Add(wound);
        }

        part.IsBleeding = part.Wounds.Any(w => w.ExternalBleedingRate + w.InternalBleedingRate > 0.01f);
        part.IsDestroyed = part.MuscleIntegrity <= 0 || slot.IsCore() && part.OrganIntegrity <= 0;
        ent.Comp.TraumaLoad += trauma;
        ent.Comp.Pain = Math.Min(100, ent.Comp.Pain + wound.Pain * 0.2f);
        Dirty(ent);
    }

    private static void MergeWound(Wound into, Wound from)
    {
        into.StackCount = (byte) Math.Min(99, into.StackCount + from.StackCount);
        into.ExternalBleedingRate += from.ExternalBleedingRate;
        into.InternalBleedingRate += from.InternalBleedingRate;
        into.Pain += from.Pain;
        into.Trauma += from.Trauma;
        into.Severity = (WoundSeverity) Math.Max((int) into.Severity, (int) from.Severity);
        into.HasForeignBody |= from.HasForeignBody;
        into.IsDirty |= from.IsDirty;
        into.InfectionRisk = Math.Max(into.InfectionRisk, from.InfectionRisk);
    }

    private static float GetTrauma(BodyPartSlot slot, WoundType type, float amount, WoundSeverity severity)
    {
        if (type != WoundType.Gunshot)
            return Math.Max(1, amount * (0.18f + (int) severity * 0.07f));

        return slot switch
        {
            BodyPartSlot.Head => 42, BodyPartSlot.Neck => 36, BodyPartSlot.Chest => 22,
            BodyPartSlot.Abdomen => 17, BodyPartSlot.LeftUpperArm or BodyPartSlot.RightUpperArm
                or BodyPartSlot.LeftThigh or BodyPartSlot.RightThigh => 10,
            BodyPartSlot.LeftForearm or BodyPartSlot.RightForearm or BodyPartSlot.LeftShin
                or BodyPartSlot.RightShin => 7,
            BodyPartSlot.LeftHand or BodyPartSlot.RightHand or BodyPartSlot.LeftFoot
                or BodyPartSlot.RightFoot => 5,
            _ => 12,
        };
    }

    /// <summary>True for damage types AdvancedHealth already models, which must not become wounds.</summary>
    private static bool IsPhysiologicalDamage(string? id)
    {
        if (id == null)
            return false;

        return id.Contains("Bloodloss", StringComparison.OrdinalIgnoreCase)
            || id.Contains("Asphyxiation", StringComparison.OrdinalIgnoreCase)
            || id.Contains("Airloss", StringComparison.OrdinalIgnoreCase)
            || id.Contains("Poison", StringComparison.OrdinalIgnoreCase)
            || id.Contains("Radiation", StringComparison.OrdinalIgnoreCase)
            || id.Contains("Cellular", StringComparison.OrdinalIgnoreCase);
    }

    private static WoundType GetWoundType(string? damageTypeId)
    {
        if (damageTypeId == null)
            return WoundType.OrganDamage;

        if (damageTypeId.Contains("Piercing", StringComparison.OrdinalIgnoreCase)) return WoundType.Gunshot;
        if (damageTypeId.Contains("Slash", StringComparison.OrdinalIgnoreCase)) return WoundType.Cut;
        if (damageTypeId.Contains("Heat", StringComparison.OrdinalIgnoreCase) ||
            damageTypeId.Contains("Cold", StringComparison.OrdinalIgnoreCase)) return WoundType.Burn;
        if (damageTypeId.Contains("Blunt", StringComparison.OrdinalIgnoreCase)) return WoundType.Bruise;
        return WoundType.OrganDamage;
    }

    public static BodyPartSlot[] GetAdjacent(BodyPartSlot slot) => slot switch
    {
        BodyPartSlot.Head => [BodyPartSlot.Neck, BodyPartSlot.Chest],
        BodyPartSlot.Neck => [BodyPartSlot.Head, BodyPartSlot.Chest],
        BodyPartSlot.Chest => [BodyPartSlot.Neck, BodyPartSlot.Abdomen, BodyPartSlot.LeftUpperArm, BodyPartSlot.RightUpperArm],
        BodyPartSlot.Abdomen => [BodyPartSlot.Chest, BodyPartSlot.Pelvis],
        BodyPartSlot.Pelvis => [BodyPartSlot.Abdomen, BodyPartSlot.LeftThigh, BodyPartSlot.RightThigh],
        BodyPartSlot.LeftUpperArm => [BodyPartSlot.Chest, BodyPartSlot.LeftForearm],
        BodyPartSlot.LeftForearm => [BodyPartSlot.LeftUpperArm, BodyPartSlot.LeftHand],
        BodyPartSlot.LeftHand => [BodyPartSlot.LeftForearm],
        BodyPartSlot.RightUpperArm => [BodyPartSlot.Chest, BodyPartSlot.RightForearm],
        BodyPartSlot.RightForearm => [BodyPartSlot.RightUpperArm, BodyPartSlot.RightHand],
        BodyPartSlot.RightHand => [BodyPartSlot.RightForearm],
        BodyPartSlot.LeftThigh => [BodyPartSlot.Pelvis, BodyPartSlot.LeftShin],
        BodyPartSlot.LeftShin => [BodyPartSlot.LeftThigh, BodyPartSlot.LeftFoot],
        BodyPartSlot.LeftFoot => [BodyPartSlot.LeftShin],
        BodyPartSlot.RightThigh => [BodyPartSlot.Pelvis, BodyPartSlot.RightShin],
        BodyPartSlot.RightShin => [BodyPartSlot.RightThigh, BodyPartSlot.RightFoot],
        BodyPartSlot.RightFoot => [BodyPartSlot.RightShin],
        _ => [BodyPartSlot.Chest],
    };
}

/// <summary>
/// Unified periodic update. Skips healthy mobs and only dirties when state changes.
/// </summary>
public sealed class AdvancedHealthTickSystem : EntitySystem
{
    [Dependency] private readonly SharedBloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    private const float TickInterval = 1f;
    private float _accumulator;

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < TickInterval)
            return;

        var elapsed = _accumulator;
        _accumulator = 0;

        var query = EntityQueryEnumerator<AdvancedHealthComponent>();
        while (query.MoveNext(out var uid, out var health))
        {
            var instant = AdvancedHealthSystem.ComputeInstantBadness(health);
            var prevBadness = health.SmoothedConditionBadness;
            health.SmoothedConditionBadness = MathHelper.Lerp(prevBadness, instant, 0.05f);
            var badnessDirty = Math.Abs(health.SmoothedConditionBadness - prevBadness) > 0.008f;

            if (!NeedsSimulation(health))
            {
                if (badnessDirty)
                    Dirty(uid, health);
                continue;
            }

            var dirty = badnessDirty;
            var loss = 0f;

            foreach (var part in health.BodyParts.Values)
            {
                var partLoss = 0f;
                foreach (var wound in part.Wounds)
                {
                    // Bandaging now reduces ExternalBleedingRate directly (incrementally), so only the
                    // tourniquet applies a live multiplier here (it can be removed to restore flow).
                    var external = wound.ExternalBleedingRate;
                    if (part.HasTourniquet && part.Slot.IsLimb())
                        external *= 1f - health.TourniquetEffectiveness;
                    partLoss += external + wound.InternalBleedingRate;
                }

                var bleeding = partLoss > 0.01f;
                if (part.IsBleeding != bleeding)
                {
                    part.IsBleeding = bleeding;
                    dirty = true;
                }

                loss += partLoss;
            }

            if (loss > 0f && health.HasBlood)
            {
                // The vanilla bloodstream is kept in step separately, in SyncBloodstream.
                var newBlood = Math.Max(0, health.BloodVolume - loss * elapsed);
                if (Math.Abs(newBlood - health.BloodVolume) > 0.01f)
                {
                    health.BloodVolume = newBlood;
                    dirty = true;
                }
            }

            if (health.HasPain)
            {
                var rawPain = health.BodyParts.Values
                    .SelectMany(x => x.Wounds)
                    .Sum(wound => wound.Pain * (wound.IsBandaged ? 0.85f : 1f));
                var newPain = Math.Clamp(rawPain, 0, 100);
                if (Math.Abs(newPain - health.Pain) > 0.05f)
                {
                    health.Pain = newPain;
                    dirty = true;
                }
            }

            var bloodShock = health.HasBlood
                ? (1f - health.BloodVolume / Math.Max(1, health.MaxBloodVolume)) * 85f
                : 0f;
            var newShock = Math.Clamp(health.Pain * 0.45f + bloodShock + health.TraumaLoad * 0.18f, 0, 100);

            var agonal = health.TraumaLoad >= health.LethalTraumaThreshold;
            if (agonal)
                newShock = Math.Max(newShock, 85f);

            if (Math.Abs(newShock - health.Shock) > 0.05f)
            {
                health.Shock = newShock;
                dirty = true;
            }

            if (health.IsInAgonalState != agonal)
            {
                health.IsInAgonalState = agonal;
                dirty = true;
            }

            var bloodRatio = health.HasBlood ? health.BloodVolume / Math.Max(1, health.MaxBloodVolume) : 1f;
            var chestIntegrity = health.BodyParts[BodyPartSlot.Chest].OrganIntegrity / 100f;
            var neckIntegrity = health.BodyParts[BodyPartSlot.Neck].OrganIntegrity / 100f;

            // --- Cardiovascular loop: sympathetic drive → heart rate & tone → pressure → perfusion ---

            // Adrenaline / inotropic drive from drugs decays over time.
            if (health.AdrenalineDrive > 0f)
                health.AdrenalineDrive = Math.Max(0f, health.AdrenalineDrive - elapsed * 0.15f);

            // Marrow slowly regrows oxygen-carrying red cells.
            if (health.OxygenCarryingCapacity < 1f)
            {
                health.OxygenCarryingCapacity =
                    Math.Min(1f, health.OxygenCarryingCapacity + health.OxygenCarryingRegen * elapsed);
                dirty = true;
            }

            var hypovolemia = Math.Clamp(1f - bloodRatio, 0f, 1f);
            var painDrive = health.HasPain ? health.Pain / 100f : 0f;
            var hypoxiaDrive = Math.Clamp((90f - health.Oxygenation) / 90f, 0f, 1f);
            // Compensatory drive drives tachycardia and vasoconstriction to defend pressure.
            var sympathetic = hypovolemia * 1.1f + painDrive * 0.5f + hypoxiaDrive * 0.7f + health.AdrenalineDrive;
            // Decompensation: only past ~65% volume loss (or severe hypoxia) the heart can no longer keep up.
            var decompensation = Math.Clamp((0.35f - bloodRatio) / 0.35f, 0f, 1f);
            if (health.Oxygenation < 25f)
                decompensation = Math.Max(decompensation, (25f - health.Oxygenation) / 25f);

            var heartRateTarget = health.IsHeartStopped
                ? 0f
                : Math.Clamp((health.RestingHeartRate + sympathetic * 55f) * (1f - decompensation), 0f, 220f);
            var newHeartRate = MoveTowards(health.HeartRate, heartRateTarget, elapsed * 35f);
            if (Math.Abs(newHeartRate - health.HeartRate) > 0.1f)
            {
                health.HeartRate = newHeartRate;
                dirty = true;
            }

            var toneTarget = Math.Clamp((1f + sympathetic * 0.3f) * (1f - decompensation * 0.85f), 0.15f, 1.9f);
            health.VascularTone = MoveTowards(health.VascularTone, toneTarget, elapsed * 0.8f);

            // Cardiac output ≈ preload (volume) × rate; bradycardia crushes output, CPR substitutes in arrest.
            var cardiacFactor = Math.Clamp(health.HeartRate / Math.Max(1f, health.RestingHeartRate), 0f, 1.5f);
            if (health.HeartRate < 40f)
                cardiacFactor *= Math.Clamp(health.HeartRate / 40f, 0f, 1f);
            var mapTarget = health.IsHeartStopped
                ? health.BaselinePressure * 0.28f * health.CprSupport
                : Math.Clamp(health.BaselinePressure * bloodRatio * cardiacFactor * health.VascularTone, 0f, 150f);
            var newMap = MoveTowards(health.MeanArterialPressure, mapTarget, elapsed * 30f);
            if (Math.Abs(newMap - health.MeanArterialPressure) > 0.1f)
            {
                health.MeanArterialPressure = newMap;
                dirty = true;
            }

            var perfusion = Math.Clamp(health.MeanArterialPressure / Math.Max(1f, health.AdequatePressure), 0f, 1f);
            if (Math.Abs(perfusion - health.Perfusion) > 0.002f)
            {
                health.Perfusion = perfusion;
                dirty = true;
            }

            // Arrest: shock/trauma triggers plus bradyasystolic arrest from a failing rate.
            if (!health.IsHeartStopped &&
                (health.Shock >= health.ShockHeartStopThreshold ||
                 health.TraumaLoad >= health.InstantDeathTraumaThreshold ||
                 health.HeartRate <= health.BradyArrestHeartRate))
            {
                health.IsHeartStopped = true;
                dirty = true;
            }

            // Oxygenation: gas exchange (lungs/airway) can only reach tissue via perfusion.
            var lungCapacity = Math.Min(chestIntegrity, neckIntegrity) / Math.Max(0.1f, health.OxygenationModifier);
            var deliverable = Math.Clamp(health.Perfusion * 1.3f, 0f, 1f);
            // Oxygen delivery is also capped by how much of the blood still carries oxygen.
            var carrying = Math.Clamp(health.OxygenCarryingCapacity, 0f, 1f);
            // Low blood volume directly starves oxygen delivery: full above ~65%, gone near 15%.
            var volumeFactor = health.HasBlood
                ? Math.Clamp((bloodRatio - 0.15f) / 0.5f, 0f, 1f)
                : 1f;
            var oxygenTarget = health.NeedsOxygen
                ? Math.Clamp(lungCapacity * Math.Min(deliverable, volumeFactor) * carrying * 100f, 0f, 100f)
                : 100f;
            var newOxygen = MoveTowards(health.Oxygenation, oxygenTarget, elapsed * 4f);
            if (Math.Abs(newOxygen - health.Oxygenation) > 0.05f)
            {
                health.Oxygenation = newOxygen;
                dirty = true;
            }

            var belowLethalOxygen = health.NeedsOxygen && health.Oxygenation <= health.OxygenDeathThreshold;
            var newTimeBelow = belowLethalOxygen ? health.TimeBelowLethalOxygen + elapsed : 0f;
            if (Math.Abs(newTimeBelow - health.TimeBelowLethalOxygen) > 0.01f)
            {
                health.TimeBelowLethalOxygen = newTimeBelow;
                dirty = true;
            }

            // Consciousness from cerebral perfusion (pressure × oxygen), minus pain/trauma insult.
            var cerebral = health.Perfusion * (health.Oxygenation / 100f);
            var consciousnessTarget = Math.Clamp(cerebral * 100f - health.Shock * 0.2f -
                Math.Max(0, health.TraumaLoad - health.CriticalTraumaThreshold), 0, 100);
            var newConsciousness = MoveTowards(health.Consciousness, consciousnessTarget, elapsed * 8f);
            if (Math.Abs(newConsciousness - health.Consciousness) > 0.05f)
            {
                health.Consciousness = newConsciousness;
                dirty = true;
            }

            // Hysteresis: pass out at <=20, but must recover past 26 to wake — stops flicker at the edge.
            var wakeThreshold = health.IsUnconscious ? 26f : 20f;
            var unconscious = health.Consciousness <= wakeThreshold || health.Shock >= health.ShockUnconsciousThreshold;
            if (health.IsUnconscious != unconscious)
            {
                var wasUnconscious = health.IsUnconscious;
                health.IsUnconscious = unconscious;
                dirty = true;

                if (!wasUnconscious && unconscious)
                    NotifyUnconscious(uid, health);
                else if (wasUnconscious && !unconscious)
                    _popup.PopupEntity(Loc.GetString("advanced-health-consciousness-returned"), uid, uid);
            }

            if (health.IsHeartStopped)
            {
                health.TimeSinceHeartStopped += elapsed;
                dirty = true;
            }
            else if (health.TimeSinceHeartStopped > 0f)
            {
                health.TimeSinceHeartStopped = 0f;
                dirty = true;
            }

            // Immune defense & wound infection. Open/dirty wounds raise the infection load faster
            // the weaker the immune system is; a strong immune system clears it and recovers.
            var openInfection = 0f;
            foreach (var part in health.BodyParts.Values)
                foreach (var wound in part.Wounds)
                    if (!wound.IsSutured)
                        openInfection += wound.InfectionRisk * (wound.IsDirty ? 1.6f : 1f);

            var resist = Math.Clamp(health.ImmuneDefense / 100f, 0f, 1f);
            var infectionTarget = Math.Clamp(openInfection * 45f * (1.2f - resist), 0f, 100f);
            var newInfection = MoveTowards(health.InfectionLoad, infectionTarget, elapsed * (0.4f + resist * 0.8f));
            if (Math.Abs(newInfection - health.InfectionLoad) > 0.05f)
            {
                health.InfectionLoad = newInfection;
                dirty = true;
            }

            var immuneDelta = health.InfectionLoad > 1f
                ? -health.InfectionLoad * 0.01f * elapsed
                : health.ImmuneRegen * elapsed;
            var newImmune = Math.Clamp(health.ImmuneDefense + immuneDelta, 0f, 100f);
            if (Math.Abs(newImmune - health.ImmuneDefense) > 0.02f)
            {
                health.ImmuneDefense = newImmune;
                dirty = true;
            }

            // AdvancedHealth is authoritative: drive the mob state and mirror the vanilla bloodstream.
            UpdateMobState(uid, health, bloodRatio);
            SyncBloodstream(uid, health, bloodRatio);

            if (dirty)
                Dirty(uid, health);
        }
    }

    /// <summary>Crit at/under 50% blood (or unconscious/hypoxic/arrested); death on lethal conditions.</summary>
    private void UpdateMobState(EntityUid uid, AdvancedHealthComponent health, float bloodRatio)
    {
        if (!TryComp<MobStateComponent>(uid, out var mobState))
            return;

        var dead = (health.IsHeartStopped && health.TimeSinceHeartStopped >= health.HeartStoppedDeathTime)
            || (health.HasBlood && health.BloodVolume <= health.BloodDeathThreshold)
            || health.TraumaLoad >= health.InstantDeathTraumaThreshold
            || (health.NeedsOxygen && health.TimeBelowLethalOxygen >= 60f);

        // Hysteresis: once critical, recovery must clear each threshold by a margin before standing
        // back up. Without this, a value resting right on a boundary flips the state every tick and
        // the pawn rapidly falls/stands. Entry uses the base threshold; exit uses a raised one.
        var inCrit = mobState.CurrentState == MobState.Critical;
        var bloodCrit = health.HasBlood && bloodRatio <= (inCrit ? 0.56f : 0.5f);
        var consciousCrit = health.Consciousness <= (inCrit ? 32f : 25f);
        var oxyCrit = health.NeedsOxygen
            && health.Oxygenation <= health.CriticalOxygenation + (inCrit ? 8f : 0f);

        var crit = bloodCrit
            || health.IsUnconscious
            || consciousCrit
            || health.IsHeartStopped
            || oxyCrit;

        var desired = dead ? MobState.Dead : crit ? MobState.Critical : MobState.Alive;

        // Never auto-revive a corpse here — leave that to defibrillation/revival.
        if (mobState.CurrentState == MobState.Dead && desired != MobState.Dead)
            return;

        if (mobState.CurrentState != desired)
            _mobState.ChangeMobState(uid, desired, mobState);
    }

    /// <summary>Keeps the vanilla bloodstream level in step with AdvancedHealth so all readers agree.</summary>
    private void SyncBloodstream(EntityUid uid, AdvancedHealthComponent health, float bloodRatio)
    {
        if (!health.HasBlood || !TryComp<BloodstreamComponent>(uid, out var bloodstream))
            return;

        // AdvancedHealth owns bleeding — cancel the vanilla bleed so it can't drain blood in parallel.
        if (bloodstream.BleedAmount > 0f)
            _bloodstream.TryModifyBleedAmount((uid, bloodstream), -bloodstream.BleedAmount);

        var current = _bloodstream.GetBloodLevel((uid, bloodstream));
        var delta = Math.Clamp(bloodRatio, 0f, 1f) - current;
        if (Math.Abs(delta) < 0.01f)
            return;

        var reference = (float) bloodstream.BloodReferenceSolution.Volume;
        if (reference <= 0f)
            return;

        _bloodstream.TryModifyBloodLevel((uid, bloodstream), FixedPoint2.New(reference * delta));
    }

    private static bool NeedsSimulation(AdvancedHealthComponent health)
    {
        if (health.TraumaLoad > 0.1f || health.Pain > 0.5f || health.Shock > 0.5f)
            return true;
        if (health.IsHeartStopped || health.IsUnconscious || health.IsInAgonalState)
            return true;
        if (health.HasBlood && health.BloodVolume < health.MaxBloodVolume - 1f)
            return true;
        if (health.Oxygenation < 99.5f || health.Consciousness < 99.5f)
            return true;
        // Keep ticking until the cardiovascular loop settles back to rest.
        if (Math.Abs(health.HeartRate - health.RestingHeartRate) > 1f)
            return true;
        if (health.Perfusion < 0.995f || health.MeanArterialPressure < health.BaselinePressure - 1f)
            return true;
        if (health.AdrenalineDrive > 0f || health.CprSupport > 0f)
            return true;
        if (health.InfectionLoad > 0.5f || health.ImmuneDefense < 99.5f)
            return true;

        foreach (var part in health.BodyParts.Values)
        {
            if (part.Wounds.Count > 0 || part.IsBleeding)
                return true;
        }

        return false;
    }

    private static float MoveTowards(float current, float target, float delta)
        => current < target ? Math.Min(current + delta, target) : Math.Max(current - delta, target);

    private void NotifyUnconscious(EntityUid uid, AdvancedHealthComponent health)
    {
        var consciousness = (int) MathF.Round(health.Consciousness);
        var shock = (int) MathF.Round(health.Shock);
        var threshold = (int) MathF.Round(health.ShockUnconsciousThreshold);

        var msgKey = health.Consciousness <= 20 && health.Shock >= health.ShockUnconsciousThreshold
            ? "advanced-health-unconscious-both"
            : health.Shock >= health.ShockUnconsciousThreshold
                ? "advanced-health-unconscious-shock"
                : "advanced-health-unconscious-consciousness";

        _popup.PopupEntity(
            Loc.GetString(msgKey, ("consciousness", consciousness), ("shock", shock), ("threshold", threshold)),
            uid,
            uid,
            PopupType.LargeCaution);
    }
}

public sealed class AimTargetSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
    {
        SubscribeNetworkEvent<AdvancedHealthSetAimTargetEvent>(OnSetAimTarget);
        SubscribeLocalEvent<AimTargetComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private const float AimSwitchCooldown = 1f;

    private void OnSetAimTarget(AdvancedHealthSetAimTargetEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user || !TryComp<AimTargetComponent>(user, out var aim))
            return;

        var now = (float) _timing.CurTime.TotalSeconds;
        if (aim.LastTargetChangeTime > 0f && now - aim.LastTargetChangeTime < AimSwitchCooldown)
        {
            _popup.PopupEntity(Loc.GetString("advanced-health-aim-too-fast"), user, user);
            return;
        }

        aim.CurrentTarget = ev.Target;
        aim.AdvancedTargetingEnabled = ev.Target != BodyPartTarget.Auto;
        aim.LastTargetChangeTime = now;

        Dirty(user, aim);
        _movement.RefreshMovementSpeedModifiers(user);

        _popup.PopupEntity(Loc.GetString("advanced-health-target-selected", ("part",
            Loc.GetString($"advanced-health-part-{ev.Target.ToString().ToLowerInvariant()}"))), user, user);
    }

    private void OnRefreshSpeed(EntityUid uid, AimTargetComponent aim, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!aim.AdvancedTargetingEnabled)
            return;

        var slot = aim.CurrentTarget.ToSlot();
        if (slot is BodyPartSlot.Head or BodyPartSlot.Neck)
            args.ModifySpeed(aim.VitalAimSlowModifier, aim.VitalAimSlowModifier);
    }

    public static int GetDefaultPenalty(BodyPartSlot slot) => slot switch
    {
        BodyPartSlot.Chest => 0, BodyPartSlot.Abdomen => -12, BodyPartSlot.Pelvis => -18,
        BodyPartSlot.Head => -48, BodyPartSlot.Neck => -62,
        BodyPartSlot.LeftUpperArm or BodyPartSlot.RightUpperArm => -28,
        BodyPartSlot.LeftForearm or BodyPartSlot.RightForearm => -38,
        BodyPartSlot.LeftHand or BodyPartSlot.RightHand => -55,
        BodyPartSlot.LeftThigh or BodyPartSlot.RightThigh => -22,
        BodyPartSlot.LeftShin or BodyPartSlot.RightShin => -34,
        _ => -50,
    };
}

public sealed class AdvancedTreatmentSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private static readonly ProtoId<TagPrototype> TourniquetTag = "Tourniquet";
    private static readonly ProtoId<TagPrototype> OintmentTag = "Ointment";

    /// <summary>External bleeding (ml/s) removed per 1% of bandage durability spent (= 0.01 L/min).</summary>
    private const float BandageBleedPerSegmentMls = AdvancedBandageRollComponent.BleedPerPercent * 1000f / 60f;

    public override void Initialize()
    {
        SubscribeLocalEvent<AdvancedTreatmentComponent, AfterInteractEvent>(OnInteract);
        SubscribeLocalEvent<AdvancedBandageRollComponent, ExaminedEvent>(OnBandageExamined);
        SubscribeLocalEvent<AdvancedTreatmentComponent, AdvancedTreatmentDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<AdvancedHealthComponent, AdvancedTargetedTreatmentDoAfterEvent>(OnTargetedDoAfter);
        SubscribeNetworkEvent<AdvancedHealthTreatmentCompleteEvent>(OnTreatmentComplete);
        SubscribeNetworkEvent<AdvancedHealthTransfusionEvent>(OnTransfusion);
    }

    private void OnTransfusion(AdvancedHealthTransfusionEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user)
            return;

        var target = GetEntity(ev.Target);
        var pack = GetEntity(ev.Pack);
        if (!Exists(target) || !Exists(pack) ||
            !TryComp<AdvancedHealthComponent>(target, out var health) ||
            !TryComp<BloodProductComponent>(pack, out var product))
            return;

        if (user != target && !_interaction.InRangeUnobstructed(user, target))
        {
            _popup.PopupEntity(Loc.GetString("advanced-health-treatment-too-far"), target, user);
            return;
        }

        if (!health.HasBlood || product.Charges <= 0)
            return;

        ApplyTransfusion(target, health, product);

        product.Charges--;
        Dirty(pack, product);
        if (product.Charges <= 0)
            QueueDel(pack);
        Dirty(target, health);
    }

    private void ApplyTransfusion(EntityUid target, AdvancedHealthComponent health, BloodProductComponent product)
    {
        var oldVol = health.BloodVolume;
        var maxVol = health.MaxBloodVolume;

        switch (product.ProductType)
        {
            case BloodProductType.WholeBlood:
            case BloodProductType.PackedCells:
            {
                var compatibility = GetCompatibility(health.BloodType, product.BloodType);
                if (compatibility == null)
                {
                    ApplyIncompatibleTransfusion(health);
                    health.BloodVolume = Math.Min(maxVol, oldVol + product.Volume * 0.5f);
                    _popup.PopupEntity(Loc.GetString(health.IncompatibleTransfusionPopup),
                        target, target, PopupType.LargeCaution);
                    return;
                }

                var donorCarry = product.OxygenCarryFactor * compatibility.Value;
                if (product.ProductType == BloodProductType.PackedCells)
                    donorCarry *= 1.2f;
                var newVol = Math.Min(maxVol, oldVol + product.Volume);
                var added = newVol - oldVol;
                if (added > 0f)
                    health.OxygenCarryingCapacity = Math.Clamp(
                        (health.OxygenCarryingCapacity * oldVol + donorCarry * added) / newVol, 0f, 1f);
                health.BloodVolume = newVol;
                _popup.PopupEntity(Loc.GetString("advanced-health-transfusion-ok"), target, target);
                break;
            }
            case BloodProductType.Ringers:
            case BloodProductType.Saline:
            {
                // Crystalloid restores volume but dilutes the oxygen-carrying fraction.
                var newVol = Math.Min(maxVol, oldVol + product.Volume);
                if (newVol > 0f)
                    health.OxygenCarryingCapacity =
                        Math.Clamp(health.OxygenCarryingCapacity * oldVol / newVol, 0f, 1f);
                health.BloodVolume = newVol;
                _popup.PopupEntity(Loc.GetString("advanced-health-transfusion-fluid"), target, target);
                break;
            }
        }
    }

    private float? GetCompatibility(string recipientType, string donorType)
    {
        if (!_prototypes.TryIndex<AdvancedHealthBloodCompatibilityPrototype>(recipientType, out var compatibility))
            return recipientType == donorType ? 1f : null;

        if (compatibility.CompatibleDonors.Contains(donorType))
            return 1f;

        if (compatibility.EmergencyDonors.Contains(donorType))
            return compatibility.EmergencyOxygenCarryFactor;

        return null;
    }

    private static void ApplyIncompatibleTransfusion(AdvancedHealthComponent health)
    {
        health.OxygenCarryingCapacity = Math.Max(0.1f, health.OxygenCarryingCapacity - 0.3f);
        health.Shock = Math.Clamp(health.Shock + 25f, 0f, 100f);
        if (health.HasPain)
            health.Pain = Math.Clamp(health.Pain + 20f, 0f, 100f);
        health.TraumaLoad += 15f;
        health.Oxygenation = Math.Max(0f, health.Oxygenation - 8f);
        health.ImmuneDefense = Math.Max(0f, health.ImmuneDefense - 10f);
    }

    private void OnBandageExamined(Entity<AdvancedBandageRollComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("advanced-health-bandage-durability",
            ("percent", (int) MathF.Round(ent.Comp.Durability))));
    }

    private void OnInteract(Entity<AdvancedTreatmentComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target ||
            !HasComp<AdvancedHealthComponent>(target))
            return;

        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User,
            ent.Comp.Delay, new AdvancedTreatmentDoAfterEvent(), ent, target: target, used: ent)
        {
            NeedHand = true,
            BreakOnMove = true,
        });
    }

    private void OnDoAfter(Entity<AdvancedTreatmentComponent> ent, ref AdvancedTreatmentDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target ||
            !TryComp<AdvancedHealthComponent>(target, out var health))
            return;

        var slot = SelectTreatmentSlot(args.User, health, ent.Comp.Treatment);
        if (!TryApply(ent, (target, health), slot, args.User))
            return;
        args.Handled = true;
    }

    private void OnTargetedDoAfter(Entity<AdvancedHealthComponent> ent,
        ref AdvancedTargetedTreatmentDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target != ent.Owner)
            return;

        if (args.Used is not { } tool || !Exists(tool) || !MatchesTreatmentItem(tool, args.Treatment))
            return;

        if (TryApplyTreatment(ent.Owner, args.User, tool, args.Slot, args.Treatment, args.Effectiveness))
            args.Handled = true;
    }

    private void OnTreatmentComplete(AdvancedHealthTreatmentCompleteEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user)
            return;

        if (ev.Quality < 0.35f)
        {
            _popup.PopupEntity(Loc.GetString("advanced-health-minigame-fail-generic"), user, user);
            return;
        }

        var target = GetEntity(ev.Target);
        if (!Exists(target) || !TryComp<AdvancedHealthComponent>(target, out var health))
            return;

        if (user != target && !_interaction.InRangeUnobstructed(user, target))
        {
            _popup.PopupEntity(Loc.GetString("advanced-health-treatment-too-far"), target, user);
            return;
        }

        if (ev.Treatment == AdvancedTreatmentType.Tourniquet && !ev.Slot.IsLimb())
        {
            _popup.PopupEntity(Loc.GetString("advanced-health-treatment-tourniquet-invalid"), target, user);
            return;
        }

        EntityUid? tool = ev.Tool != null ? GetEntity(ev.Tool.Value) : null;
        if (tool != null && !Exists(tool.Value))
        {
            _popup.PopupEntity(Loc.GetString("advanced-health-treatment-no-item"), target, user);
            return;
        }

        if (tool != null)
        {
            if (!_hands.IsHolding(user, tool.Value) || !MatchesTreatmentItem(tool.Value, ev.Treatment))
            {
                _popup.PopupEntity(Loc.GetString("advanced-health-treatment-no-item"), target, user);
                return;
            }
        }
        else if (ev.Treatment != AdvancedTreatmentType.ForeignBodyRemoval)
        {
            _popup.PopupEntity(Loc.GetString("advanced-health-treatment-no-item"), target, user);
            return;
        }

        if (!health.BodyParts.TryGetValue(ev.Slot, out var part) || !PartNeedsTreatment(part, ev.Treatment))
        {
            _popup.PopupEntity(Loc.GetString("advanced-health-treatment-not-needed"), target, user);
            return;
        }

        // Bandaging is durability-based, not quality-based: apply exactly the number of 1% segments the
        // player wound on in the minigame.
        if (ev.Treatment is AdvancedTreatmentType.Bandage or AdvancedTreatmentType.PressureBandage)
        {
            ApplyBandageSegments(target, user, tool, ev.Slot, part, ev.Treatment, ev.Segments);
            return;
        }

        var baseEffectiveness = tool != null && TryComp<AdvancedTreatmentComponent>(tool.Value, out var advanced)
            ? advanced.Effectiveness
            : ev.Treatment == AdvancedTreatmentType.ForeignBodyRemoval ? 0.55f : 1f;

        var effectiveness = baseEffectiveness * Math.Clamp(ev.Quality, 0.35f, 1f);
        TryApplyTreatment(target, user, tool ?? EntityUid.Invalid, ev.Slot, ev.Treatment, effectiveness);
    }

    /// <summary>
    /// Winds a bandage on: each 1% segment removes 0.01 L/min of external bleeding and spends 1% of the
    /// roll. Clamped to the roll's remaining durability and to the wound's actual bleeding. The roll is
    /// consumed when depleted. A pressure bandage does the same but each segment is 60% more effective.
    /// </summary>
    private bool ApplyBandageSegments(EntityUid target, EntityUid user, EntityUid? tool, BodyPartSlot slot,
        BodyPartState part, AdvancedTreatmentType treatment, int requestedSegments)
    {
        if (tool is not { } toolUid || !TryComp<AdvancedBandageRollComponent>(toolUid, out var roll))
        {
            _popup.PopupEntity(Loc.GetString("advanced-health-treatment-no-item"), target, user);
            return false;
        }

        if (!TryComp<AdvancedHealthComponent>(target, out var health))
            return false;

        var perSegment = treatment == AdvancedTreatmentType.PressureBandage
            ? BandageBleedPerSegmentMls * 1.6f
            : BandageBleedPerSegmentMls;

        var bleedMls = part.Wounds.Sum(w => w.ExternalBleedingRate);
        var bleedSegs = (int) MathF.Ceiling(bleedMls / perSegment - 0.0001f);
        var available = (int) MathF.Floor(roll.Durability);
        var segments = Math.Clamp(requestedSegments, 0, Math.Min(available, bleedSegs));

        if (segments <= 0)
        {
            _popup.PopupEntity(Loc.GetString("advanced-health-treatment-not-needed"), target, user);
            return false;
        }

        ReduceExternalBleeding(part, segments * perSegment);
        part.IsBandaged = true;
        foreach (var wound in part.Wounds)
            wound.IsBandaged = true;

        roll.Durability = Math.Max(0f, roll.Durability - segments);
        Dirty(toolUid, roll);
        Dirty(target, health);

        _popup.PopupEntity(Loc.GetString("advanced-health-bandage-applied",
            ("part", Loc.GetString($"advanced-health-part-{slot.ToString().ToLowerInvariant()}")),
            ("percent", (int) MathF.Round(roll.Durability))), target, user);

        if (roll.Durability <= 0.5f)
            QueueDel(toolUid);

        return true;
    }

    private static bool PartNeedsTreatment(BodyPartState part, AdvancedTreatmentType treatment)
    {
        var bleeding = part.Wounds.Any(w => w.ExternalBleedingRate + w.InternalBleedingRate > 0.01f);
        return treatment switch
        {
            // Bandaging is incremental now, so it stays available while the part is still bleeding.
            AdvancedTreatmentType.Bandage or AdvancedTreatmentType.PressureBandage => bleeding,
            AdvancedTreatmentType.Tourniquet => bleeding && !part.HasTourniquet,
            AdvancedTreatmentType.Splint => part.Wounds.Any(w => w.Type == WoundType.Fracture) && !part.IsSplinted,
            AdvancedTreatmentType.Hemostatic => bleeding,
            AdvancedTreatmentType.Suture => part.Wounds.Any(w => !w.HasForeignBody && !w.IsSutured),
            AdvancedTreatmentType.ForeignBodyRemoval => part.ForeignBodyCount > 0,
            _ => false,
        };
    }

    private bool TryFindTreatmentTool(EntityUid user, AdvancedTreatmentType treatment, out EntityUid tool,
        out float delay, out float effectiveness)
    {
        tool = default;
        delay = 3f;
        effectiveness = 1f;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (!MatchesTreatmentItem(held, treatment))
                continue;

            tool = held;
            delay = GetTreatmentDelay(held, treatment);
            if (TryComp<AdvancedTreatmentComponent>(held, out var advanced))
                effectiveness = advanced.Effectiveness;
            return true;
        }

        if (treatment == AdvancedTreatmentType.ForeignBodyRemoval)
        {
            tool = EntityUid.Invalid;
            delay = 0f;
            effectiveness = 0.55f;
            return true;
        }

        return false;
    }

    private bool MatchesTreatmentItem(EntityUid item, AdvancedTreatmentType treatment)
    {
        if (TryComp<AdvancedTreatmentComponent>(item, out var advanced))
            return advanced.Treatment == treatment;

        if (MetaData(item).EntityPrototype is not { } proto)
            return false;

        var id = proto.ID;
        return treatment switch
        {
            AdvancedTreatmentType.Bandage or AdvancedTreatmentType.PressureBandage =>
                HasComp<AdvancedBandageRollComponent>(item),
            AdvancedTreatmentType.Tourniquet => _tags.HasTag(item, TourniquetTag),
            AdvancedTreatmentType.Splint => id is "Brutepack" or "Brutepack1",
            AdvancedTreatmentType.Hemostatic => _tags.HasTag(item, OintmentTag),
            AdvancedTreatmentType.Suture => id is "MedicatedSuture" or "MedicatedSuture1" or "BrutepackAdvanced1",
            AdvancedTreatmentType.ForeignBodyRemoval => id is "AdvancedForcepsPack",
            _ => false,
        };
    }

    private float GetTreatmentDelay(EntityUid item, AdvancedTreatmentType treatment)
    {
        if (TryComp<AdvancedTreatmentComponent>(item, out var advanced))
            return advanced.Delay;

        return treatment switch
        {
            AdvancedTreatmentType.Suture => 6f,
            AdvancedTreatmentType.Splint => 5f,
            AdvancedTreatmentType.PressureBandage => 4f,
            AdvancedTreatmentType.Tourniquet => 2f,
            AdvancedTreatmentType.Hemostatic => 2f,
            AdvancedTreatmentType.ForeignBodyRemoval => 7f,
            _ => 3f,
        };
    }

    private bool TryApplyTreatment(EntityUid target, EntityUid user, EntityUid tool, BodyPartSlot slot,
        AdvancedTreatmentType treatment, float effectiveness)
    {
        if (!TryComp<AdvancedHealthComponent>(target, out var health))
            return false;

        if (treatment == AdvancedTreatmentType.Tourniquet && !slot.IsLimb())
        {
            _popup.PopupEntity(Loc.GetString("advanced-health-treatment-tourniquet-invalid"), target, user);
            return false;
        }

        if (!health.BodyParts.TryGetValue(slot, out var part))
            return false;

        // Direct in-world use has no wrap minigame, so wind on as much as the roll and wound allow.
        if (treatment is AdvancedTreatmentType.Bandage or AdvancedTreatmentType.PressureBandage)
            return ApplyBandageSegments(target, user, tool == EntityUid.Invalid ? null : tool, slot, part,
                treatment, int.MaxValue);

        ApplyTreatment(health, part, treatment, effectiveness);

        if (tool != EntityUid.Invalid)
        {
            if (TryComp<StackComponent>(tool, out var stack))
                _stacks.ReduceCount((tool, stack), 1);
            else
                QueueDel(tool);
        }

        Dirty(target, health);

        _popup.PopupEntity(Loc.GetString("advanced-health-treatment-applied", ("part",
            Loc.GetString($"advanced-health-part-{slot.ToString().ToLowerInvariant()}"))), target, user);
        return true;
    }

    private bool TryApply(Entity<AdvancedTreatmentComponent> ent, Entity<AdvancedHealthComponent> target,
        BodyPartSlot slot, EntityUid user)
        => TryApplyTreatment(target, user, ent, slot, ent.Comp.Treatment, ent.Comp.Effectiveness);

    /// <summary>Subtracts a flat amount (ml/s) of external bleeding, spread across the part's wounds.</summary>
    private static void ReduceExternalBleeding(BodyPartState part, float amountMls)
    {
        var remaining = amountMls;
        foreach (var wound in part.Wounds)
        {
            if (remaining <= 0f)
                break;
            var take = Math.Min(wound.ExternalBleedingRate, remaining);
            wound.ExternalBleedingRate -= take;
            remaining -= take;
        }
    }

    private static void ApplyTreatment(AdvancedHealthComponent health, BodyPartState part,
        AdvancedTreatmentType treatment, float effectiveness)
    {
        switch (treatment)
        {
            // Bandage/PressureBandage are handled by ApplyBandageSegments (durability-based), never here.
            case AdvancedTreatmentType.Tourniquet:
                part.HasTourniquet = true;
                break;
            case AdvancedTreatmentType.Splint:
                part.IsSplinted = true;
                foreach (var wound in part.Wounds.Where(x => x.Type == WoundType.Fracture))
                    wound.Pain *= 0.35f;
                break;
            case AdvancedTreatmentType.Hemostatic:
                foreach (var wound in part.Wounds)
                    wound.ExternalBleedingRate *= 1f - health.HemostaticEffectiveness * effectiveness;
                break;
            case AdvancedTreatmentType.Suture:
                part.IsBandaged = true;
                foreach (var wound in part.Wounds)
                {
                    if (wound.HasForeignBody)
                        continue;
                    wound.IsSutured = true;
                    wound.ExternalBleedingRate *= 1f - 0.95f * effectiveness;
                    wound.InfectionRisk *= 0.5f;
                    wound.Pain *= 0.8f;
                }
                break;
            case AdvancedTreatmentType.ForeignBodyRemoval:
            {
                if (part.ForeignBodyCount > 0)
                    part.ForeignBodyCount--;
                foreach (var wound in part.Wounds)
                {
                    if (!wound.HasForeignBody)
                        continue;
                    wound.HasForeignBody = false;
                    wound.IsDirty = false;
                    wound.InfectionRisk *= 0.4f;
                    wound.Pain *= 0.85f;
                    break;
                }
                break;
            }
        }

        part.IsBleeding = part.Wounds.Sum(w => w.ExternalBleedingRate + w.InternalBleedingRate) > 0.01f;
    }

    private BodyPartSlot SelectTreatmentSlot(EntityUid user, AdvancedHealthComponent health,
        AdvancedTreatmentType treatment)
    {
        if (TryComp<AimTargetComponent>(user, out var aim) && aim.CurrentTarget != BodyPartTarget.Auto)
            return aim.CurrentTarget.ToSlot();

        return health.BodyParts.Values
            .Where(x => treatment != AdvancedTreatmentType.Tourniquet || x.Slot.IsLimb())
            .OrderByDescending(x => x.Wounds.Sum(w => w.ExternalBleedingRate + w.InternalBleedingRate + w.Pain * 0.05f))
            .First().Slot;
    }
}
