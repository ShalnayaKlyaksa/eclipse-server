using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[RegisterComponent, NetworkedComponent]
public sealed partial class IndustrialHeatPipeComponent : Component;

[Serializable, NetSerializable]
public enum IndustrialHeatPipeVisuals : byte
{
    Connections,
}
