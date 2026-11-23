namespace Content.Shared._Eclipse.Industrial;

public readonly record struct PipeTierSpecs(
    float ThroughputPerSecond);

public static class PipeTierHelper
{
    public static PipeTierSpecs GetSpecs(PipeTier tier)
    {
        return tier switch
        {
            PipeTier.Basic => new PipeTierSpecs(1f),
            PipeTier.Industrial => new PipeTierSpecs(3f),
            PipeTier.Perfect => new PipeTierSpecs(6f),
            _ => new PipeTierSpecs(1f),
        };
    }

    public static PipeTier GetWeakest(PipeTier current, PipeTier candidate)
    {
        return (PipeTier) Math.Min((int) current, (int) candidate);
    }
}
