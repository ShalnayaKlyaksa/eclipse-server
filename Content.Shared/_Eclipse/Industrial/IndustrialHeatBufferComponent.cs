using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>
/// Exchanges heat with a plasma circuit and supplies industrial heat inputs directly or through heat pipes.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class IndustrialHeatBufferComponent : Component
{
    public const string HeatNodeName = "heat";

    [DataField]
    public string InletNodeName = "inlet";

    [DataField]
    public string OutletNodeName = "outlet";

    /// <summary>
    /// Swaps the roles of the fixed gas ports without rebuilding their pipe networks.
    /// </summary>
    [DataField]
    public bool Reversed;

    /// <summary>
    /// Maximum delivery temperature (Kelvin); the buffer must be heated by an external source.
    /// </summary>
    [DataField]
    public float OperatingTemperature = 1200f;

    /// <summary>
    /// Minimum plasma moles in the inlet pipe to count as active.
    /// </summary>
    [DataField]
    public float MinPlasmaMoles = 0.25f;

    /// <summary>
    /// Heat transferred per second to all linked machines, in joules.
    /// </summary>
    [DataField]
    public float HeatTransferRate = 500000f;

    [ViewVariables]
    public bool PlasmaFlowing;

    public bool SharedGasCircuit;
}

[Serializable, NetSerializable]
public enum IndustrialHeatBufferVisuals : byte
{
    PlasmaFlowing,
}

public enum IndustrialHeatBufferVisualLayers : byte
{
    Light,
}
