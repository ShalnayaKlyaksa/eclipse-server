using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.MedicalChemistry;

/// <summary>Bound impurities travel with the drug through transfers, pills and injections.</summary>
[Serializable, NetSerializable]
public sealed partial class MedicalBatchData : ReagentData
{
    [DataField]
    public int Purity = 100;

    [DataField]
    public string? PatientDna;

    public override ReagentData Clone() => new MedicalBatchData { Purity = Purity, PatientDna = PatientDna };
    public override bool Equals(ReagentData? other) => other is MedicalBatchData batch && batch.Purity == Purity && batch.PatientDna == PatientDna;
    public override int GetHashCode() => HashCode.Combine(Purity, PatientDna);
}

[Serializable, NetSerializable]
public sealed partial class MedicalSampleAnalysisData : ReagentData
{
    [DataField] public string Dna = string.Empty;
    public override ReagentData Clone() => new MedicalSampleAnalysisData { Dna = Dna };
    public override bool Equals(ReagentData? other) => other is MedicalSampleAnalysisData analysis && analysis.Dna == Dna;
    public override int GetHashCode() => HashCode.Combine(typeof(MedicalSampleAnalysisData), Dna);
}

public static class MedicalBatch
{
    public static bool IsBatch(ReagentId reagent) => reagent.Data?.Any(data => data is MedicalBatchData) == true;

    // Existing supplies and ordinary, untagged reagents retain their original potency.
    public static float GetPurity(ReagentId reagent) =>
        Math.Clamp(reagent.Data?.OfType<MedicalBatchData>().FirstOrDefault()?.Purity ?? 100, 0, 100) / 100f;

    public static string? GetPatientDna(ReagentId reagent) => reagent.Data?.OfType<MedicalBatchData>().FirstOrDefault()?.PatientDna;

    public static float Affinity(ReagentId reagent, string? patientDna) =>
        GetPatientDna(reagent) is not { Length: > 0 } profile ? 1f : profile == patientDna ? 1.15f : 0.65f;

    public static float GetActivity(Solution solution, string prototype, string? patientDna) => solution.Contents
        .Where(reagent => reagent.Reagent.Prototype == prototype)
        .Sum(reagent => reagent.Quantity.Float() * GetPurity(reagent.Reagent) * Affinity(reagent.Reagent, patientDna));

    public static float GetMismatch(Solution solution, string prototype, string? patientDna)
    {
        var volume = solution.GetTotalPrototypeQuantity(prototype).Float();
        return volume <= 0 ? 0f : solution.Contents
            .Where(reagent => reagent.Reagent.Prototype == prototype && Affinity(reagent.Reagent, patientDna) < 1f)
            .Sum(reagent => reagent.Quantity.Float()) / volume;
    }

    public static bool TryGetPatientProfile(Solution solution, string prototype, out string? profile)
    {
        var profiles = solution.Contents.Where(reagent => reagent.Reagent.Prototype == prototype)
            .Select(reagent => GetPatientDna(reagent.Reagent)).Distinct().ToArray();
        profile = profiles.Length == 1 ? profiles[0] : null;
        return profiles.Length <= 1;
    }

    public static string? GetSampleDna(Solution solution, bool requireAssayed)
    {
        var dna = solution.Contents.SelectMany(reagent => reagent.Reagent.Data ?? [])
            .OfType<DnaData>().Select(data => data.DNA).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToArray();
        if (dna.Length != 1)
            return null;
        if (requireAssayed && !solution.Contents.Any(reagent => reagent.Quantity >= 1
            && reagent.Reagent.Data?.OfType<MedicalSampleAnalysisData>().Any(data => data.Dna == dna[0]) == true))
            return null;
        return dna[0];
    }

    public static string? Assay(Solution solution)
    {
        var profile = GetSampleDna(solution, false);
        if (profile == null)
            return null;
        foreach (var reagent in solution.Contents.ToArray())
        {
            if (reagent.Reagent.Data?.OfType<DnaData>().Any(data => data.DNA == profile) != true)
                continue;
            var data = reagent.Reagent.Data.Select(item => item.Clone()).Where(item => item is not MedicalSampleAnalysisData).ToList();
            data.Add(new MedicalSampleAnalysisData { Dna = profile });
            solution.RemoveReagent(reagent);
            solution.AddReagent(new ReagentId(reagent.Reagent.Prototype, data), reagent.Quantity);
        }
        return profile;
    }

    public static string ProfileCode(string dna) => dna[..Math.Min(8, dna.Length)];

    public static float GetPurity(Solution solution, string prototype)
        => GetPurity(solution.Contents, prototype);

    public static float GetPurity(IEnumerable<ReagentQuantity> contents, string prototype)
    {
        var volume = 0f;
        var active = 0f;
        foreach (var reagent in contents)
        {
            if (reagent.Reagent.Prototype != prototype)
                continue;
            volume += reagent.Quantity.Float();
            active += reagent.Quantity.Float() * GetPurity(reagent.Reagent);
        }
        return volume <= 0 ? 1f : active / volume;
    }

    /// <summary>Consume every grade proportionally, so blending cannot multiply metabolic effects.</summary>
    public static Solution Take(Solution solution, string prototype, FixedPoint2 amount) =>
        solution.SplitSolutionWithOnly(amount, prototype);
}
