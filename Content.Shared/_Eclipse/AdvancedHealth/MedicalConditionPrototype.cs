using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>
/// An anatomical condition supplied by another system (implants, medication, surgery, etc.).
/// Wounds and their dressings remain authoritative in BodyPartState; do not duplicate them here.
/// </summary>
[Prototype]
public sealed partial class MedicalConditionPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public LocId Name;
    [DataField(required: true)] public LocId Description;
    [DataField] public bool Beneficial;
    /// <summary>Multiplier at full severity. A severity of zero has no effect.</summary>
    [DataField] public float FunctionMultiplier = 1f;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class MedicalCondition
{
    [DataField(required: true)] public ProtoId<MedicalConditionPrototype> Prototype;
    [DataField] public float Severity = 1f;
}
