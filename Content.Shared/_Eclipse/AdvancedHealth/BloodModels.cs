using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Content.Shared.DoAfter;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Chemistry.Components;
using Robust.Shared.Localization;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>ABO blood group.</summary>
[Serializable, NetSerializable]
public enum AboGroup : byte
{
    O,
    A,
    B,
    AB,
}

/// <summary>What a transfusable pack delivers.</summary>
[Serializable, NetSerializable]
public enum BloodProductType : byte
{
    /// <summary>Whole blood contains both cells and plasma; human ABO groups must match.</summary>
    WholeBlood,
    /// <summary>Packed red cells use donor-cell ABO/Rh compatibility and denser oxygen carriers.</summary>
    PackedCells,
    /// <summary>Ringer's lactate — universal crystalloid, restores volume but carries no oxygen (dilutes).</summary>
    Ringers,
    /// <summary>Saline (0.9% NaCl) — universal crystalloid, restores volume but carries no oxygen (dilutes).</summary>
    Saline,
}

public static class BloodCompatibility
{
    /// <summary>The species blood-type label is authoritative. Nonhuman types are not ABO groups.</summary>
    public static bool TryParse(string type, out AboGroup group, out bool rh)
    {
        rh = type.EndsWith('+');
        group = AboGroup.O;
        if (type.Length < 2 || type[^1] is not ('+' or '-')) return false;
        switch (type[..^1])
        {
            case "O": group = AboGroup.O; return true;
            case "A": group = AboGroup.A; return true;
            case "B": group = AboGroup.B; return true;
            case "AB": group = AboGroup.AB; return true;
            default: return false;
        }
    }

    public static float? OxygenFactor(IPrototypeManager prototypes, string recipientFluid, string recipientType,
        BloodProductComponent donor)
    {
        if (donor.ProductType is BloodProductType.Saline or BloodProductType.Ringers) return 0f;
        if (recipientFluid != donor.BodyFluid) return null;
        if (TryParse(recipientType, out var recipient, out var rh)
            && TryParse(donor.BloodType, out var group, out var donorRh))
        {
            // Whole blood also contains donor plasma: ordinary group O whole blood is not universal.
            if (donor.ProductType == BloodProductType.WholeBlood && recipient != group) return null;
            return IsCompatible(recipient, rh, group, donorRh) ? 1f : null;
        }
        if (!prototypes.TryIndex<AdvancedHealthBloodCompatibilityPrototype>(recipientType, out var compatibility))
            return recipientType == donor.BloodType ? 1f : null;
        if (compatibility.CompatibleDonors.Contains(donor.BloodType)) return 1f;
        return compatibility.EmergencyDonors.Contains(donor.BloodType) ? compatibility.EmergencyOxygenCarryFactor : null;
    }
    /// <summary>Whether a recipient can safely receive red cells from a donor of the given group/Rh.</summary>
    public static bool IsCompatible(AboGroup recipient, bool recipientRh, AboGroup donor, bool donorRh)
    {
        // An Rh- recipient reacts to Rh+ red cells.
        if (!recipientRh && donorRh)
            return false;

        // Recipient plasma antibodies attack antigens the recipient lacks.
        return recipient switch
        {
            AboGroup.O => donor == AboGroup.O,
            AboGroup.A => donor is AboGroup.O or AboGroup.A,
            AboGroup.B => donor is AboGroup.O or AboGroup.B,
            AboGroup.AB => true,
            _ => false,
        };
    }

    /// <summary>Human-readable group label, e.g. "0(I) Rh+".</summary>
    public static string Format(AboGroup group, bool rh)
    {
        var abo = group switch
        {
            AboGroup.O => "0(I)",
            AboGroup.A => "A(II)",
            AboGroup.B => "B(III)",
            AboGroup.AB => "AB(IV)",
            _ => "?",
        };
        return $"{abo} Rh{(rh ? "+" : "-")}";
    }

    public static string Format(string fluid, string type)
    {
        if (string.IsNullOrWhiteSpace(fluid)) return type;
        var label = Loc.TryGetString($"clinical-fluid-{fluid.ToLowerInvariant()}", out var localized) ? localized : fluid;
        return $"{label}: {type}";
    }
}

