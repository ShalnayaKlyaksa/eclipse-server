using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>Broad armour class, purely for display/feel.</summary>
[Serializable, NetSerializable]
public enum ArmorClass : byte
{
    Clothing,
    Light,
    Medium,
    Heavy,
}

/// <summary>
/// Gives a worn item a protection rating (percent) counted by the AdvancedHealth zone readout.
/// Light clothing gives a couple percent; heavy armour gives a lot (and usually slows the wearer).
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class BodyArmorRatingComponent : Component
{
    /// <summary>Protection percentage this item contributes to the parts it covers (0..100).</summary>
    [DataField, AutoNetworkedField] public float Protection = 3f;

    [DataField, AutoNetworkedField] public ArmorClass Class = ArmorClass.Clothing;
}
