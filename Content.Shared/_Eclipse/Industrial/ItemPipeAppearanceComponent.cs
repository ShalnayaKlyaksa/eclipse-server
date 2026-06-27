using Content.Shared.Atmos;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[RegisterComponent, NetworkedComponent]
public sealed partial class ItemPipeAppearanceComponent : Component
{
    [DataField]
    public Dictionary<ItemPipeLayer, string> SpriteRsiPaths = new();
}
