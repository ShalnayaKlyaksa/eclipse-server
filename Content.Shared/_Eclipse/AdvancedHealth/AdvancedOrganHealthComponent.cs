using Robust.Shared.GameStates;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>Clinical state belongs to the actual organ and travels with a transplant.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AdvancedOrganHealthComponent : Component
{
    [DataField, AutoNetworkedField] public BodyPartSlot Region;
    [DataField, AutoNetworkedField] public float Function = 1f;
    [DataField, AutoNetworkedField] public float? ExaminedIntegrity;
    [DataField, AutoNetworkedField] public List<MedicalCondition> KnownConditions = new();

    // Authoritative secrets: never included in the generated network state.
    [DataField] public float Integrity = 100f;
    [DataField] public bool SurgicallyStabilized;
    [DataField] public List<MedicalCondition> Conditions = new();
    [DataField] public HashSet<string> Discovered = new();
}

public static class AdvancedOrganAnatomy
{
    public static BodyPartSlot? Region(string category) => category switch
    {
        "Brain" or "Eyes" or "Ears" or "Tongue" => BodyPartSlot.Head,
        "Heart" or "Lungs" => BodyPartSlot.Chest,
        "Stomach" or "Liver" or "Kidneys" or "Appendix" or "Intestines"
            or "Uterus" or "OvaryLeft" or "OvaryRight" or "VaginalCanal" or "Prostate" => BodyPartSlot.Abdomen,
        _ => null,
    };
}
