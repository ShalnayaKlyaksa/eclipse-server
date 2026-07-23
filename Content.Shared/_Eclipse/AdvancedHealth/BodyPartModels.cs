using System.Linq;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.AdvancedHealth;

[Serializable, NetSerializable]
public enum BodyPartSlot : byte
{
    Head,
    Neck,
    Chest,
    Abdomen,
    Pelvis,
    LeftUpperArm,
    LeftForearm,
    LeftHand,
    RightUpperArm,
    RightForearm,
    RightHand,
    LeftThigh,
    LeftShin,
    LeftFoot,
    RightThigh,
    RightShin,
    RightFoot,
}

[Serializable, NetSerializable]
public enum BodyPartTarget : byte
{
    Auto,
    Head,
    Neck,
    Chest,
    Abdomen,
    Pelvis,
    LeftUpperArm,
    LeftForearm,
    LeftHand,
    RightUpperArm,
    RightForearm,
    RightHand,
    LeftThigh,
    LeftShin,
    LeftFoot,
    RightThigh,
    RightShin,
    RightFoot,
}

[Serializable, NetSerializable]
public enum WoundType : byte
{
    Cut,
    Puncture,
    Gunshot,
    Bruise,
    Burn,
    Fracture,
    Shrapnel,
    OrganDamage,
    NerveDamage,
}

