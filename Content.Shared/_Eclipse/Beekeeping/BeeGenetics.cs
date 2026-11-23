using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Beekeeping;

[Serializable, NetSerializable]
public enum BeeCaste : byte { Princess, Drone }

[DataDefinition, Serializable, NetSerializable]
public sealed partial class BeeGenome
{
    [DataField] public string Dominant = "Meadow";
    [DataField] public string Recessive = "Meadow";
    [DataField] public int Productivity = 2;
    [DataField] public int Fertility = 2;
    [DataField] public int Hardiness = 2;
    [DataField] public int Generation;
    [DataField] public float LineageSeconds;
    public BeeGenome Clone() => new()
    {
        Dominant = Dominant, Recessive = Recessive, Productivity = Productivity, Fertility = Fertility,
        Hardiness = Hardiness, Generation = Generation, LineageSeconds = LineageSeconds,
    };
}

[Prototype]
public sealed partial class BeeSpeciesPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField] public float CycleSeconds = 180;
    [DataField] public int Tier;
    [DataField] public bool NeedsMinerals;
    [DataField] public EntProtoId Comb = "EclipseFoodHoneycomb";
    [DataField] public EntProtoId? RareProduct;
    [DataField] public float RareChance;
    [DataField] public int RareGeneration;
    [DataField] public float RareLineageSeconds;
    [DataField] public string Branch = "Natural";
    [DataField] public float MinTemperature = 289;
    [DataField] public float MaxTemperature = 309;
    [DataField] public EntProtoId? Byproduct;
    [DataField] public float ByproductChance;
}

[Prototype]
public sealed partial class BeeMutationPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public ProtoId<BeeSpeciesPrototype> First;
    [DataField(required: true)] public ProtoId<BeeSpeciesPrototype> Second;
    [DataField(required: true)] public ProtoId<BeeSpeciesPrototype> Result;
    [DataField] public float Chance;
    [DataField] public int Generation;
    [DataField] public float LineageSeconds;
    [DataField] public bool NeedsMinerals;
}

public static class BeeGenetics
{
    public static bool Eligible(BeeGenome first, BeeGenome second, BeeMutationPrototype mutation, float cycleSeconds, bool minerals)
        => ((first.Dominant == mutation.First.Id && second.Dominant == mutation.Second.Id)
            || (first.Dominant == mutation.Second.Id && second.Dominant == mutation.First.Id))
        && Math.Min(first.Generation, second.Generation) + 1 >= mutation.Generation
        && Math.Min(first.LineageSeconds, second.LineageSeconds) + cycleSeconds >= mutation.LineageSeconds
        && (!mutation.NeedsMinerals || minerals);

    public static BeeGenome Cross(BeeGenome first, BeeGenome second, float seconds, Func<double> random)
    {
        int Inherit(int a, int b) => Math.Clamp(random() < 0.5 ? a : b, 1, 5);
        return new BeeGenome
        {
            Dominant = random() < 0.5 ? first.Dominant : first.Recessive,
            Recessive = random() < 0.5 ? second.Dominant : second.Recessive,
            Productivity = Inherit(first.Productivity, second.Productivity),
            Fertility = Inherit(first.Fertility, second.Fertility),
            Hardiness = Inherit(first.Hardiness, second.Hardiness),
            Generation = Math.Min(first.Generation, second.Generation) + 1,
            // Parallel families cannot add their ages together to bypass the time gate.
            LineageSeconds = Math.Min(first.LineageSeconds, second.LineageSeconds) + seconds,
        };
    }

    public static bool RareEligible(BeeGenome genome, BeeSpeciesPrototype species)
        => species.RareProduct != null && genome.Generation >= species.RareGeneration
            && genome.LineageSeconds >= species.RareLineageSeconds;
}
