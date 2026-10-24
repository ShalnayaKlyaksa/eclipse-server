using Robust.Shared.GameStates;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>
/// Machine operates from accumulated heat instead of APC power.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class IndustrialHeatPoweredComponent : Component
{
    /// <summary>
    /// Minimum body temperature (Kelvin) required to process recipes.
    /// </summary>
    [DataField]
    public float MinTemperature = 400f;
}
