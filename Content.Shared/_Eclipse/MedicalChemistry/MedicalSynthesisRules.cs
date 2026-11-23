namespace Content.Shared._Eclipse.MedicalChemistry;

/// <summary>Deterministic reactor rules shared with the instrument's yield preview.</summary>
public static class MedicalSynthesisRules
{
    public const int MaxBatchVolume = 60;
    public const string ReactorSolution = "medicalReactor";
    public const string SampleSlot = "medicalSampleSlot";
    public const float AssayDuration = 10f;

    public static float Duration(MedicalSynthesisProtocol protocol) => protocol switch
    {
        MedicalSynthesisProtocol.Rapid => 12f,
        MedicalSynthesisProtocol.Precision => 28f,
        MedicalSynthesisProtocol.Purify => 20f,
        MedicalSynthesisProtocol.Personalize => 18f,
        _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
    };

    public static (int Purity, float Yield) Result(MedicalSynthesisProtocol protocol, float stress, float inputPurity = 1f)
    {
        stress = Math.Clamp(stress, 0f, 1f);
        if (protocol == MedicalSynthesisProtocol.Personalize)
            return ((int) MathF.Floor(Math.Clamp(inputPurity - stress * 0.2f, 0f, 1f) * 100 + 0.0001f), 0.95f);
        var purity = (int) MathF.Round(100f * Math.Clamp(
            (protocol == MedicalSynthesisProtocol.Rapid ? 0.80f : 0.99f) - stress * 0.45f, 0.35f, 0.99f));
        if (protocol != MedicalSynthesisProtocol.Purify)
            purity = Math.Max(0, (int) MathF.Round(purity * Math.Clamp(inputPurity, 0f, 1f)));
        var yield = protocol switch
        {
            MedicalSynthesisProtocol.Rapid => 1f,
            MedicalSynthesisProtocol.Precision => 0.9f,
            // Purification removes bound impurities; it never creates active medicine.
            MedicalSynthesisProtocol.Purify => Math.Min(1f, Math.Clamp(inputPurity, 0f, 1f) / (purity / 100f)) * 0.95f,
            _ => throw new ArgumentOutOfRangeException(nameof(protocol)),
        };
        return (purity, yield);
    }

    public static (float Temperature, float Progress, float Stress) Step(
        MedicalSynthesisPrototype recipe, MedicalSynthesisProtocol protocol, MedicalReactorHeat heat,
        float temperature, float progress, float stress, float seconds)
    {
        var target = (recipe.MinTemperature + recipe.MaxTemperature) / 2f;
        temperature = heat switch
        {
            MedicalReactorHeat.Automatic => temperature + Math.Clamp(target - temperature, -4f * seconds, 4f * seconds),
            MedicalReactorHeat.Boost => temperature + 8f * seconds,
            MedicalReactorHeat.Cool => temperature - 6f * seconds,
            _ => temperature,
        };
        temperature = Math.Clamp(temperature, 273.15f, 473.15f);
        var duration = Duration(protocol);
        if (temperature >= recipe.MinTemperature)
        {
            var overheated = temperature > recipe.MaxTemperature;
            progress += seconds / duration * (overheated ? 0.25f : 1f);
            if (overheated)
                stress += seconds / duration * (temperature - recipe.MaxTemperature) / 20f;
        }
        return (temperature, Math.Clamp(progress, 0f, 1f), Math.Clamp(stress, 0f, 1f));
    }
}
