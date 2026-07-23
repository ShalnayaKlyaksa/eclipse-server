using Content.Shared._Eclipse.Industrial;
using Content.Shared.Atmos;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
[Access(typeof(SharedItemPipeSystem), Other = AccessPermissions.ReadWriteExecute)]
public sealed partial class ItemPipeComponent : Component
{
    [DataField]
    public PipeTier Tier = PipeTier.Basic;

    [DataField]
    public PipeDirection OriginalPipeDirection = PipeDirection.South;

    [DataField]
    public float ThroughputPerSecond = 1f;

    [DataField]
    public float TransferDelay = 1f;

    [DataField, AutoNetworkedField]
    public PipeTransferMode TransferMode = PipeTransferMode.Transit;

    [DataField, AutoNetworkedField]
    public int NetworkId = -1;

    [DataField, AutoNetworkedField]
    public PipeDirection ConnectedDirections = PipeDirection.None;
}
