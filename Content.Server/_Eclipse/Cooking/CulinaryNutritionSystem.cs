using System.Linq;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.Cooking;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Jittering;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Nutrition;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Rejuvenate;
using Content.Shared.Temperature;
using Content.Shared.Temperature.Components;

namespace Content.Server._Eclipse.Cooking;

/// <summary>Food acts after ingestion, including spoon feeding, with a shared capped reserve for each effect.</summary>
public sealed class CulinaryNutritionSystem : EntitySystem
{
    [Dependency] private readonly HungerSystem _hunger = default!;
    [Dependency] private readonly ThirstSystem _thirst = default!;
    [Dependency] private readonly SharedStaminaSystem _stamina = default!;
    [Dependency] private readonly SharedBloodstreamSystem _blood = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly SharedJitteringSystem _jitter = default!;
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<BodyComponent, IngestingEvent>(OnIngest);
        SubscribeLocalEvent<CulinaryNutritionComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<TemperatureComponent, ModifyChangedTemperatureEvent>(OnTemperature);
    }

    private void OnIngest(Entity<BodyComponent> ent, ref IngestingEvent args)
    {
        foreach (var reagent in args.Split.Contents)
        {
            var data = reagent.Reagent.Data?.OfType<CulinaryData>().FirstOrDefault();
            if (data is not { Cooked: true }) continue;
            var nutrition = EnsureComp<CulinaryNutritionComponent>(ent);
            var delay = CulinaryMath.Onset(data);
            var scale = CulinaryMath.DurationScale(data);
            foreach (var (property, value) in data.Traits)
            {
                var index = (int) property;
                if (index >= nutrition.Doses.Length || !float.IsFinite(value) || value <= 0) continue;
                var room = Math.Max(0, 30 - nutrition.Doses[index] - nutrition.Pending[index]);
                nutrition.Pending[index] += Math.Min(room, value * reagent.Quantity.Float() * scale);
                nutrition.Delays[index] = nutrition.Delays[index] > 0 ? Math.Min(nutrition.Delays[index], delay) : delay;
            }
        }
    }

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < 1) return;
        var seconds = _accumulator;
        _accumulator = 0;
        var query = EntityQueryEnumerator<CulinaryNutritionComponent>();
        while (query.MoveNext(out var uid, out var nutrition))
        {
            if (Paused(uid)) continue;
            Advance((uid, nutrition), seconds);
        }
    }

    public void Advance(Entity<CulinaryNutritionComponent> ent, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0) return;
        var dead = TryComp<MobStateComponent>(ent, out var state) && state.CurrentState == MobState.Dead;
        var active = false;
        for (var i = 0; i < ent.Comp.Doses.Length; i++)
        {
            var activeSeconds = seconds;
            if (ent.Comp.Pending[i] > 0)
            {
                var delay = ent.Comp.Delays[i];
                ent.Comp.Delays[i] = Math.Max(0, delay - seconds);
                if (ent.Comp.Delays[i] <= 0)
                {
                    ent.Comp.Doses[i] = Math.Min(30, ent.Comp.Doses[i] + ent.Comp.Pending[i]);
                    ent.Comp.Pending[i] = 0;
                    activeSeconds = Math.Max(0, seconds - delay);
                }
            }
            ent.Comp.Strengths[i] = dead ? 0 : Math.Min(1, ent.Comp.Doses[i] / 4);
            ent.Comp.Doses[i] = Math.Max(0, ent.Comp.Doses[i] - activeSeconds * 0.1f);
            active |= ent.Comp.Doses[i] > 0 || ent.Comp.Pending[i] > 0;
        }
        float Strength(CulinaryProperty property) => ent.Comp.Strengths[(int) property];
        var old = (ent.Comp.Recovery, ent.Comp.Efficiency, ent.Comp.Heaviness);
        ent.Comp.Recovery = Strength(CulinaryProperty.Recovery);
        ent.Comp.Efficiency = Strength(CulinaryProperty.Efficiency);
        ent.Comp.Heaviness = Strength(CulinaryProperty.Heaviness);
        if (old != (ent.Comp.Recovery, ent.Comp.Efficiency, ent.Comp.Heaviness)) Dirty(ent);

        if (!dead)
        {
            var running = TryComp<InputMoverComponent>(ent, out var mover) && mover.Sprinting && mover.HasDirectionalMovement;
            if (!running && TryComp<StaminaComponent>(ent, out var stamina) && !stamina.Critical && stamina.StaminaDamage > 0)
                _stamina.TakeStaminaDamage(ent, -ent.Comp.Recovery * 0.5f * seconds, stamina, visual: false);
            if (TryComp<HungerComponent>(ent, out var hunger))
                _hunger.ModifyHunger(ent, Math.Min(hunger.ActualDecayRate * 0.5f, 0.03f)
                    * Strength(CulinaryProperty.Satiety) * seconds, hunger);
            if (TryComp<ThirstComponent>(ent, out var thirst))
                _thirst.ModifyThirst(ent, thirst, (Strength(CulinaryProperty.Hydration)
                    - Strength(CulinaryProperty.Thirst)) * 0.05f * seconds);
            if (TryComp<AdvancedExertionComponent>(ent, out var exertion))
            {
                exertion.SleepDebt = Math.Clamp(exertion.SleepDebt + (Strength(CulinaryProperty.Calm)
                    - Strength(CulinaryProperty.Stimulation)) * 0.01f * seconds, 0, 100);
                exertion.RecoveryLoad = Math.Max(0, exertion.RecoveryLoad - Strength(CulinaryProperty.Calm) * 0.002f * seconds);
                Dirty(ent.Owner, exertion);
            }
            if (Strength(CulinaryProperty.Stimulation) > 0.75f)
                _jitter.DoJitter(ent, TimeSpan.FromSeconds(2), true, 3);
            if (Strength(CulinaryProperty.Nausea) > 0)
            {
                if (TryComp<ThirstComponent>(ent, out var upsetThirst))
                    _thirst.ModifyThirst(ent, upsetThirst, -Strength(CulinaryProperty.Nausea) * 0.03f * seconds);
                if (TryComp<AdvancedExertionComponent>(ent, out var upsetExertion))
                {
                    upsetExertion.RecoveryLoad = Math.Min(1, upsetExertion.RecoveryLoad + Strength(CulinaryProperty.Nausea) * 0.003f * seconds);
                    Dirty(ent.Owner, upsetExertion);
                }
            }
            if (Strength(CulinaryProperty.Blood) > 0)
                _blood.TryModifyBloodLevel(ent.Owner, FixedPoint2.New(Strength(CulinaryProperty.Blood) * 0.03f * seconds));
            if (Strength(CulinaryProperty.Healing) > 0 && state?.CurrentState == MobState.Alive
                && (mover == null || !mover.HasDirectionalMovement) && HasComp<DamageableComponent>(ent))
                _damage.TryChangeDamage(ent.Owner, new DamageSpecifier
                {
                    DamageDict = new()
                    {
                        ["Blunt"] = FixedPoint2.New(-0.01f * Strength(CulinaryProperty.Healing) * seconds),
                        ["Heat"] = FixedPoint2.New(-0.01f * Strength(CulinaryProperty.Healing) * seconds),
                    },
                }, interruptsDoAfters: false);
        }
        if (!active) RemCompDeferred<CulinaryNutritionComponent>(ent);
    }

    private void OnTemperature(Entity<TemperatureComponent> ent, ref ModifyChangedTemperatureEvent args)
    {
        if (!TryComp<CulinaryNutritionComponent>(ent, out var nutrition)) return;
        // Resistance applies to harmful exchange only; it never blocks a return towards normal temperature.
        var property = args.TemperatureDelta < 0 && ent.Comp.CurrentTemperature <= 310.15f ? CulinaryProperty.Warmth
            : args.TemperatureDelta > 0 && ent.Comp.CurrentTemperature >= 310.15f ? CulinaryProperty.Cooling : (CulinaryProperty?) null;
        if (property != null) args.TemperatureDelta *= 1 - nutrition.Strengths[(int) property.Value] * 0.25f;
    }

    private void OnRejuvenate(Entity<CulinaryNutritionComponent> ent, ref RejuvenateEvent args)
        => RemCompDeferred<CulinaryNutritionComponent>(ent);
}
