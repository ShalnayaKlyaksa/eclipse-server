using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.WallDisplays;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class TelevisionComponent : Component
{
    public const float ViewingRange = 12f;
    [DataField, AutoNetworkedField] public TelevisionChannel Channel;
    [DataField, AutoNetworkedField] public EntityUid? Camera;
    [DataField, AutoNetworkedField] public bool Powered;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PublicBroadcastCameraComponent : Component
{
    [DataField, AutoNetworkedField] public bool OnAir;
    [DataField, AutoNetworkedField] public string Reporter = "";
    public EntityUid? Station;
}

[Serializable, NetSerializable]
public enum TelevisionChannel : byte { Broadcast, Flappy, Blocks }
[Serializable, NetSerializable]
public enum TelevisionUiKey : byte { Key }
[Serializable, NetSerializable]
public readonly record struct PublicBroadcastChannel(NetEntity Camera, string Reporter);

[Serializable, NetSerializable]
public sealed class TelevisionState(TelevisionChannel channel, NetEntity? camera, bool powered, PublicBroadcastChannel[] broadcasts) : BoundUserInterfaceState
{
    public TelevisionChannel Channel = channel;
    public NetEntity? Camera = camera;
    public bool Powered = powered;
    public PublicBroadcastChannel[] Broadcasts = broadcasts;
}

[Serializable, NetSerializable]
public sealed class TelevisionTuneMessage(TelevisionChannel channel, NetEntity? camera = null) : BoundUserInterfaceMessage
{
    public TelevisionChannel Channel = channel;
    public NetEntity? Camera = camera;
}
