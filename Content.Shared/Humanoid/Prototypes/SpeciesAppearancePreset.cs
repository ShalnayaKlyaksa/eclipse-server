using Content.Shared.Humanoid.Markings;
using Robust.Shared.Prototypes;

namespace Content.Shared.Humanoid.Prototypes;

[DataDefinition]
public sealed partial class SpeciesAppearancePreset
{
    [DataField(required: true)] public LocId Name;
    [DataField(required: true)] public Color Skin;
    [DataField(required: true)] public PresetGradient Hair = default!;
    [DataField(required: true)] public PresetGradient Tail = default!;
}

[DataDefinition]
public sealed partial class PresetGradient
{
    [DataField(required: true)] public ProtoId<MarkingPrototype> Fallback;
    [DataField(required: true)] public Color Root;
    [DataField(required: true)] public Color Middle;
    [DataField(required: true)] public Color Tip;

    public Marking Apply(Marking marking) => marking.WithColor(Root) with
    {
        Gradient = new MarkingGradient
        {
            Steps = 5,
            TipColor = Tip,
            Stops = [new MarkingGradientStop { Position = 52, Color = Middle }],
        },
    };
}
