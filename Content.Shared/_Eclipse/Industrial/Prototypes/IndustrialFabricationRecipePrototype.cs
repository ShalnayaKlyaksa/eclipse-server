using Content.Shared._Eclipse.Industrial.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Dictionary;

namespace Content.Shared._Eclipse.Industrial.Prototypes;

[Prototype]
public sealed partial class IndustrialFabricationRecipePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public LocId? Name;

    [DataField(required: true)]
    public EntProtoId Result;

    [DataField]
    public int ResultCount = 1;

    [DataField]
    public float Duration = 30f;

    [DataField(required: true, customTypeSerializer: typeof(PrototypeIdDictionarySerializer<int, EntityPrototype>))]
    public Dictionary<string, int> Ingredients = new();
}
