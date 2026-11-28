using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>A reusable instrument loses sterility on contact with a patient or after being dropped.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SurgicalInstrumentComponent : Component
{
    [DataField, AutoNetworkedField] public bool Sterile = true;
}

/// <summary>A single-use sterile wrap for reprocessing one instrument.</summary>
[RegisterComponent]
public sealed partial class InstrumentSterilizationComponent : Component
{
    [DataField] public float Delay = 5;
}

[Serializable, NetSerializable]
public sealed partial class InstrumentSterilizationDoAfterEvent : SimpleDoAfterEvent;

[RegisterComponent]
public sealed partial class ClinicalBloodAnalyzerComponent : Component
{
    [DataField] public float Delay = 8;
    public DoAfterId? Analysis;
}

[Serializable, NetSerializable]
public sealed partial class ClinicalAnalysisDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class MedicalXRayDoAfterEvent : DoAfterEvent
{
    [DataField] public NetEntity Patient;
    [DataField] public BodyPartSlot Slot;

    public override DoAfterEvent Clone() => new MedicalXRayDoAfterEvent { Patient = Patient, Slot = Slot };
}

/// <summary>Completed observations, never a live view of undiscovered illness.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MedicalDiagnosticHistoryComponent : Component
{
    [DataField, AutoNetworkedField] public List<MedicalDiagnosticReport> Reports = new();
}

[Serializable, NetSerializable, DataDefinition]
public sealed partial class MedicalDiagnosticReport
{
    [DataField] public MedicalDiagnosticKind Kind;
    [DataField] public BodyPartSlot Slot;
    [DataField] public TimeSpan CollectedAt;
    [DataField] public string PatientName = "";
    [DataField] public string PatientIdentity = "";
    [DataField] public Dictionary<string, float> Readings = new();
    [DataField] public List<string> Findings = new();
    [DataField] public string BloodGroup = "";
    [DataField] public string BodyFluid = "";
    [DataField] public Dictionary<string, float> Medications = new();
}

[Serializable, NetSerializable]
public enum MedicalDiagnosticKind : byte { Blood, XRay, Ecg }

[Serializable, NetSerializable]
public sealed class MedicalDiagnosticResultEvent(MedicalDiagnosticReport report) : EntityEventArgs
{
    public MedicalDiagnosticReport Report = report;
}
