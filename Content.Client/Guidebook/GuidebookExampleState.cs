namespace Content.Client.Guidebook;

/// <summary>Deliberately isolated teaching examples; never operates on round entities or inventories.</summary>
public sealed class GuidebookExampleState
{
    public enum CrushResult { Success, NoPower, OutputFull, NoInput }
    public int Input { get; private set; } = 3;
    public int Output { get; private set; }

    public CrushResult Crush(bool powered, bool outputFull)
    {
        if (!powered) return CrushResult.NoPower;
        if (outputFull) return CrushResult.OutputFull;
        if (Input == 0) return CrushResult.NoInput;
        Input--;
        Output += 2;
        return CrushResult.Success;
    }
    public void Reset() { Input = 3; Output = 0; }
    public static bool CanTransfer(bool sourceIsOutput, bool targetIsInput) => sourceIsOutput && targetIsInput;
    public static bool CanStartHeatCycle(float temperature) => temperature >= 400;
}
