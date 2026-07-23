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
public sealed class AdvancedDiseaseSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    private float _accumulator;
    private const float TickInterval = 1f;

    /// <summary>Infection load at/above which an untreated wound can spawn wound fever.</summary>
    private const float WoundFeverThreshold = 40f;
    private const string WoundFeverId = "WoundFever";
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

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < TickInterval)
            return;

        var elapsed = _accumulator;
        _accumulator = 0f;

        // Infections are collected and applied AFTER iteration — TryInfect adds components, which
        // would otherwise invalidate the entity queries mid-loop.
        var pending = new List<(EntityUid Target, string Disease)>();

        var query = EntityQueryEnumerator<AdvancedDiseaseComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Active.Count == 0)
                continue;

            TryComp<AdvancedHealthComponent>(uid, out var health);
            var immune = health?.ImmuneDefense ?? 100f;

            var toRemove = new List<string>();
            foreach (var id in comp.Active.Keys)
            {
                if (!_proto.TryIndex<AdvancedDiseasePrototype>(id, out var disease))
                {
                    toRemove.Add(id);
                    continue;
                }

                comp.Active[id] += elapsed;

                if (health != null)
                {
                    if (health.HasPain && disease.PainPerSecond > 0f)
                        health.Pain = Math.Clamp(health.Pain + disease.PainPerSecond * elapsed, 0f, 100f);
                    if (disease.ShockPerSecond > 0f)
                        health.Shock = Math.Clamp(health.Shock + disease.ShockPerSecond * elapsed, 0f, 100f);
                    if (disease.ImmuneDrain > 0f)
                        health.ImmuneDefense = Math.Clamp(health.ImmuneDefense - disease.ImmuneDrain * elapsed, 0f, 100f);
                    Dirty(uid, health);
                }

                // A strong immune system makes progress clearing it; a weak one lets it linger.
                var progress = comp.ClearProgress.GetValueOrDefault(id);
                progress = immune >= disease.ClearImmuneThreshold
                    ? progress + elapsed
                    : Math.Max(0f, progress - elapsed * 0.5f);
                comp.ClearProgress[id] = progress;

                if (progress >= disease.ClearTime)
                    toRemove.Add(id);
            }

            foreach (var id in toRemove)
            {
                comp.Active.Remove(id);
                comp.ClearProgress.Remove(id);
            }

            Dirty(uid, comp);
            if (toRemove.Count > 0)
                _movement.RefreshMovementSpeedModifiers(uid);

            // Airborne spread: occasionally try to pass each disease to nearby hosts.
            if (comp.Active.Count > 0 && _random.Prob(0.15f))
            {
                var nearby = new HashSet<Entity<AdvancedHealthComponent>>();
                _lookup.GetEntitiesInRange(Transform(uid).Coordinates, SpreadRange, nearby);
                foreach (var host in nearby)
                {
                    if (host.Owner == uid)
                        continue;
                    foreach (var id in comp.Active.Keys)
                        pending.Add((host.Owner, id));
                }
            }
        }

        // Wound fever: a badly infected, untreated body raises a fever disease.
        var healthQuery = EntityQueryEnumerator<AdvancedHealthComponent>();
        while (healthQuery.MoveNext(out var uid, out var hp))
        {
            if (hp.InfectionLoad >= WoundFeverThreshold && _random.Prob(0.02f))
                pending.Add((uid, WoundFeverId));
        }

        foreach (var (target, disease) in pending)
        {
            if (Exists(target))
                TryInfect(target, disease);
        }
    }

    /// <summary>Attempt to infect an entity; the immune system resists based on its strength.</summary>
    public bool TryInfect(EntityUid uid, string diseaseId)
    {
        if (!_proto.TryIndex<AdvancedDiseasePrototype>(diseaseId, out var disease))
            return false;

        var comp = EnsureComp<AdvancedDiseaseComponent>(uid);
        if (comp.Active.ContainsKey(diseaseId))
            return false;

        var immune = TryComp<AdvancedHealthComponent>(uid, out var health) ? health.ImmuneDefense : 100f;
        var chance = Math.Clamp(disease.Infectivity * (1f - immune / 100f * 0.9f), 0f, 1f);
        if (!_random.Prob(chance))
            return false;

        comp.Active[diseaseId] = 0f;
        comp.ClearProgress[diseaseId] = 0f;
        Dirty(uid, comp);
        _movement.RefreshMovementSpeedModifiers(uid);
        return true;
    }
}
