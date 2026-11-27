using Content.Shared.Atmos;
using Robust.Shared.GameStates;

namespace Content.Shared._Eclipse.Industrial;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class LiquidPipeComponent : Component
{
    public const string SolutionName = "pipe";

    [DataField, AutoNetworkedField]
    public PipeDirection ConnectedDirections;

    [DataField, AutoNetworkedField]
    public PipeDirection MachineInputDirections;

    [DataField, AutoNetworkedField]
    public PipeDirection MachineOutputDirections;

    [DataField]
    public PipeTier Tier = PipeTier.Basic;

    [DataField]
    public float ThroughputPerSecond = 1f;

    [DataField]
    public float? ThroughputOverride;

    [DataField, AutoNetworkedField]
    public int NetworkId = -1;
}
