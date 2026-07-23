using Robust.Shared.GameStates;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>
/// A bandage roll with a durability pool, expressed as a percentage. Each 1% spent in the wrap
/// minigame removes 0.01 L/min of external bleeding, so a full roll can close up to 1.00 L/min total.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AdvancedBandageRollComponent : Component
{
    /// <summary>Remaining durability, 0..100 (%). 1% == 0.01 L/min of bleeding it can still stop.</summary>
    [DataField, AutoNetworkedField] public float Durability = 100f;

    [DataField] public float MaxDurability = 100f;

    /// <summary>Litres/min of bleeding removed per 1% of durability spent.</summary>
    public const float BleedPerPercent = 0.01f;
}
