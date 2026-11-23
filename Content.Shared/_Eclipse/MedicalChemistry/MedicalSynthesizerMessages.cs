using Content.Shared.FixedPoint;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.MedicalChemistry;

[Serializable, NetSerializable]
public enum MedicalSynthesisProtocol : byte
{
    Rapid,
    Precision,
    Purify,
    Personalize,
}

[Serializable, NetSerializable]
public enum MedicalReactorHeat : byte
{
    Automatic,
    Boost,
    Cool,
}

[Serializable, NetSerializable]
public sealed class MedicalSynthesisStartMessage(string recipe, MedicalSynthesisProtocol protocol, int amount) : BoundUserInterfaceMessage
{
    public readonly string Recipe = recipe;
    public readonly MedicalSynthesisProtocol Protocol = protocol;
    public readonly int Amount = amount;
}

[Serializable, NetSerializable]
public sealed class MedicalReactorHeatMessage(MedicalReactorHeat heat) : BoundUserInterfaceMessage
{
    public readonly MedicalReactorHeat Heat = heat;
}

[Serializable, NetSerializable]
public sealed class MedicalSynthesisCancelMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class MedicalSampleAnalyzeMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public enum MedicalReactorVisuals : byte
{
    Working,
}

[Serializable, NetSerializable]
public sealed record MedicalSynthesisAnalysis(string Recipe, FixedPoint2 Precursor, FixedPoint2 Medicine, float Purity, float PrecursorPurity = 1f, bool MixedProfiles = false);

[Serializable, NetSerializable]
public sealed record MedicalSynthesizerState(
    bool Powered,
    string? ActiveRecipe,
    MedicalSynthesisProtocol Protocol,
    MedicalReactorHeat Heat,
    float Temperature,
    float Progress,
    float Stress,
    FixedPoint2 Volume,
    string Status,
    MedicalSynthesisAnalysis[] Analysis,
    bool Advanced = false,
    bool AnalysisOnly = false,
    bool Analyzing = false,
    string? SampleProfile = null,
    ReagentQuantity[]? AssayResults = null);
