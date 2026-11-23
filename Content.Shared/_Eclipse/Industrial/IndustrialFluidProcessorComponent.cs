using Robust.Shared.GameStates;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>Separate process and product tanks. Recipes, rather than ambient chemistry, control reactions inside them.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class IndustrialFluidProcessorComponent : Component
{
    [DataField] public string InputSolution = "industrial_fluid_input";
    [DataField] public string OutputSolution = "industrial_fluid_output";
}
