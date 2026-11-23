using System.Linq;

namespace Content.Shared._Eclipse.Cooking;

/// <summary>Pure batch calculations; no nutrition, property mass or beneficial reagents are created by dilution.</summary>
public static class CulinaryMath
{
    public const float PropertyScale = 0.1f;

    public static bool IsHot(CulinaryMethod method) => method >= CulinaryMethod.Boil;
    public static float Seconds(CulinaryMethod method) => method switch
    {
        CulinaryMethod.Chop => 6, CulinaryMethod.Grind => 8, CulinaryMethod.Soak => 25,
        CulinaryMethod.Marinate => 35, CulinaryMethod.Boil => 30, CulinaryMethod.Simmer => 60,
        CulinaryMethod.Reduce => 45, CulinaryMethod.Fry => 20, CulinaryMethod.Bake => 45,
        CulinaryMethod.Steam => 35, _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };

    public static CulinaryData Process(CulinaryData input, CulinaryMethod method, CulinaryData? environment = null)
    {
        var result = (CulinaryData) input.Clone();
        var flag = 1u << (int) method;
        if ((input.Methods & flag) != 0)
            return result;
        result.Methods |= flag;
        result.Cooked |= IsHot(method);
        environment ??= input;
        var traits = result.Traits;
        var budget = traits.Values.Sum();
        var heatProtection = IsHot(method) ? Math.Clamp(environment.Get(CulinaryProperty.Stability) / (PropertyScale * 4), 0, 0.75f) : 0;
        void Scale(CulinaryProperty property, float factor)
        {
            if (factor < 1 && property is CulinaryProperty.Stimulation or CulinaryProperty.Cooling or CulinaryProperty.Healing)
                factor += (1 - factor) * heatProtection;
            if (traits.TryGetValue(property, out var value)) traits[property] = value * factor;
        }
        void Move(CulinaryProperty from, CulinaryProperty to, float fraction)
        {
            var amount = result.Get(from) * fraction;
            traits[from] = result.Get(from) - amount;
            traits[to] = result.Get(to) + amount;
        }
        switch (method)
        {
            case CulinaryMethod.Chop:
                Move(CulinaryProperty.Starch, CulinaryProperty.Rapid, 0.25f);
                break;
            case CulinaryMethod.Grind:
                Move(CulinaryProperty.Satiety, CulinaryProperty.Rapid, 0.35f);
                Scale(CulinaryProperty.Stability, 0.8f);
                break;
            case CulinaryMethod.Soak:
                Scale(CulinaryProperty.Thirst, 0.5f);
                Scale(CulinaryProperty.Nausea, 0.5f);
                Scale(CulinaryProperty.Warmth, 0.7f);
                Scale(CulinaryProperty.Stimulation, 0.7f);
                break;
            case CulinaryMethod.Marinate:
                Scale(CulinaryProperty.Heaviness, 0.6f);
                Scale(CulinaryProperty.Recovery, 0.9f);
                Move(CulinaryProperty.Starch, CulinaryProperty.Rapid, 0.2f);
                break;
            case CulinaryMethod.Boil:
                Scale(CulinaryProperty.Stimulation, 0.8f);
                Scale(CulinaryProperty.Cooling, 0.8f);
                Scale(CulinaryProperty.Nausea, 0.7f);
                break;
            case CulinaryMethod.Simmer:
                Move(CulinaryProperty.Rapid, CulinaryProperty.Starch, 0.7f);
                Scale(CulinaryProperty.Stimulation, 0.5f);
                Scale(CulinaryProperty.Recovery, 1.15f + Math.Min(0.2f, environment.Get(CulinaryProperty.Acid)));
                Scale(CulinaryProperty.Heaviness, 0.75f);
                break;
            case CulinaryMethod.Reduce:
                Scale(CulinaryProperty.Hydration, 0.5f);
                Scale(CulinaryProperty.Stimulation, 0.75f);
                break;
            case CulinaryMethod.Fry:
                Move(CulinaryProperty.Cooling, CulinaryProperty.Warmth, 0.5f);
                Scale(CulinaryProperty.Warmth, 1.2f + Math.Min(0.2f, environment.Get(CulinaryProperty.Fat)));
                Scale(CulinaryProperty.Thirst, 1.2f);
                Move(CulinaryProperty.Fat, CulinaryProperty.Heaviness, 0.3f);
                break;
            case CulinaryMethod.Bake:
                Move(CulinaryProperty.Rapid, CulinaryProperty.Satiety, 0.5f);
                Scale(CulinaryProperty.Healing, 0.75f);
                break;
            case CulinaryMethod.Steam:
                Scale(CulinaryProperty.Heaviness, 0.8f);
                Scale(CulinaryProperty.Nausea, 0.8f);
                break;
        }

        // A processing chain may redistribute or lose properties, never multiply its original budget.
        var total = traits.Values.Sum();
        if (total > budget && total > 0)
            foreach (var property in traits.Keys.ToArray()) traits[property] *= budget / total;
        return result;
    }

    /// <summary>Resolve mixture interactions once, when the batch is finished, in a deterministic order.</summary>
    public static CulinaryData Resolve(CulinaryData input)
    {
        var result = (CulinaryData) input.Clone();
        void Cancel(CulinaryProperty a, CulinaryProperty b, float fraction = 1)
        {
            var amount = Math.Min(result.Get(a), result.Get(b)) * fraction;
            result.Traits[a] = result.Get(a) - amount;
            result.Traits[b] = result.Get(b) - amount;
        }
        Cancel(CulinaryProperty.Warmth, CulinaryProperty.Cooling);
        Cancel(CulinaryProperty.Stimulation, CulinaryProperty.Calm);
        Cancel(CulinaryProperty.Thirst, CulinaryProperty.Hydration, 0.75f);
        Cancel(CulinaryProperty.Fat, CulinaryProperty.Acid, 0.5f);
        // Starch softens both the benefit and the drawback of hot food.
        var binding = Math.Min(result.Get(CulinaryProperty.Starch) * 0.25f,
            Math.Min(result.Get(CulinaryProperty.Warmth), result.Get(CulinaryProperty.Thirst)));
        result.Traits[CulinaryProperty.Warmth] = result.Get(CulinaryProperty.Warmth) - binding;
        result.Traits[CulinaryProperty.Thirst] = result.Get(CulinaryProperty.Thirst) - binding;
        result.Traits[CulinaryProperty.Starch] = result.Get(CulinaryProperty.Starch) - binding;
        return result;
    }

    public static float Onset(CulinaryData data) => Math.Clamp(5 + (data.Get(CulinaryProperty.Fat) * 2
        + data.Get(CulinaryProperty.Starch) * 3 - data.Get(CulinaryProperty.Rapid) * 3) / PropertyScale, 2, 35);

    public static float DurationScale(CulinaryData data) => Math.Clamp(1 + (data.Get(CulinaryProperty.Fat) * 0.08f
        + data.Get(CulinaryProperty.Starch) * 0.08f - data.Get(CulinaryProperty.Rapid) * 0.08f) / PropertyScale, 0.5f, 1.8f);
}
