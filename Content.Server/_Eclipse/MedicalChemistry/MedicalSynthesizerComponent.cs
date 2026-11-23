using Content.Shared._Eclipse.MedicalChemistry;
using Robust.Shared.Prototypes;
using Content.Shared.Chemistry.Reagent;

namespace Content.Server._Eclipse.MedicalChemistry;

[RegisterComponent]
public sealed partial class MedicalSynthesizerComponent : Component
{
    [DataField] public ProtoId<MedicalSynthesisPrototype>? ActiveRecipe;
    [DataField] public MedicalSynthesisProtocol Protocol;
    [DataField] public MedicalReactorHeat Heat;
    [DataField] public float Progress;
    [DataField] public float Stress;
    [DataField] public string Status = "medical-chemistry-status-ready";
    [DataField] public bool Advanced;
    [DataField] public bool AnalysisOnly;
    [DataField] public bool Analyzing;
    [DataField] public string? PatientDna;
    [DataField] public ReagentQuantity[] AssayResults = [];
    public float UiAccumulator;
}
