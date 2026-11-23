using Content.Client.HealthAnalyzer.UI;
using System.Globalization;
using Content.Shared._Eclipse.AdvancedHealth;
using Robust.Shared.Prototypes;

namespace Content.Client._Eclipse.AdvancedHealth;

public sealed partial class AdvancedHealthStatusWindow
{
    private void AppendDiagnosticReports()
    {
        if (_viewMode != MedicalBodyViewMode.General) return;
        if (_entities.TryGetComponent<MedicalDiagnosticHistoryComponent>(_target, out var history))
            foreach (var report in history.Reports)
            {
                if (_selectedSlot is not { } selected || _bodyDoll.DisplaySlot(report.Slot) != _bodyDoll.DisplaySlot(selected)) continue;
                var details = new List<string>
                {
                    Loc.GetString("diagnostic-report-time", ("time", report.CollectedAt.ToString(@"hh\:mm\:ss"))),
                    Loc.GetString("advanced-health-part-" + report.Slot.ToString().ToLowerInvariant()),
                };
                foreach (var (key, value) in report.Readings)
                    details.Add(Loc.GetString("diagnostic-reading-" + key, ("value", value.ToString("0.#", CultureInfo.InvariantCulture))));
                foreach (var finding in report.Findings) details.Add(Loc.GetString(finding));
                _conditionRows.Add(($"diagnostic-{report.Kind}-{report.Slot}",
                    Loc.GetString("diagnostic-report-title-" + report.Kind.ToString().ToLowerInvariant()),
                    string.Join("\n", details), HealthMenuStyle.AmberBright));
            }
    }

    private void AppendClinicalConditions(AdvancedHealthComponent health)
    {
        if (health.HasBlood)
        {
            var group = BloodCompatibility.Format(health.BodyFluid, health.BloodType);
            _conditionRows.Add(("clinical-blood-type", Loc.GetString("clinical-blood-type", ("group", group)),
                Loc.GetString(health.BloodTypeConfirmed ? "clinical-blood-type-confirmed" : "clinical-blood-type-unconfirmed"), HealthMenuStyle.AmberBright));
            if (_entities.System<AdvancedHealthClientSystem>().TryGetHeldBloodProduct(_equipmentOwner, out var pack)
                && _entities.TryGetComponent<BloodProductComponent>(pack, out var product))
            {
                var compatible = BloodCompatibility.OxygenFactor(IoCManager.Resolve<IPrototypeManager>(), health.BodyFluid, health.BloodType, product) != null;
                var matched = product.CrossmatchedIdentity == health.MedicalIdentity && product.CrossmatchCompatible;
                _conditionRows.Add(("clinical-blood-compatibility", Loc.GetString(compatible ? "clinical-pack-compatible" : "clinical-pack-incompatible"),
                    Loc.GetString(matched ? "clinical-pack-crossmatched" : "clinical-pack-not-crossmatched"), compatible ? HealthMenuStyle.AmberBright : HealthMenuStyle.Red));
            }
        }
        if (health.TransfusionReaction > 0)
            Condition("clinical-transfusion-reaction", "clinical-transfusion-reaction", "clinical-transfusion-reaction-desc", HealthMenuStyle.Red);
        if (health.Sedation > 0.15f)
            Condition("clinical-sedation", "clinical-sedation", "clinical-sedation-desc", HealthMenuStyle.Amber);
        if (health.RespiratoryDepression > 0.3f)
            Condition("clinical-depression", "clinical-depression", "clinical-depression-desc", HealthMenuStyle.Red);
        if (!_entities.TryGetComponent<AdvancedDiseaseComponent>(_target, out var diseases)) return;
        if (diseases.Symptoms != ClinicalSymptoms.None)
            _conditionRows.Add(("clinical-symptoms", Loc.GetString("clinical-symptoms"),
                Loc.GetString("clinical-symptoms-desc"), HealthMenuStyle.Amber));
        foreach (var symptom in new[] { ClinicalSymptoms.Nausea, ClinicalSymptoms.Vomiting, ClinicalSymptoms.Hematemesis })
            if ((diseases.Symptoms & symptom) != 0)
                Condition("clinical-" + symptom.ToString().ToLowerInvariant(), "clinical-" + symptom.ToString().ToLowerInvariant(),
                    "clinical-" + symptom.ToString().ToLowerInvariant() + "-desc", symptom == ClinicalSymptoms.Hematemesis ? HealthMenuStyle.Red : HealthMenuStyle.Amber);
        if (diseases.CompletedTests.Contains(ClinicalTest.Blood))
            _conditionRows.Add(("clinical-glucose", Loc.GetString("clinical-glucose", ("value", MathF.Round(health.BloodGlucose, 1))),
                Loc.GetString("clinical-glucose-desc"), health.BloodGlucose < 3 || health.BloodGlucose > 16
                    ? HealthMenuStyle.Red : HealthMenuStyle.AmberBright));
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        foreach (var (id, severity) in diseases.Known)
        {
            if (!prototypes.TryIndex<AdvancedDiseasePrototype>(id, out var disease)) continue;
            var stage = severity >= 0.85f ? "critical" : severity >= 0.6f ? "severe" : severity >= 0.3f ? "moderate" : "minor";
            _conditionRows.Add(($"clinical-{id}", Loc.GetString(disease.Name) + " · "
                    + Loc.GetString($"advanced-health-severity-{stage}"), Loc.GetString(disease.Description),
                severity >= 0.6f ? HealthMenuStyle.Red : HealthMenuStyle.Amber));
        }
    }
}
