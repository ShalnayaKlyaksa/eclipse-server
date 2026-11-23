namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>
/// Game-scale human laboratory observations derived from the existing physiological state.
/// These are correlated readouts of that model, not additional independent organ or blood pools.
/// </summary>
public static class ClinicalLaboratoryModel
{
    /// <summary>Reference intervals for the game's human calibration, not species-independent clinical advice.</summary>
    public static readonly IReadOnlyDictionary<string, (float Low, float High)> Reference = new Dictionary<string, (float, float)>
    {
        ["hemoglobin"] = (120,180), ["hematocrit"] = (36,54), ["erythrocytes"] = (4,6),
        ["leukocytes"] = (4,11), ["platelets"] = (150,400), ["crp"] = (0,5),
        ["creatinine"] = (44,110), ["urea"] = (2.5f,8), ["alt"] = (0,45), ["ast"] = (0,40),
        ["bilirubin"] = (0,21), ["protein"] = (64,83), ["sodium"] = (135,145),
        ["potassium"] = (3.5f,5.1f), ["lactate"] = (0.5f,2.2f),
    };
    public static Dictionary<string, float> Calculate(AdvancedHealthComponent health, float kidneys, float liver,
        float dehydration, float inflammation)
    {
        kidneys = Math.Clamp(kidneys, 0, 1);
        liver = Math.Clamp(liver, 0, 1);
        dehydration = Math.Clamp(dehydration, 0, 1);
        inflammation = Math.Clamp(inflammation, 0, 1);
        var concentration = 1 + dehydration * 0.15f;
        var redCells = Math.Clamp(health.OxygenCarryingCapacity, 0, 1) * concentration;
        var renalStrain = (1 - kidneys) / Math.Max(0.15f, kidneys);
        var hepaticStrain = 1 - liver;
        var hypoperfusion = 1 - Math.Clamp(health.Perfusion, 0, 1);
        var injury = Math.Clamp(health.TraumaLoad / 100, 0, 1);
        return new()
        {
            ["hemoglobin"] = 150 * redCells,
            ["hematocrit"] = 45 * redCells,
            ["erythrocytes"] = 5 * redCells,
            ["leukocytes"] = (6.5f + 14 * inflammation + 3 * injury) * concentration,
            ["platelets"] = Math.Clamp(250 * (0.4f + liver * 0.6f) * (1 - health.TransfusionReaction * 0.35f), 30, 450) * concentration,
            ["crp"] = 2 + 100 * inflammation + 30 * injury,
            ["creatinine"] = (85 + 90 * renalStrain) * concentration,
            ["urea"] = (5 + 6 * renalStrain) * concentration,
            ["alt"] = 25 + 300 * hepaticStrain,
            ["ast"] = 22 + 220 * hepaticStrain + 80 * injury,
            ["bilirubin"] = 12 + 80 * hepaticStrain + 45 * health.TransfusionReaction,
            ["protein"] = 72 * (0.7f + 0.3f * liver) * concentration,
            ["sodium"] = 140 + 12 * dehydration,
            ["potassium"] = 4.2f + 2.5f * (1 - kidneys) + health.TransfusionReaction,
            ["lactate"] = 1 + 7 * hypoperfusion + 3 * injury,
        };
    }
}
