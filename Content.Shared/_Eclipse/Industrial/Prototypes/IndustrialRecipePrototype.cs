using Robust.Shared.Prototypes;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Dictionary;

namespace Content.Shared._Eclipse.Industrial.Prototypes;

[Prototype]
public sealed partial class IndustrialRecipePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Optional process name for alternate routes that produce the same material.</summary>
    [DataField] public LocId? Name;

    [DataField(required: true)]
    public IndustrialProcessorType Processor;

    [DataField(required: true)]
    public float Time = 5f;

    [DataField]
    public MachineTier MinimumTier = MachineTier.Basic;

    [DataField]
    public ProtoId<Content.Shared.Research.Prototypes.TechnologyPrototype>? RequiredTechnology;

    /// <summary>Energy drawn from the machine body when a thermal cycle completes (joules).</summary>
    [DataField]
    public float Heat;

    /// <summary>Zero for ordinary production, one/two for controlled clean rooms.</summary>
    [DataField] public int CleanroomGrade;
    [DataField] public bool RequireDataDisk;


    [DataField(customTypeSerializer: typeof(PrototypeIdDictionarySerializer<int, EntityPrototype>))]
    public Dictionary<string, int> Inputs = new();

    [DataField(customTypeSerializer: typeof(PrototypeIdDictionarySerializer<int, EntityPrototype>))]
    public Dictionary<string, int> Outputs = new();

    [DataField] public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> FluidInputs = new();
    [DataField] public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> FluidOutputs = new();

    /// <summary>Required medium left in the process tank after a cycle, in addition to consumed reagents.</summary>
    [DataField] public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> Bath = new();
}
