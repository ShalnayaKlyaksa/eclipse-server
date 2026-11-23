using System.Numerics;
using System.Globalization;
using System.Linq;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Chemistry.Reagent;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;
using Robust.Shared.Prototypes;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>A completed examination is a snapshot; the schematic never polls the patient's live health.</summary>
public sealed class MedicalDiagnosticReportWindow : DefaultWindow
{
    public MedicalDiagnosticReportWindow(MedicalDiagnosticReport report)
    {
        Title = Loc.GetString("diagnostic-report-title-" + report.Kind.ToString().ToLowerInvariant());
        var available = Vector2.Max(Vector2.One, UserInterfaceManager.WindowRoot.Size - new Vector2(24));
        MinSize = Vector2.Min(new Vector2(380, 360), available);
        SetSize = Vector2.Min(new Vector2(640, 650), available);
        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8,
            Margin = new Thickness(12),
        };
        Contents.AddChild(root);
        root.AddChild(new Label { Text = Loc.GetString("diagnostic-report-patient", ("patient", report.PatientName)), ClipText = true });
        root.AddChild(new Label { Text = Loc.GetString("advanced-health-part-" + report.Slot.ToString().ToLowerInvariant()) });
        root.AddChild(new Label { Text = Loc.GetString("diagnostic-report-time", ("time", report.CollectedAt.ToString(@"hh\:mm\:ss"))), ClipText = true });
        if (report.Kind != MedicalDiagnosticKind.Blood)
        {
            root.AddChild(report.Kind == MedicalDiagnosticKind.XRay
                ? new MedicalRadiographControl(report) { MinHeight = 250, HorizontalExpand = true }
                : new DiagnosticSchematic(report) { MinHeight = 160, HorizontalExpand = true });
            root.AddChild(new Label { Text = Loc.GetString("diagnostic-schematic"), ClipText = true });
        }
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
        root.AddChild(scroll);
        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 5, HorizontalExpand = true };
        scroll.AddChild(content);
        if (report.Kind == MedicalDiagnosticKind.Blood)
            AppendLaboratory(content, report);
        else
            foreach (var (key, value) in report.Readings)
                AddText(content, Loc.GetString("diagnostic-reading-" + key, ("value", Number(value))));
        foreach (var finding in report.Findings) AddText(content, Loc.GetString(finding));
        var close = new Button { Text = Loc.GetString("diagnostic-close") };
        close.OnPressed += _ => Close();
        root.AddChild(close);
    }

    private static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static void AddText(BoxContainer parent, string value, Color? color = null)
    {
        var text = new RichTextLabel { HorizontalExpand = true };
        var message = new FormattedMessage();
        message.AddText(value);
        text.SetMessage(message, color ?? Color.FromHex("#cccfc5"));
        parent.AddChild(text);
    }

    private static void AppendLaboratory(BoxContainer parent, MedicalDiagnosticReport report)
    {
        AddText(parent, Loc.GetString("diagnostic-blood-group", ("group", report.BloodGroup), ("fluid", report.BodyFluid)));
        var sections = new (string Name, string[] Markers)[]
        {
            ("hematology", ["hemoglobin", "hematocrit", "erythrocytes", "leukocytes", "platelets"]),
            ("inflammation", ["crp", "lactate"]),
            ("biochemistry", ["glucose", "creatinine", "urea"]),
            ("hepatic", ["alt", "ast", "bilirubin", "protein"]),
            ("electrolytes", ["sodium", "potassium"]),
            ("physiology", ["oxygen-capacity", "toxins", "kidneys", "liver"]),
        };
        foreach (var (name, markers) in sections)
        {
            if (!markers.Any(report.Readings.ContainsKey)) continue;
            parent.AddChild(new Label
            {
                Text = Loc.GetString("diagnostic-section-" + name),
                FontColorOverride = Color.FromHex("#b4c4b1"), Margin = new Thickness(0,12,0,3),
            });
            foreach (var marker in markers)
            {
                if (!report.Readings.TryGetValue(marker, out var value)) continue;
                var row = new BoxContainer { HorizontalExpand = true, SeparationOverride = 16 };
                var phrase = Loc.GetString("diagnostic-reading-" + marker, ("value", Number(value)));
                var abnormal = ClinicalLaboratoryModel.Reference.TryGetValue(marker, out var reference)
                    && (value < reference.Low || value > reference.High);
                row.AddChild(new Label
                {
                    Text = phrase, HorizontalExpand = true, ClipText = true, ToolTip = phrase,
                    FontColorOverride = abnormal ? Color.FromHex("#e4ae77") : Color.FromHex("#dddcd3"),
                });
                if (ClinicalLaboratoryModel.Reference.ContainsKey(marker))
                    row.AddChild(new Label
                    {
                        Text = (abnormal ? value < reference.Low ? "↓ " : "↑ " : "") + Number(reference.Low) + "–" + Number(reference.High),
                        FontColorOverride = abnormal ? Color.FromHex("#e4ae77") : Color.FromHex("#7e8980"), MinWidth = 110,
                        ToolTip = Loc.GetString("diagnostic-reference"),
                    });
                parent.AddChild(row);
            }
        }
        parent.AddChild(new Label { Text = Loc.GetString("diagnostic-section-medications"), Margin = new Thickness(0,12,0,3), FontColorOverride = Color.FromHex("#b4c4b1") });
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        foreach (var (drug, quantity) in report.Medications)
            AddText(parent, Loc.GetString("diagnostic-medication", ("drug", prototypes.Index<ReagentPrototype>(drug).LocalizedName), ("value", Number(quantity))));
        if (report.Medications.Count == 0) AddText(parent, Loc.GetString("diagnostic-no-medications"));
    }

    private sealed class DiagnosticSchematic(MedicalDiagnosticReport report) : Control
    {
        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            handle.DrawRect(new UIBox2(Vector2.Zero, Size), Color.FromHex("#080f17"));
            if (report.Kind == MedicalDiagnosticKind.Ecg)
            {
                for (var x = 0; x < Size.X; x += 20)
                    handle.DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), Color.FromHex("#19322b"));
                for (var y = 0; y < Size.Y; y += 20)
                    handle.DrawLine(new Vector2(0, y), new Vector2(Size.X, y), Color.FromHex("#19322b"));
                var rate = report.Readings.GetValueOrDefault("heart-rate");
                var previous = new Vector2(0, Size.Y / 2);
                // A six-second schematic of the recorded rate, without inventing an arrhythmia model.
                for (var x = 1; x <= (int) Size.X; x++)
                {
                    var phase = rate <= 0 ? 0.6f : x / Math.Max(1f, Size.X) * 6 * rate / 60 % 1;
                    var wave = rate <= 0 ? 0 : MathF.Exp(-MathF.Pow((phase - 0.3f) / 0.025f, 2)) * 48
                        - MathF.Exp(-MathF.Pow((phase - 0.34f) / 0.025f, 2)) * 20;
                    var next = new Vector2(x, Size.Y / 2 - wave);
                    handle.DrawLine(previous, next, Color.FromHex("#91e1b0"));
                    previous = next;
                }
                return;
            }
        }
    }
}
