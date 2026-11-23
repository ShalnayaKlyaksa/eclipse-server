using System.Collections.Generic;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Wieldable.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Eclipse.AdvancedHealth;

/// <summary>
/// Foundation disease system: active diseases progress while immunity is too weak to clear them,
/// applying medical effects (pain/shock/immune drain) and a movement-speed debuff. Combat debuffs
/// (melee/accuracy/two-handed) are described on the prototype and can be hooked in later.
/// </summary>
public sealed partial class AdvancedDiseaseSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly Content.Shared.Inventory.InventorySystem _clinicalInventory = default!;

    /// <summary>Range (tiles) an airborne disease can spread to nearby hosts.</summary>
    private const float SpreadRange = 2.5f;

    public override void Initialize()
    {
        SubscribeLocalEvent<AdvancedDiseaseComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        // Combat debuffs — the weapon events carry (or let us find) the wielder.
        SubscribeLocalEvent<MeleeWeaponComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
        SubscribeLocalEvent<WieldableComponent, AttemptMeleeEvent>(OnAttemptMelee);
        SubscribeLocalEvent<GunComponent, GunRefreshModifiersEvent>(OnGunRefresh);
    }

    private void OnRefreshSpeed(EntityUid uid, AdvancedDiseaseComponent comp, ref RefreshMovementSpeedModifiersEvent args)
    {
        var mod = 1f;
        foreach (var id in comp.Active.Keys)
        {
            if (_proto.TryIndex<AdvancedDiseasePrototype>(id, out var disease))
                mod = Math.Min(mod, disease.SpeedModifier);
        }

        var blocked = mod < 0.8f;
        if (comp.SprintBlocked != blocked)
        {
            comp.SprintBlocked = blocked;
            Dirty(uid, comp);
        }
        if (mod < 1f)
            args.ModifySpeed(mod, mod);
    }

    private void OnGetMeleeDamage(EntityUid uid, MeleeWeaponComponent comp, ref GetMeleeDamageEvent args)
    {
        var mod = WorstMeleeModifier(args.User);
        if (mod < 1f)
            args.Damage *= mod;
    }

    private void OnAttemptMelee(EntityUid uid, WieldableComponent comp, ref AttemptMeleeEvent args)
    {
        if (args.Cancelled || !comp.Wielded)
            return;

        // Wielded (two-handed) melee is blocked while too weak. The wielder is the item's holder.
        if (BlocksTwoHandedMelee(Transform(uid).ParentUid))
        {
            args.Cancelled = true;
            args.Message = Loc.GetString("advanced-disease-too-weak-twohanded");
        }
    }

    private void OnGunRefresh(EntityUid uid, GunComponent comp, ref GunRefreshModifiersEvent args)
    {
        var spread = WorstRangedSpread(Transform(uid).ParentUid);
        if (spread <= 0f)
            return;

        var extra = Angle.FromDegrees(spread);
        args.MaxAngle += extra;
        args.MinAngle += extra;
    }

    private float WorstMeleeModifier(EntityUid user)
    {
        var mod = 1f;
        if (TryComp<AdvancedDiseaseComponent>(user, out var comp))
            foreach (var id in comp.Active.Keys)
                if (_proto.TryIndex<AdvancedDiseasePrototype>(id, out var d))
                    mod = Math.Min(mod, d.MeleeDamageModifier);
        return mod;
    }

    private bool BlocksTwoHandedMelee(EntityUid user)
    {
        if (!TryComp<AdvancedDiseaseComponent>(user, out var comp))
            return false;
        foreach (var id in comp.Active.Keys)
            if (_proto.TryIndex<AdvancedDiseasePrototype>(id, out var d) && d.BlockTwoHandedMelee)
                return true;
        return false;
    }

    private float WorstRangedSpread(EntityUid user)
    {
        var spread = 0f;
        if (TryComp<AdvancedDiseaseComponent>(user, out var comp))
            foreach (var id in comp.Active.Keys)
                if (_proto.TryIndex<AdvancedDiseasePrototype>(id, out var d))
                    spread = Math.Max(spread, d.RangedSpread);
        return spread;
    }

    /// <summary>Infectious exposure is resisted; mechanical/metabolic conditions use ApplyCondition.</summary>
    public bool TryInfect(EntityUid uid, string diseaseId, bool airborne = false)
    {
        if (!_proto.TryIndex<AdvancedDiseasePrototype>(diseaseId, out var disease)
            || disease.Cause is not (ClinicalCause.Viral or ClinicalCause.Bacterial or ClinicalCause.Fungal)
            || airborne && !disease.Airborne || !CanHaveCondition(uid, disease))
            return false;
        var immune = Comp<AdvancedHealthComponent>(uid).ImmuneDefense;
        var chance = Math.Clamp(disease.Infectivity * (1f - immune / 100f * 0.9f), 0f, 1f);
        if (airborne && TryComp<Content.Shared._Eclipse.Industrial.RoomSanitationComponent>(uid, out var room))
            chance *= room.Grade >= 2 ? 0.2f : room.Grade > 0 ? 0.35f : 1f;
        if (airborne && _clinicalInventory.TryGetSlotEntity(uid, "mask", out var mask)
            && TryComp<Content.Shared.Clothing.Components.MaskComponent>(mask, out var cover) && !cover.IsToggled) chance *= 0.5f;
        return _random.Prob(chance) && ApplyCondition(uid, diseaseId);
    }
}
