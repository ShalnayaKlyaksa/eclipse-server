using System.Linq;
using System.Globalization;
using Content.Server.Medical.Components;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Paper;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Verbs;
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
    [Dependency] private readonly DamageableSystem _damage = default!;

    internal const float XRayDuration = 7f;
    private static readonly ProtoId<DamageTypePrototype> Radiation = "Radiation";

    public override void Initialize()
    {
        SubscribeLocalEvent<ClinicalBloodAnalyzerComponent, InteractUsingEvent>(OnUseAnalyzer);
        SubscribeLocalEvent<ClinicalBloodAnalyzerComponent, AnchorStateChangedEvent>(OnAnalyzerUnanchored);
        SubscribeLocalEvent<ClinicalBloodAnalyzerComponent, PowerChangedEvent>(OnAnalyzerPowerChanged);
        SubscribeLocalEvent<ClinicalBloodAnalyzerComponent, ClinicalAnalysisDoAfterEvent>(OnAnalysisComplete);
        SubscribeLocalEvent<MedicalScannerComponent, GetVerbsEvent<Verb>>(OnScannerVerbs);
        SubscribeLocalEvent<MedicalScannerComponent, MedicalXRayDoAfterEvent>(OnXRayComplete);
    }

    private void OnScannerVerbs(Entity<MedicalScannerComponent> scanner, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || scanner.Comp.BodyContainer.ContainedEntity is not { } patient)
            return;

        var user = args.User;
        var slot = TryComp<AimTargetComponent>(user, out var aim) && aim.CurrentTarget != BodyPartTarget.Auto
            ? aim.CurrentTarget.ToSlot() : BodyPartSlot.Chest;
        if (!CanTakeXRay(scanner, user, patient, slot))
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("advanced-health-action-xray"),
            Act = () => TryStartXRay(scanner, user, patient, slot),
        });
    }

    internal bool TryStartXRay(Entity<MedicalScannerComponent> scanner, EntityUid user, EntityUid patient, BodyPartSlot slot)
    {
        if (!CanTakeXRay(scanner, user, patient, slot))
            return false;

        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, XRayDuration,
            new MedicalXRayDoAfterEvent { Patient = GetNetEntity(patient), Slot = slot }, scanner, target: scanner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
        });
    }

    private bool CanTakeXRay(Entity<MedicalScannerComponent> scanner, EntityUid user, EntityUid patient, BodyPartSlot slot)
        => scanner.Comp.BodyContainer.ContainedEntity == patient && Transform(scanner).Anchored
            && user != patient && this.IsPowered(scanner, EntityManager) && _actions.CanConsciouslyPerformAction(user)
            && _actions.CanInteract(user, scanner) && _interaction.InRangeUnobstructed(user, scanner.Owner)
            && TryComp<AdvancedHealthComponent>(patient, out var health) && health.BodyParts.ContainsKey(slot);

    private void OnXRayComplete(Entity<MedicalScannerComponent> scanner, ref MedicalXRayDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !TryGetEntity(args.Patient, out var patientUid) || patientUid is not { } patient
            || !CanTakeXRay(scanner, args.User, patient, args.Slot))
            return;

        _damage.ChangeDamage(patient, new DamageSpecifier(_prototypes.Index(Radiation), 0.5), true);
        RecordExamination(patient, args.Slot, AdvancedTreatmentType.XRay, args.User, scanner);
        args.Handled = true;
    }

    private bool CanReachAnalyzer(EntityUid user, EntityUid analyzer, EntityUid tube)
        => _hands.IsHolding(user, tube) && _actions.CanUseHeldEntity(user, tube)
            && _actions.CanConsciouslyPerformAction(user) && _actions.CanInteract(user, analyzer)
            && _interaction.InRangeUnobstructed(user, analyzer);

    private bool IsReady(EntityUid analyzer)
        => Transform(analyzer).Anchored && TryComp<ApcPowerReceiverComponent>(analyzer, out var receiver) && receiver.Powered;

    private void OnAnalyzerUnanchored(Entity<ClinicalBloodAnalyzerComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
            CancelAnalysis(ent.Comp);
    }

    private void OnAnalyzerPowerChanged(Entity<ClinicalBloodAnalyzerComponent> ent, ref PowerChangedEvent args)
    {
        if (!args.Powered)
            CancelAnalysis(ent.Comp);
    }

    private void CancelAnalysis(ClinicalBloodAnalyzerComponent analyzer)
    {
        var analysis = analyzer.Analysis;
        analyzer.Analysis = null;
        if (_doAfter.IsRunning(analysis))
            _doAfter.Cancel(analysis);
    }

    private void OnUseAnalyzer(Entity<ClinicalBloodAnalyzerComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<ClinicalBloodSampleComponent>(args.Used, out var sample)
            || !CanReachAnalyzer(args.User, ent, args.Used))
            return;

        args.Handled = true;
        var error = !IsReady(ent) ? "diagnostic-analyzer-unavailable"
            : _doAfter.IsRunning(ent.Comp.Analysis) ? "diagnostic-analyzer-busy"
            : !_blood.IsFresh(sample) ? "diagnostic-sample-invalid"
            : null;
        if (error != null)
        {
            _popup.PopupEntity(Loc.GetString(error), ent, args.User);
            return;
        }

        if (_doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, ent.Comp.Delay,
            new ClinicalAnalysisDoAfterEvent(), ent, target: ent, used: args.Used)
        { NeedHand = true, BreakOnMove = true, BreakOnDamage = true }, out var analysis) && _doAfter.IsRunning(analysis))
            ent.Comp.Analysis = analysis;
    }

    private void OnAnalysisComplete(Entity<ClinicalBloodAnalyzerComponent> ent, ref ClinicalAnalysisDoAfterEvent args)
    {
        if (ent.Comp.Analysis == args.DoAfter.Id)
            ent.Comp.Analysis = null;

        if (args.Cancelled || args.Handled || args.Target != ent.Owner || args.Used is not { } tube
            || !CanReachAnalyzer(args.User, ent, tube))
            return;

        if (!IsReady(ent))
            _popup.PopupEntity(Loc.GetString("diagnostic-analyzer-unavailable"), ent, args.User);
        else if (!AnalyzeSample(tube, args.User, ent))
            _popup.PopupEntity(Loc.GetString("diagnostic-sample-invalid"), ent, args.User);
        args.Handled = true;
    }

    /// <summary>Uses the contents-validated sample snapshot, including after the patient's death.</summary>
    public bool AnalyzeSample(EntityUid tube, EntityUid? user = null, EntityUid? analyzer = null)
    {
        if (!TryComp<ClinicalBloodSampleComponent>(tube, out var sample) || !_blood.IsFresh(sample)) return false;
        var report = new MedicalDiagnosticReport
        {
            Kind = MedicalDiagnosticKind.Blood, Slot = BodyPartSlot.Chest,
            CollectedAt = sample.CollectedAt, PatientName = sample.PatientName, PatientIdentity = sample.PatientIdentity,
            BloodGroup = sample.BloodType, BodyFluid = sample.BodyFluid,
            Readings = new(sample.LaboratoryReadings) { ["glucose"] = sample.Glucose },
        };
        report.Findings.Add(sample.LaboratoryReadings.Count > 0 ? "diagnostic-human-laboratory" : "diagnostic-no-laboratory-reference");
        // Never attach a report to a different body just because its name matches the label.
        if (sample.Patient is { } patient && Exists(patient)
            && TryComp<AdvancedHealthComponent>(patient, out var health) && health.MedicalIdentity == sample.PatientIdentity)
        {
            Remember(patient, report);
            if (!health.BloodTypeConfirmed && health.BodyFluid == sample.BodyFluid && health.BloodType == sample.BloodType)
            {
                health.BloodTypeConfirmed = true;
                Dirty(patient, health);
            }

            EntityManager.System<AdvancedDiseaseSystem>().Diagnose(patient, ClinicalTest.Blood, sample.BloodTestConditions);
        }
        Print(analyzer ?? tube, report);
        Show(user, report);
        return true;
    }

    public void RecordExamination(EntityUid patient, BodyPartSlot slot, AdvancedTreatmentType treatment,
        EntityUid? user = null, EntityUid? printAt = null)
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
        Print(printAt ?? patient, report);
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
        };
        if (report.Kind == MedicalDiagnosticKind.Blood)
        {
            lines.Add(Loc.GetString("diagnostic-blood-group", ("group", report.BloodGroup), ("fluid", report.BodyFluid)));
            foreach (var (section, markers) in ClinicalLaboratoryModel.Sections)
            {
                if (!markers.Any(report.Readings.ContainsKey))
                    continue;

                lines.Add("");
                lines.Add(Loc.GetString("diagnostic-section-" + section));
                foreach (var marker in markers)
                {
                    if (!report.Readings.TryGetValue(marker, out var value))
                        continue;

                    var reading = Loc.GetString("diagnostic-reading-" + marker, ("value", ClinicalLaboratoryModel.FormatValue(value)));
                    var reference = report.Readings.ContainsKey("hemoglobin")
                        ? ClinicalLaboratoryModel.FormatReference(marker, value) : null;
                    lines.Add(reference == null ? reading : $"{reading} ({reference})");
                }
            }
            lines.Add("");
            foreach (var (drug, quantity) in report.Medications)
                lines.Add(Loc.GetString("diagnostic-medication", ("drug", _prototypes.Index<ReagentPrototype>(drug).LocalizedName),
                    ("value", quantity.ToString("0.##", CultureInfo.InvariantCulture))));
        }
        else
        {
            lines.Add(Loc.GetString("advanced-health-part-" + report.Slot.ToString().ToLowerInvariant()));
            foreach (var (key, value) in report.Readings)
                lines.Add(Loc.GetString("diagnostic-reading-" + key, ("value", ClinicalLaboratoryModel.FormatValue(value))));
        }
        foreach (var finding in report.Findings) lines.Add(Loc.GetString(finding));
        var paper = Spawn("Paper", Transform(source).Coordinates);
        _paper.SetContent(paper, string.Join("\n", lines));
    }
}
