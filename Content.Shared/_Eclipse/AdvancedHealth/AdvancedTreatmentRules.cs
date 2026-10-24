using System.Linq;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>The chip and server use the same indications; an internal bleed is not a bandaging target.</summary>
public static class AdvancedTreatmentRules
{
    public static bool IsNeeded(BodyPartState part, AdvancedTreatmentType treatment)
    {
        var external = part.Wounds.Any(wound => wound.ExternalBleedingRate > 0.01f);
        return treatment switch
        {
            AdvancedTreatmentType.Bandage or AdvancedTreatmentType.PressureBandage
                or AdvancedTreatmentType.Hemostatic => external,
            AdvancedTreatmentType.Tourniquet => part.Slot.IsLimb() && external && !part.HasTourniquet,
            AdvancedTreatmentType.Splint => part.Slot.IsLimb() && !part.IsSplinted
                && part.Wounds.Any(wound => wound.Type == WoundType.Fracture),
            AdvancedTreatmentType.Suture => part.Wounds.Any(wound =>
                wound.Type is WoundType.Cut or WoundType.Puncture or WoundType.Gunshot or WoundType.Shrapnel
                && !wound.HasForeignBody && !wound.IsSutured),
            AdvancedTreatmentType.ForeignBodyRemoval => part.ForeignBodyCount > 0
                || part.Wounds.Any(wound => wound.HasForeignBody),
            _ => false,
        };
    }
}
