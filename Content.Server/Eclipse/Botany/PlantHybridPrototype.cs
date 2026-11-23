using Content.Server.Botany;
using Robust.Shared.Prototypes;

namespace Content.Server.Eclipse.Botany;

[Prototype]
public sealed partial class PlantHybridPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public ProtoId<SeedPrototype> ParentA;
    [DataField(required: true)] public ProtoId<SeedPrototype> ParentB;
    [DataField(required: true)] public ProtoId<SeedPrototype> Result;
    [DataField] public float Chance = 0.2f;
}
