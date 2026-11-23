using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>Clinical effects sample the real bloodstream; standard metabolism owns removal.</summary>
[Prototype]
public sealed partial class ClinicalMedicationPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public string Reagent = default!;
    [DataField] public MedicationClass Class;
    [DataField] public float EffectiveQuantity = 2f;
    [DataField] public float OverdoseQuantity = 15f;
    [DataField] public float Toxicity = 0.2f;
    [DataField] public float Analgesia;
    [DataField] public float Sedation;
    [DataField] public float RespiratoryDepression;
    [DataField] public float PulseDrive;
    [DataField] public float Antipyretic;
    [DataField] public float GlucosePerSecond;
    [DataField] public float BleedingRisk;
}

public sealed class ClinicalMedicationEffects
{
    public readonly Dictionary<MedicationClass, float> Activity = new();
    public float Analgesia, Sedation, RespiratoryDepression, PulseDrive, Antipyretic, Glucose, Toxicity, BleedingRisk;
}

public static class ClinicalMedicationMath
{
    public static ClinicalMedicationEffects Calculate(IEnumerable<(ClinicalMedicationPrototype Drug, float Quantity)> doses)
        => Calculate(doses.Select(dose => (dose.Drug, dose.Quantity, dose.Quantity)));

    public static ClinicalMedicationEffects Calculate(IEnumerable<(ClinicalMedicationPrototype Drug, float Quantity, float EffectiveQuantity)> doses)
    {
        var result = new ClinicalMedicationEffects();
        var opioidDepression = 0f;
        var opioidSedation = 0f;
        var opioidAnalgesia = 0f;
        foreach (var (drug, quantity, effectiveQuantity) in doses)
        {
            if (!float.IsFinite(quantity) || !float.IsFinite(effectiveQuantity) || quantity <= 0f || drug.EffectiveQuantity <= 0f)
                continue;
            var activity = Math.Clamp(effectiveQuantity / drug.EffectiveQuantity, 0f, 1f);
            var overdose = Math.Clamp(quantity / Math.Max(drug.EffectiveQuantity, drug.OverdoseQuantity) - 1f, 0f, 4f);
            result.Activity[drug.Class] = Math.Max(result.Activity.GetValueOrDefault(drug.Class), activity);
            result.Toxicity += overdose * drug.Toxicity;
            result.PulseDrive += activity * drug.PulseDrive;
            result.Antipyretic += activity * drug.Antipyretic;
            result.Glucose += activity * drug.GlucosePerSecond;
            result.BleedingRisk += activity * drug.BleedingRisk;
            var depression = drug.RespiratoryDepression * (activity + overdose * 2f);
            var sedation = drug.Sedation * (activity + overdose);
            if (drug.Class == MedicationClass.Opioid)
            {
                opioidDepression += depression;
                opioidSedation += sedation;
                opioidAnalgesia += drug.Analgesia * activity;
            }
            else
            {
                result.RespiratoryDepression += depression;
                result.Sedation += sedation;
                result.Analgesia += drug.Analgesia * activity;
            }
        }
        var antagonist = result.Activity.GetValueOrDefault(MedicationClass.Naloxone);
        var synergy = result.Activity.GetValueOrDefault(MedicationClass.Sedative) > 0f ? 1.5f : 1f;
        result.RespiratoryDepression = Math.Clamp(result.RespiratoryDepression
            + opioidDepression * (1f - antagonist) * synergy, 0f, 0.98f);
        result.Sedation = Math.Clamp(result.Sedation + opioidSedation * (1f - antagonist), 0f, 0.95f);
        result.Analgesia = Math.Clamp(result.Analgesia + opioidAnalgesia * (1f - antagonist), 0f, 0.9f);
        return result;
    }
}
