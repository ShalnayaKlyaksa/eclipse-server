using System.Linq;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>The chip and server use the same indications; an internal bleed is not a bandaging target.</summary>
public static class AdvancedTreatmentRules
{
    public static bool IsOperative(AdvancedTreatmentType treatment) => treatment is
        AdvancedTreatmentType.SurgicalAccess or AdvancedTreatmentType.InternalHemostasis
        or AdvancedTreatmentType.BoneFixation or AdvancedTreatmentType.SurgicalClosure
        or AdvancedTreatmentType.ThoracicDrain or AdvancedTreatmentType.SourceControl or AdvancedTreatmentType.OrganRepair;

    // The initial trauma operations cover limbs and trunk; cranial/spinal access needs its own procedure.
    public static bool NeedsSurgicalAccess(BodyPartState part) => part.Slot is not (BodyPartSlot.Head or BodyPartSlot.Neck)
        && (part.ClinicalProcedures.Contains(AdvancedTreatmentType.SourceControl)
            || part.ClinicalProcedures.Contains(AdvancedTreatmentType.OrganRepair)
            || part.Wounds.Any(wound => wound.InternalBleedingRate > 0.001f)
            || part.Slot.IsLimb() && part.Wounds.Any(wound => wound.Type == WoundType.Fracture
                && wound.Severity >= WoundSeverity.Severe && !wound.IsBoneFixed));

    public static bool IsNeeded(BodyPartState part, AdvancedTreatmentType treatment)
    {
        if (part.IsDestroyed && treatment is not (AdvancedTreatmentType.Bandage
            or AdvancedTreatmentType.PressureBandage or AdvancedTreatmentType.Tourniquet
            or AdvancedTreatmentType.Hemostatic or AdvancedTreatmentType.TourniquetRemoval))
            return false;
        var external = part.Wounds.Any(wound => wound.ExternalBleedingRate > 0.01f);
        var dirty = part.Wounds.Any(wound => wound.IsDirty);
        return treatment switch
        {
            AdvancedTreatmentType.Injection or AdvancedTreatmentType.ClinicalExamination or AdvancedTreatmentType.BloodTest
                or AdvancedTreatmentType.MedicalImaging or AdvancedTreatmentType.MicrobiologyTest => true,
            AdvancedTreatmentType.XRay => true,
            AdvancedTreatmentType.Electrocardiogram => part.Slot == BodyPartSlot.Chest,
            AdvancedTreatmentType.ThoracicDrain => part.Slot == BodyPartSlot.Chest
                && part.LocalAnesthesia > 0 && part.ClinicalProcedures.Contains(treatment),
            AdvancedTreatmentType.SourceControl or AdvancedTreatmentType.OrganRepair => part.IsSurgicalOpen
                && part.LocalAnesthesia > 0 && part.ClinicalProcedures.Contains(treatment),
            AdvancedTreatmentType.Bandage or AdvancedTreatmentType.PressureBandage
                or AdvancedTreatmentType.Hemostatic => external,
            AdvancedTreatmentType.Tourniquet => part.Slot.IsLimb() && external && !part.HasTourniquet,
            AdvancedTreatmentType.Splint => part.Slot.IsLimb() && !part.IsSplinted
                && part.Wounds.Any(wound => wound.Type == WoundType.Fracture),
            AdvancedTreatmentType.Suture => !part.IsSurgicalOpen && !dirty && part.Wounds.Any(wound =>
                wound.Type is WoundType.Cut or WoundType.Puncture or WoundType.Gunshot or WoundType.Shrapnel
                && !wound.HasForeignBody && !wound.IsSutured && wound.InternalBleedingRate <= 0.001f),
            AdvancedTreatmentType.ForeignBodyRemoval => part.ForeignBodyCount > 0
                || part.Wounds.Any(wound => wound.HasForeignBody),
            AdvancedTreatmentType.WoundCleaning => dirty || !part.IsSurgicalSitePrepared && NeedsSurgicalAccess(part),
            AdvancedTreatmentType.LocalAnesthesia => part.LocalAnesthesia <= 0f
                && (part.IsSurgicalOpen || NeedsSurgicalAccess(part)
                    || part.ClinicalProcedures.Contains(AdvancedTreatmentType.ThoracicDrain)),
            AdvancedTreatmentType.SurgicalAccess => !part.IsSurgicalOpen && part.IsSurgicalSitePrepared
                && part.LocalAnesthesia > 0f && !dirty && NeedsSurgicalAccess(part),
            AdvancedTreatmentType.InternalHemostasis => part.IsSurgicalOpen && part.LocalAnesthesia > 0f
                && part.Wounds.Any(wound => wound.InternalBleedingRate > 0.001f),
            AdvancedTreatmentType.BoneFixation => part.Slot.IsLimb() && part.IsSurgicalOpen
                && part.LocalAnesthesia > 0f && !dirty
                && !part.Wounds.Any(wound => wound.InternalBleedingRate > 0.001f)
                && part.Wounds.Any(wound => wound.Type == WoundType.Fracture && !wound.IsBoneFixed),
            AdvancedTreatmentType.SurgicalClosure => part.IsSurgicalOpen && !dirty
                && part.LocalAnesthesia > 0f && !NeedsSurgicalAccess(part),
            AdvancedTreatmentType.TourniquetRemoval => part.HasTourniquet,
            _ => false,
        };
    }
}
