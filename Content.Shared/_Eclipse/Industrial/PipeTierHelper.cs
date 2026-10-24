namespace Content.Shared._Eclipse.Industrial;

public readonly record struct PipeTierSpecs(
    float ThroughputPerSecond,
    float TransferDelay);

public static class PipeTierHelper
{
    public static PipeTierSpecs GetSpecs(PipeTier tier)
    {
        return tier switch
        {
            PipeTier.Basic => new PipeTierSpecs(1f, 1.0f),
            PipeTier.Industrial => new PipeTierSpecs(3f, 0.5f),
            PipeTier.Perfect => new PipeTierSpecs(6f, 0.25f),
            _ => new PipeTierSpecs(1f, 1.0f),
        };
    }

    public static PipeTier GetWeakest(PipeTier current, PipeTier candidate)
    {
        return (PipeTier) Math.Min((int) current, (int) candidate);
    }
}