[Prototype]
public sealed partial class AdvancedHealthBloodCompatibilityPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField] public List<string> CompatibleDonors = new();
    [DataField] public List<string> EmergencyDonors = new();
    [DataField] public float EmergencyOxygenCarryFactor = 0.65f;
}

/// <summary>
/// A transfusable blood/fluid pack. Whole blood and packed cells must be ABO/Rh compatible with the
/// recipient; Ringer's and saline are universal but dilute oxygen-carrying capacity.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class BloodProductComponent : Component
{
    [DataField, AutoNetworkedField] public BloodProductType ProductType = BloodProductType.WholeBlood;
    [DataField, AutoNetworkedField] public AboGroup BloodGroup = AboGroup.O;
    [DataField, AutoNetworkedField] public bool RhPositive = true;
    [DataField, AutoNetworkedField] public string BodyFluid = "Blood";
    [DataField, AutoNetworkedField] public string BloodType = "O+";
    [DataField, AutoNetworkedField] public Color FluidColor = Color.Red;
    [DataField, AutoNetworkedField] public float OxygenCarryFactor = 1f;

    /// <summary>Millilitres delivered per use.</summary>
    [DataField, AutoNetworkedField] public float Volume = 500f;

    /// <summary>Uses remaining in the pack.</summary>
    [DataField, AutoNetworkedField] public int Charges = 1;
    [DataField, AutoNetworkedField] public string DonorName = "";
    [DataField, AutoNetworkedField] public string CrossmatchedIdentity = "";
    [DataField, AutoNetworkedField] public string CrossmatchedName = "";
    [DataField, AutoNetworkedField] public bool CrossmatchCompatible;
    [DataField] public List<string> BloodborneConditions = new();
}

/// <summary>A physical, identified snapshot. Pharmacy can consume this profile without reading hidden illnesses.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ClinicalBloodSampleComponent : Component
{
    [DataField, AutoNetworkedField] public string PatientIdentity = "";
    [DataField, AutoNetworkedField] public string PatientName = "";
    [DataField, AutoNetworkedField] public string BodyFluid = "";
    [DataField, AutoNetworkedField] public string BloodType = "";
    [DataField, AutoNetworkedField] public float Glucose;
    [DataField, AutoNetworkedField] public float OxygenCarryingCapacity;
    [DataField, AutoNetworkedField] public float ToxinLoad;
    [DataField, AutoNetworkedField] public float KidneyFunction;
    [DataField, AutoNetworkedField] public float LiverFunction;
    [DataField, AutoNetworkedField] public Dictionary<string, float> Medications = new();
    [DataField, AutoNetworkedField] public TimeSpan CollectedAt;
    [DataField] public EntityUid? Patient;
    /// <summary>Server-only observations at collection time, revealed by a completed laboratory analysis.</summary>
    [DataField] public Dictionary<string, float> LaboratoryReadings = new();
}

[RegisterComponent]
public sealed partial class EmptyBloodBagComponent : Component;

[Serializable, NetSerializable]
public sealed partial class ClinicalBloodDoAfterEvent : SimpleDoAfterEvent
{
    [DataField] public bool Crossmatch;
}

[Serializable, NetSerializable]
public sealed partial class ClinicalTransfusionDoAfterEvent : SimpleDoAfterEvent;

/// <summary>Blood typing follows an actual reagent through syringes and chemistry equipment.</summary>
[Serializable, NetSerializable]
public sealed partial class ClinicalBloodData : ReagentData
{
    [DataField] public string BodyFluid = "";
    [DataField] public string BloodType = "";
    [DataField] public string PatientIdentity = "";
    [DataField] public float OxygenCarryFactor = 1f;
    public override ReagentData Clone() => new ClinicalBloodData
        { BodyFluid = BodyFluid, BloodType = BloodType, PatientIdentity = PatientIdentity, OxygenCarryFactor = OxygenCarryFactor };
    public override bool Equals(ReagentData? other) => other is ClinicalBloodData blood
        && blood.BodyFluid == BodyFluid && blood.BloodType == BloodType && blood.PatientIdentity == PatientIdentity
        && blood.OxygenCarryFactor == OxygenCarryFactor;
    public override int GetHashCode() => HashCode.Combine(BodyFluid, BloodType, PatientIdentity, OxygenCarryFactor);
}

[ByRefEvent]
public readonly record struct AdvancedBloodTransferEvent(Solution Solution, bool Drawn);
