using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class ActivationKeyTrackerUiState : BoundUserInterfaceState
{
    public readonly NetEntity? Grid;
    public readonly Vector2? KeyPosition;

    public ActivationKeyTrackerUiState(NetEntity? grid, Vector2? keyPosition)
    {
        Grid = grid;
        KeyPosition = keyPosition;
    }
}
