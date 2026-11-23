using System.Linq;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Cooking;

[Serializable, NetSerializable]
public enum CulinaryProperty : byte
{
    Recovery, Warmth, Cooling, Satiety, Hydration, Stimulation, Calm,
    Heaviness, Thirst, Nausea, Healing, Blood, Efficiency,
    Fat, Acid, Starch, Rapid, Stability,
}

[Serializable, NetSerializable]
public enum CulinaryMethod : byte
{
    Chop, Grind, Soak, Marinate, Boil, Simmer, Reduce, Fry, Bake, Steam,
}

/// <summary>Properties per unit of the original food reagent. Data follows ordinary splitting, spoons and pouring.</summary>
[Serializable, NetSerializable]
public sealed partial class CulinaryData : ReagentData
{
    [DataField] public Dictionary<CulinaryProperty, float> Traits = new();
    [DataField] public uint Methods;
    [DataField] public bool Cooked;

    public float Get(CulinaryProperty property) => Traits.GetValueOrDefault(property);

    public override ReagentData Clone() => new CulinaryData
    {
        Traits = new(Traits), Methods = Methods, Cooked = Cooked,
    };

    public override bool Equals(ReagentData? other) => other is CulinaryData data
        && Methods == data.Methods && Cooked == data.Cooked && Traits.Count == data.Traits.Count
        && Traits.All(pair => data.Traits.TryGetValue(pair.Key, out var value) && value.Equals(pair.Value));

    public override int GetHashCode()
    {
        var hash = HashCode.Combine(Methods, Cooked);
        foreach (var pair in Traits.OrderBy(pair => pair.Key))
            hash = HashCode.Combine(hash, pair.Key, pair.Value);
        return hash;
    }
}

/// <summary>Data-driven profiles for existing foods, crop yields and liquid ingredients.</summary>
[Prototype]
public sealed partial class CulinaryIngredientPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField] public List<EntProtoId> Foods = new();
    [DataField] public List<ProtoId<ReagentPrototype>> Reagents = new();
    [DataField] public Dictionary<CulinaryProperty, float> Traits = new();
}
