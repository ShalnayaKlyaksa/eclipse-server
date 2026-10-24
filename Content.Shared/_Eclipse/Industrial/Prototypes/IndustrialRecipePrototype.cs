using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Dictionary;

namespace Content.Shared._Eclipse.Industrial.Prototypes;

[Prototype]
public sealed partial class IndustrialRecipePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public IndustrialProcessorType Processor;

    [DataField(required: true)]
    public float Time = 5f;

    [DataField(required: true, customTypeSerializer: typeof(PrototypeIdDictionarySerializer<int, EntityPrototype>))]
    public Dictionary<string, int> Inputs = new();

    [DataField(required: true, customTypeSerializer: typeof(PrototypeIdDictionarySerializer<int, EntityPrototype>))]
    public Dictionary<string, int> Outputs = new();
}
