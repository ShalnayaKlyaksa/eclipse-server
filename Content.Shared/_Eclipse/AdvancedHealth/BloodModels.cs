using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

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
    /// <summary>Whole blood — restores volume and oxygen-carrying capacity, but must be ABO/Rh compatible.</summary>
    WholeBlood,
    /// <summary>Packed red cells — same compatibility rules, denser oxygen carriers.</summary>
    PackedCells,
    /// <summary>Ringer's lactate — universal crystalloid, restores volume but carries no oxygen (dilutes).</summary>
    Ringers,
    /// <summary>Saline (0.9% NaCl) — universal crystalloid, restores volume but carries no oxygen (dilutes).</summary>
    Saline,
}

public static class BloodCompatibility
{
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
        return string.IsNullOrWhiteSpace(fluid) ? type : $"{fluid}: {type}";
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
}
