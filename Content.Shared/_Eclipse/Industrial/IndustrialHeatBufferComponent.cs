using Content.Shared.Atmos;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>
/// Stores and distributes heat to adjacent industrial machines with heat-input ports.
/// Plasma flows through atmos inlet/outlet nodes like a TEG circulator.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class IndustrialHeatBufferComponent : Component
{
    [DataField]
    public string InletNodeName = "inlet";

    [DataField]
    public string OutletNodeName = "outlet";

    /// <summary>
    /// Target operating temperature when plasma is flowing (Kelvin).
    /// </summary>
    [DataField]
    public float OperatingTemperature = 1200f;

    /// <summary>
    /// Minimum plasma moles in the inlet pipe to count as active.
    /// </summary>
    [DataField]
    public float MinPlasmaMoles = 0.25f;

    /// <summary>
    /// Heat transferred per second to each linked machine (joules-scale units).
    /// </summary>
    [DataField]
    public float HeatTransferRate = 800f;

    [ViewVariables]
    public bool PlasmaFlowing;
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
