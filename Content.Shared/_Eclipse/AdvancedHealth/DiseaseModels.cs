using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>
/// A disease definition. Diseases progress while the immune system is too weak to clear them and
/// apply medical effects and debuffs (weakness, clumsiness, etc.). This is the foundation — more
/// effects (melee/accuracy hooks) can be layered on later.
/// </summary>
[Prototype]
public sealed partial class AdvancedDiseasePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField] public LocId Name = "advanced-disease-unknown";
    [DataField] public LocId Description = "clinical-condition-unknown-desc";
    [DataField] public ClinicalCause Cause = ClinicalCause.Viral;
    [DataField] public ClinicalTest Diagnostic = ClinicalTest.Examination;
    [DataField] public BodyPartSlot Region = BodyPartSlot.Chest;
    [DataField] public string? RequiredOrgan;
    [DataField] public bool Airborne;
    [DataField] public bool Bloodborne;
    [DataField] public MedicationClass Treatment;
    [DataField] public AdvancedTreatmentType? Procedure;
    [DataField] public bool RequiresSourceControl;
    [DataField] public bool Chronic;
    [DataField] public float Progression = 0.001f;
    [DataField] public float NaturalRecovery = 0.002f;
    [DataField] public float LungImpairment;
    [DataField] public float PressureLoss;
    [DataField] public float Fever;
    [DataField] public float GlucoseRise;
    [DataField] public float OrganImpairment;
    [DataField] public ProtoId<AdvancedDiseasePrototype>? Complication;
    /// <summary>Short status word shown for the debuff it causes, e.g. "Слабость".</summary>
    [DataField] public LocId? StatusLabel;

    /// <summary>Movement speed multiplier while sick (0.8 = −20%).</summary>
    [DataField] public float SpeedModifier = 1f;
    /// <summary>Melee damage multiplier while sick (0.5 = −50%).</summary>
    [DataField] public float MeleeDamageModifier = 1f;
    /// <summary>Two-handed melee weapons cannot be used while sick.</summary>
    [DataField] public bool BlockTwoHandedMelee;
    /// <summary>Extra weapon spread (accuracy penalty) in degrees.</summary>
    [DataField] public float RangedSpread;

    /// <summary>Pain added per second while active.</summary>
    [DataField] public float PainPerSecond;
    /// <summary>Shock added per second while active.</summary>
    [DataField] public float ShockPerSecond;
    /// <summary>Immune defense drained per second while fighting the disease.</summary>
    [DataField] public float ImmuneDrain = 0.05f;

    /// <summary>Seconds of sustained strong immunity needed to clear the disease.</summary>
    [DataField] public float ClearTime = 120f;
    /// <summary>Immune defense at/above which the immune system makes progress clearing it.</summary>
    [DataField] public float ClearImmuneThreshold = 55f;

    /// <summary>Base infectivity 0..1 — chance to catch it, before immune resistance.</summary>
    [DataField] public float Infectivity = 0.5f;
}

/// <summary>Diseases currently affecting a mob, mapped to how long they've been active (seconds).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AdvancedDiseaseComponent : Component
{
    public float NauseaExposure;
    public float VomitCooldown;
    public float VomitSymptomsRemaining;
    public bool VomitWasBloody;
    [DataField] public Dictionary<string, float> Active = new();
    [DataField] public Dictionary<string, float> Severity = new();
    [DataField] public Dictionary<string, BodyPartSlot> Locations = new();
    [DataField] public HashSet<string> SourceControlled = new();
    [DataField, AutoNetworkedField] public Dictionary<string, float> Known = new();
    [DataField, AutoNetworkedField] public ClinicalSymptoms Symptoms;
    [DataField, AutoNetworkedField] public bool SprintBlocked;
    [DataField, AutoNetworkedField] public HashSet<ClinicalTest> CompletedTests = new();
    /// <summary>Progress (seconds of strong immunity) toward clearing each active disease.</summary>
    [DataField] public Dictionary<string, float> ClearProgress = new();
}

[Serializable, NetSerializable]
public enum ClinicalCause : byte { Viral, Bacterial, Fungal, Inflammatory, Mechanical, Metabolic, Toxic }
[Serializable, NetSerializable]
public enum ClinicalTest : byte { Examination, Blood, Imaging, Microbiology }
[Serializable, NetSerializable]
public enum MedicationClass : byte
{
    None, Antibiotic, Antifungal, Antiviral, Bronchodilator, Antihistamine, Adrenaline,
    Opioid, Sedative, Naloxone, Insulin, Glucose, Anticonvulsant, AntiInflammatory,
}
[Flags, Serializable, NetSerializable]
public enum ClinicalSymptoms : ushort
{
    None = 0, Fever = 1, Breathlessness = 2, Pain = 4, CirculatoryFailure = 8,
    Sedation = 16, DrugReaction = 32, GlucoseAbnormal = 64, Nausea = 128, Vomiting = 256, Hematemesis = 512,
}
