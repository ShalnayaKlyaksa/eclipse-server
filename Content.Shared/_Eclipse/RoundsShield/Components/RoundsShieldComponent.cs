using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Eclipse.RoundsShield.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RoundsShieldComponent : Component
{
    [DataField]
    public TimeSpan RaiseDelay = TimeSpan.FromSeconds(2.2);

    [DataField, AutoNetworkedField]
    public int MaxCharges = 5;

    [DataField, AutoNetworkedField]
    public int Charges = 5;

    [DataField]
    public float ProjectileBlockChance = 0.10f;

    [DataField]
    public float MeleeMissChance = 0.20f;

    [DataField]
    public float ArcDegrees = 110f;

    [DataField]
    public float VisualOffset = 0.45f;

    [DataField]
    public EntProtoId VisualNorthPrototype = "RoundsShieldVisualNorth";

    [DataField]
    public EntProtoId VisualEastPrototype = "RoundsShieldVisualEast";

    [DataField]
    public EntProtoId VisualSouthPrototype = "RoundsShieldVisualSouth";

    [DataField]
    public EntProtoId VisualWestPrototype = "RoundsShieldVisualWest";

    [AutoNetworkedField]
    public bool Raising;

    [AutoNetworkedField]
    public bool Active;

    [AutoNetworkedField, DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan RaiseEndTime;

    [AutoNetworkedField]
    public Angle AimAngle;
}
