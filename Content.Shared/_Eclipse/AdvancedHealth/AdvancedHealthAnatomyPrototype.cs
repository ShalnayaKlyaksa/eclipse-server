using Robust.Shared.Prototypes;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>
/// YAML-driven anatomical defaults for a species. The ID normally matches SpeciesPrototype.ID.
/// </summary>
[Prototype]
public sealed partial class AdvancedHealthAnatomyPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField] public float MaxBloodVolume = 5000f;
    [DataField] public float BleedingModifier = 1f;
    [DataField] public float PainModifier = 1f;
    [DataField] public float TraumaModifier = 1f;
    [DataField] public float OxygenationModifier = 1f;
    [DataField] public string BodyFluid = "Blood";
    [DataField] public Color FluidColor = Color.Red;
    [DataField] public List<string> BloodTypes = new() { "O-", "O+", "A-", "A+", "B-", "B+", "AB-", "AB+" };
    [DataField] public string IncompatibleTransfusionPopup = "advanced-health-transfusion-incompatible";

    [DataField] public bool HasBlood = true;
    [DataField] public bool HasPain = true;
    [DataField] public bool NeedsOxygen = true;
    [DataField] public bool CanFracture = true;

    [DataField] public float DefaultSkinIntegrity = 100f;
    [DataField] public float DefaultMuscleIntegrity = 100f;
    [DataField] public float DefaultBoneIntegrity = 100f;
    [DataField] public float DefaultVesselIntegrity = 100f;
    [DataField] public float DefaultNerveIntegrity = 100f;
    [DataField] public float DefaultOrganIntegrity = 100f;

    /// <summary>
    /// Optional per-zone overrides. Missing zones use the defaults above.
    /// Setting Enabled=false removes a zone from hit selection and the body doll.
    /// </summary>
    [DataField] public Dictionary<BodyPartSlot, BodyPartAnatomyData> BodyParts = new();
}

[DataDefinition]
public sealed partial class BodyPartAnatomyData
{
    [DataField] public bool Enabled = true;
    [DataField] public float? SkinIntegrity;
    [DataField] public float? MuscleIntegrity;
    [DataField] public float? BoneIntegrity;
    [DataField] public float? VesselIntegrity;
    [DataField] public float? NerveIntegrity;
    [DataField] public float? OrganIntegrity;
    [DataField] public float HitWeightModifier = 1f;
}
