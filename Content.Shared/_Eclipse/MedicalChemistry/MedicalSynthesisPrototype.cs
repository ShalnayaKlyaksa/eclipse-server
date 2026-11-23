using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared._Eclipse.MedicalChemistry;

[Prototype]
public sealed partial class MedicalSynthesisPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public ProtoId<ReagentPrototype> Precursor;
    [DataField(required: true)] public ProtoId<ReagentPrototype> Medicine;
    [DataField(required: true)] public float MinTemperature;
    [DataField(required: true)] public float MaxTemperature;
    [DataField] public bool Advanced;
}
