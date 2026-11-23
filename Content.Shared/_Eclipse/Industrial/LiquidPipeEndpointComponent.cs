using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
namespace Content.Shared._Eclipse.Industrial;

/// <summary>A tank connection for non-processor machines, using the same physical liquid network.</summary>
[RegisterComponent]
public sealed partial class LiquidPipeEndpointComponent : Component
{
    [DataField] public string InputSolution = "tank";
    [DataField] public string OutputSolution = "output";
    [DataField] public Direction InputFace = Direction.West;
    [DataField] public Direction OutputFace = Direction.East;
    [DataField] public bool HasOutput;
    [DataField] public HashSet<ProtoId<ReagentPrototype>> AllowedInputs = new();
    public LiquidPortMode GetPortMode(Direction face) => face == InputFace ? LiquidPortMode.Input : HasOutput && face == OutputFace ? LiquidPortMode.Output : LiquidPortMode.Disabled;
}