[Serializable, NetSerializable]
public enum WoundSeverity : byte
{
    Minor,
    Moderate,
    Severe,
    Critical,
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class Wound
{
    [DataField] public WoundType Type;
    [DataField] public WoundSeverity Severity;
    [DataField] public BodyPartSlot BodyPart;
    [DataField] public float ExternalBleedingRate;
    [DataField] public float InternalBleedingRate;
    [DataField] public float Pain;
    [DataField] public float Trauma;
    [DataField] public float InfectionRisk;
    [DataField] public bool IsDirty;
    [DataField] public bool IsBandaged;
    [DataField] public bool IsSutured;
    [DataField] public bool HasForeignBody;
    /// <summary>How many hits were folded into this wound entry (for UI and network size).</summary>
    [DataField] public byte StackCount = 1;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class BodyPartState
{
    [DataField] public BodyPartSlot Slot;
    [DataField] public float SkinIntegrity = 100f;
    [DataField] public float MuscleIntegrity = 100f;
    [DataField] public float BoneIntegrity = 100f;
    [DataField] public float VesselIntegrity = 100f;
    [DataField] public float NerveIntegrity = 100f;
    [DataField] public float OrganIntegrity = 100f;

    // Per-race full-health values. A part at these values is "100%" regardless of race.
    [DataField] public float MaxSkinIntegrity = 100f;
    [DataField] public float MaxMuscleIntegrity = 100f;
    [DataField] public float MaxBoneIntegrity = 100f;
    [DataField] public float MaxVesselIntegrity = 100f;
    [DataField] public float MaxNerveIntegrity = 100f;
    [DataField] public float MaxOrganIntegrity = 100f;

    /// <summary>Tissue integrity as a 0..1 fraction of its per-race maximum.</summary>
    public float SkinFraction => Math.Clamp(SkinIntegrity / Math.Max(1f, MaxSkinIntegrity), 0f, 1f);
    public float MuscleFraction => Math.Clamp(MuscleIntegrity / Math.Max(1f, MaxMuscleIntegrity), 0f, 1f);
    public float BoneFraction => Math.Clamp(BoneIntegrity / Math.Max(1f, MaxBoneIntegrity), 0f, 1f);
    public float VesselFraction => Math.Clamp(VesselIntegrity / Math.Max(1f, MaxVesselIntegrity), 0f, 1f);
    public float NerveFraction => Math.Clamp(NerveIntegrity / Math.Max(1f, MaxNerveIntegrity), 0f, 1f);
    public float OrganFraction => Math.Clamp(OrganIntegrity / Math.Max(1f, MaxOrganIntegrity), 0f, 1f);
    [DataField] public List<Wound> Wounds = new();
    [DataField] public bool IsBandaged;
    [DataField] public bool IsSplinted;
    [DataField] public bool HasTourniquet;
    [DataField] public bool IsDestroyed;
    [DataField] public bool IsBleeding;
    /// <summary>Embedded shrapnel/bullets remaining in this zone (one per penetrating hit).</summary>
    [DataField] public byte ForeignBodyCount;

    public BodyPartUiState ToUiState()
    {
        var bleedRate = Wounds.Sum(w => w.ExternalBleedingRate + w.InternalBleedingRate);
        var bleeding = GetBleedingLevel(bleedRate);

        // Condition is graded from actual tissue integrity so the doll and label show five stages.
        var stage = GetConditionStage();
        var severity = stage switch
        {
            1 => WoundSeverity.Minor,
            2 => WoundSeverity.Moderate,
            3 => WoundSeverity.Severe,
            >= 4 => WoundSeverity.Critical,
            _ => WoundSeverity.Minor,
        };

        return new(
            Slot,
            severity,
            stage >= 1,
            bleeding,
            Wounds.Any(wound => wound.Type == WoundType.Fracture),
            IsBandaged,
            IsSplinted,
            HasTourniquet,
            IsDestroyed,
            ForeignBodyCount > 0 || Wounds.Any(wound => wound.HasForeignBody));
    }

    /// <summary>
    /// Five-stage condition band (0 healthy … 4 critical), combining tissue integrity with the
    /// number/severity of wounds so a part riddled with wounds never reads as merely "minor".
    /// </summary>
    public int GetConditionStage()
    {
        if (IsDestroyed)
            return 4;

        // Fractions of each tissue's per-race maximum, so a full part reads 100% for any race.
        // Skin and muscle exist on every part; organs only matter for core parts.
        var worst = Math.Min(SkinFraction, MuscleFraction);
        if (Slot.IsCore())
            worst = Math.Min(worst, OrganFraction);

        var integrityStage = worst >= 0.9f ? 0
            : worst >= 0.7f ? 1
            : worst >= 0.45f ? 2
            : worst >= 0.2f ? 3
            : 4;

        var woundStage = 0;
        if (Wounds.Count > 0)
        {
            woundStage = Wounds.Max(w => w.Severity) switch
            {
                WoundSeverity.Critical => 4,
                WoundSeverity.Severe => 3,
                WoundSeverity.Moderate => 2,
                _ => 1,
            };

            // Multiple wounds compound the condition.
            var count = Wounds.Sum(w => (int) w.StackCount);
            if (count >= 5)
                woundStage = Math.Max(woundStage, 3);
            else if (count >= 3)
                woundStage = Math.Max(woundStage, 2);

            if (Wounds.Any(w => w.Type == WoundType.Fracture))
                woundStage = Math.Max(woundStage, 3);
        }

        return Math.Max(integrityStage, woundStage);
    }

    private BleedingLevel GetBleedingLevel(float bleedRate)
    {
        if (!IsBleeding || bleedRate <= 0.01f)
            return BleedingLevel.None;

        return bleedRate switch
        {
            < 0.05f => BleedingLevel.Light,
            < 0.11f => BleedingLevel.Moderate,
            _ => BleedingLevel.Heavy,
        };
    }
}

[Serializable, NetSerializable]
public enum BleedingLevel : byte
{
    None = 0,
    Light = 1,
    Moderate = 2,
    Heavy = 3,
}

[Serializable, NetSerializable]
public readonly record struct BodyPartUiState(
    BodyPartSlot Slot,
    WoundSeverity Severity,
    bool Damaged,
    BleedingLevel Bleeding,
    bool Fractured,
    bool Bandaged,
    bool Splinted,
    bool Tourniquet,
    bool Destroyed,
    bool ForeignBody);

public static class BodyPartHelpers
{
    public static BodyPartSlot ToSlot(this BodyPartTarget target)
        => (BodyPartSlot) ((byte) target - 1);

    public static BodyPartTarget ToTarget(this BodyPartSlot slot)
        => (BodyPartTarget) ((byte) slot + 1);

    public static bool IsLimb(this BodyPartSlot slot)
        => slot >= BodyPartSlot.LeftUpperArm;

    public static bool IsCore(this BodyPartSlot slot)
        => slot <= BodyPartSlot.Pelvis;
}
