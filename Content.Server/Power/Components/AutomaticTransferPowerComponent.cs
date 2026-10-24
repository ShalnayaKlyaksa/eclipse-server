using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.Power.Components;

/// <summary>
/// Switches a battery charger between a preferred and fallback cable node.
/// </summary>
[RegisterComponent]
public sealed partial class AutomaticTransferPowerComponent : Component
{
    [DataField]
    public string MainNode = "main";

    [DataField]
    public string EmergencyNode = "emergency";

    [DataField]
    public float SupplyThreshold = 0f;

    [DataField]
    public TimeSpan SwitchCooldown = TimeSpan.FromSeconds(1);

    [ViewVariables]
    public string? ActiveNode;

    [ViewVariables]
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextSwitchTime;
}
