namespace Content.Shared._Eclipse.Industrial;

public readonly record struct MachineTierSpecs(
    float ProcessingSpeedMultiplier,
    int MaxInputSlots,
    int MaxOutputSlots,
    int MaxAutoTransferPerSecond,
    float PowerMultiplier);

public static class MachineTierHelper
{
    public static MachineTierSpecs GetSpecs(MachineTier tier)
    {
        return tier switch
        {
            MachineTier.Basic => new MachineTierSpecs(1.0f, 4, 4, 1, 1.0f),
            MachineTier.Industrial => new MachineTierSpecs(1.75f, 8, 8, 2, 1.6f),
            MachineTier.Perfect => new MachineTierSpecs(2.75f, 16, 16, 4, 2.5f),
            _ => new MachineTierSpecs(1.0f, 4, 4, 1, 1.0f),
        };
    }
}
