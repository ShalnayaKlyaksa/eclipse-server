using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>
/// Empty industrial machine body. Accepts upgrade modules via the industrial key.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class IndustrialMachineChassisComponent : Component;

/// <summary>
/// Upgrade module installed on a chassis to create a finished industrial machine.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class IndustrialUpgradeModuleComponent : Component
{
    [DataField(required: true)]
    public IndustrialProcessorType ProcessorType;

    [DataField]
    public MachineTier Tier = MachineTier.Basic;

    /// <summary>
    /// Spawned machine prototype when applied to a chassis.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId ResultMachine;
}

/// <summary>
/// Two-tile industrial fabrication station.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class IndustrialWorkbenchComponent : Component;
