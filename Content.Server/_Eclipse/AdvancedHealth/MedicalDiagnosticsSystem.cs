using System.Linq;
using System.Globalization;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.ActionBlocker;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Paper;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Popups;
using Robust.Shared.Timing;
using Robust.Shared.Player;
using Robust.Shared.Utility;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.AdvancedHealth;

/// <summary>Physical samples and completed examinations produce dated, immutable observations.</summary>
public sealed class MedicalDiagnosticsSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly AdvancedBloodSystem _blood = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _actions = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ClinicalBloodAnalyzerComponent, AfterInteractEvent>(OnUseAnalyzer);
        SubscribeLocalEvent<ClinicalBloodSampleComponent, ClinicalAnalysisDoAfterEvent>(OnAnalysisComplete);
    }

    private bool CanAnalyze(EntityUid user, EntityUid analyzer, EntityUid tube)
        => Exists(analyzer) && Exists(tube) && _hands.IsHolding(user, analyzer)
            && HasComp<ClinicalBloodAnalyzerComponent>(analyzer)
            && _actions.CanConsciouslyPerformAction(user) && _actions.CanInteract(user, tube)
            && _interaction.InRangeUnobstructed(user, tube)
            && TryComp<ClinicalBloodSampleComponent>(tube, out var sample) && _blood.IsFresh(sample);

    private void OnUseAnalyzer(Entity<ClinicalBloodAnalyzerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } tube) return;
        if (!CanAnalyze(args.User, ent, tube))
        {
            if (HasComp<ClinicalBloodSampleComponent>(tube))
                _popup.PopupEntity(Loc.GetString("diagnostic-sample-invalid"), tube, args.User);
            return;
        }
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay,
            new ClinicalAnalysisDoAfterEvent(), tube, target: tube, used: ent)
        { NeedHand = true, BreakOnMove = true, BreakOnDamage = true });
    }

    private void OnAnalysisComplete(Entity<ClinicalBloodSampleComponent> ent, ref ClinicalAnalysisDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target != ent.Owner || args.Used is not { } analyzer
            || !CanAnalyze(args.User, analyzer, ent)) return;
        args.Handled = AnalyzeSample(ent.Owner, args.User);
    }

    /// <summary>Uses the contents-validated sample snapshot, including after the patient's death.</summary>
    public bool AnalyzeSample(EntityUid tube, EntityUid? user = null)
    {
        if (!TryComp<ClinicalBloodSampleComponent>(tube, out var sample) || !_blood.IsFresh(sample)) return false;
        var report = new MedicalDiagnosticReport
        {
            Kind = MedicalDiagnosticKind.Blood, Slot = BodyPartSlot.Chest,
            CollectedAt = sample.CollectedAt, PatientName = sample.PatientName, PatientIdentity = sample.PatientIdentity,
            BloodGroup = sample.BloodType, BodyFluid = sample.BodyFluid,
            Medications = new(sample.Medications),
            Readings = new()
            {
                ["glucose"] = sample.Glucose,
                ["oxygen-capacity"] = sample.OxygenCarryingCapacity * 100,
                ["toxins"] = sample.ToxinLoad,
                ["kidneys"] = sample.KidneyFunction * 100,
                ["liver"] = sample.LiverFunction * 100,
            },
        };
        foreach (var (marker, value) in sample.LaboratoryReadings) report.Readings[marker] = value;
        report.Findings.Add(sample.LaboratoryReadings.Count > 0 ? "diagnostic-human-laboratory" : "diagnostic-no-laboratory-reference");
        report.Findings.Add("diagnostic-blood-proxies");
        // Never attach a report to a different body just because its name matches the label.
        if (sample.Patient is { } patient && Exists(patient)
            && TryComp<AdvancedHealthComponent>(patient, out var health) && health.MedicalIdentity == sample.PatientIdentity)
            Remember(patient, report);
        Print(tube, report);
        Show(user, report);
        return true;
    }

    public void RecordExamination(EntityUid patient, BodyPartSlot slot, AdvancedTreatmentType treatment, EntityUid? user = null)
    {
        if (treatment is not (AdvancedTreatmentType.XRay or AdvancedTreatmentType.Electrocardiogram)
            || !TryComp<AdvancedHealthComponent>(patient, out var health)
            || !health.BodyParts.TryGetValue(slot, out var part)) return;
        var report = new MedicalDiagnosticReport
        {
            Kind = treatment == AdvancedTreatmentType.XRay ? MedicalDiagnosticKind.XRay : MedicalDiagnosticKind.Ecg,
            Slot = slot, CollectedAt = _timing.CurTime,
            PatientName = Name(patient), PatientIdentity = health.MedicalIdentity,
        };
        if (report.Kind == MedicalDiagnosticKind.XRay)
        {
            if (part.Wounds.Any(w => w.Type == WoundType.Fracture)) report.Findings.Add("diagnostic-fracture");
            if (part.ForeignBodyCount > 0 || part.Wounds.Any(w => w.HasForeignBody)) report.Findings.Add("diagnostic-foreign-body");
            if (part.Wounds.Any(w => w.IsBoneFixed)) report.Findings.Add("diagnostic-fixation");
            if (report.Findings.Count == 0) report.Findings.Add("diagnostic-xray-clear");
            report.Findings.Add("diagnostic-xray-limits");
        }
        else
        {
            report.Readings["heart-rate"] = health.IsHeartStopped ? 0 : health.HeartRate;
            report.Findings.Add(health.IsHeartStopped ? "diagnostic-cardiac-arrest"
                : health.HeartRate > health.RestingHeartRate * 1.5f ? "diagnostic-fast-rate"
                : health.HeartRate < health.RestingHeartRate * 0.75f ? "diagnostic-slow-rate" : "diagnostic-regular-rate");
            report.Findings.Add("diagnostic-ecg-limits");
        }
        Remember(patient, report);
        Print(patient, report);
        Show(user, report);
    }

    private void Show(EntityUid? user, MedicalDiagnosticReport report)
    {
        if (user is { } doctor && TryComp<ActorComponent>(doctor, out var actor))
            RaiseNetworkEvent(new MedicalDiagnosticResultEvent(report), Filter.SinglePlayer(actor.PlayerSession));
    }

    private void Remember(EntityUid patient, MedicalDiagnosticReport report)
    {
        var history = EnsureComp<MedicalDiagnosticHistoryComponent>(patient);
        // A bounded history: latest result per method and region. Older samples cannot replace newer ones.
        if (history.Reports.Any(r => r.Kind == report.Kind && r.Slot == report.Slot && r.CollectedAt > report.CollectedAt)) return;
        history.Reports.RemoveAll(r => r.Kind == report.Kind && r.Slot == report.Slot);
        history.Reports.Add(report);
        Dirty(patient, history);
    }

    private void Print(EntityUid source, MedicalDiagnosticReport report)
    {
        var lines = new List<string>
        {
            Loc.GetString("diagnostic-report-title-" + report.Kind.ToString().ToLowerInvariant()),
            Loc.GetString("diagnostic-report-patient", ("patient", FormattedMessage.EscapeText(report.PatientName))),
            Loc.GetString("diagnostic-report-time", ("time", report.CollectedAt.ToString(@"hh\:mm\:ss"))),
            Loc.GetString("advanced-health-part-" + report.Slot.ToString().ToLowerInvariant()),
        };
        foreach (var (key, value) in report.Readings)
            lines.Add(Loc.GetString("diagnostic-reading-" + key, ("value", value.ToString("0.#", CultureInfo.InvariantCulture))));
        if (report.Kind == MedicalDiagnosticKind.Blood)
        {
            lines.Add(Loc.GetString("diagnostic-blood-group", ("group", report.BloodGroup), ("fluid", report.BodyFluid)));
            foreach (var (drug, quantity) in report.Medications)
                lines.Add(Loc.GetString("diagnostic-medication", ("drug", _prototypes.Index<ReagentPrototype>(drug).LocalizedName),
                    ("value", quantity.ToString("0.##", CultureInfo.InvariantCulture))));
        }
        foreach (var finding in report.Findings) lines.Add(Loc.GetString(finding));
        var paper = Spawn("Paper", Transform(source).Coordinates);
        _paper.SetContent(paper, string.Join("\n", lines));
    }
}
