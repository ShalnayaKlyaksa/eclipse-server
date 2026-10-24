using Robust.Shared.GameStates;

namespace Content.Shared._Eclipse.Industrial;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class LiquidPipeComponent : Component
{
    [DataField]
    public PipeTier Tier = PipeTier.Basic;

    [DataField]
    public float ThroughputPerSecond = 1f;

    [DataField]
    public float TransferDelay = 1f;

    [DataField, AutoNetworkedField]
    public int NetworkId = -1;
}
