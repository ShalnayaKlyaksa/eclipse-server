using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>Adapts reagent effects to advanced physiology without healing a parallel HP pool.</summary>
public sealed class SharedAdvancedMedicineSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly SharedMedicalConditionSystem _functions = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public void ApplyGroupDose(Entity<AdvancedHealthComponent> ent, ProtoId<DamageGroupPrototype> group, FixedPoint2 amount)
    {
        if (amount >= FixedPoint2.Zero || !_prototypes.TryIndex(group, out var prototype) || prototype.DamageTypes.Count == 0)
            return;
        var dose = new DamageSpecifier();
        foreach (var type in prototype.DamageTypes)
            dose.DamageDict[type] = FixedPoint2.New(amount.Float() / prototype.DamageTypes.Count);
        ApplyDose(ent, dose, true);
    }

    public void ApplyDose(Entity<AdvancedHealthComponent> ent, DamageSpecifier dose, bool ignoreResistances)
    {
        var positive = DamageSpecifier.GetPositive(dose);
        if (!positive.Empty)
            _damage.TryChangeDamage(ent.Owner, positive, ignoreResistances, interruptsDoAfters: false);

        var repair = 0f;
        foreach (var (type, value) in dose.DamageDict)
        {
            if (value >= FixedPoint2.Zero)
                continue;
            var amount = -value.Float();
            switch (type)
            {
                case "Poison": ent.Comp.ToxinLoad = Math.Max(0f, ent.Comp.ToxinLoad - amount); break;
                case "Radiation": ent.Comp.RadiationLoad = Math.Max(0f, ent.Comp.RadiationLoad - amount); break;
                case "Blunt": case "Slash": case "Piercing": case "Heat": case "Cold": case "Caustic":
                    repair += amount;
                    break;
            }
        }

        // Medication helps viable soft tissue after the wound is protected. It cannot reconnect
        // nerves, set bones, remove foreign bodies, or restore a destroyed limb.
        foreach (var part in ent.Comp.BodyParts.Values)
        {
            if (repair <= 0f)
                break;
            if (part.IsDestroyed || part.ForeignBodyCount > 0)
                continue;
            var protectedWounds = true;
            foreach (var wound in part.Wounds)
            {
                if (wound.HasForeignBody || wound.InternalBleedingRate > 0.001f
                    || wound.Type is WoundType.Cut or WoundType.Puncture or WoundType.Gunshot or WoundType.Shrapnel
                    && !wound.IsBandaged && !wound.IsSutured)
                    protectedWounds = false;
            }
            if (!protectedWounds)
                continue;
            var skin = Math.Min(repair, Math.Max(0f, part.MaxSkinIntegrity - part.SkinIntegrity));
            part.SkinIntegrity += skin;
            repair -= skin;
            var muscle = Math.Min(repair, Math.Max(0f, part.MaxMuscleIntegrity - part.MuscleIntegrity));
            part.MuscleIntegrity += muscle;
            repair -= muscle;
            if (part.SkinFraction >= 0.999f && part.MuscleFraction >= 0.999f)
                part.Wounds.RemoveAll(wound => wound.Type is WoundType.Cut or WoundType.Bruise or WoundType.Burn
                    or WoundType.Puncture or WoundType.Gunshot or WoundType.Shrapnel
                    && wound.ExternalBleedingRate + wound.InternalBleedingRate <= 0.001f && !wound.HasForeignBody);
        }
        Dirty(ent);
        _functions.Refresh((ent.Owner, ent.Comp));
    }
}
