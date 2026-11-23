using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Eclipse.StationEvents;

public enum EclipseIncidentKind : byte
{
    Gungnir,
    Hammer,
    Damocles,
    DistressVessel,
    OtherShift,
    ZeroPoint,
    LivingHull,
    BlackTide,
}

/// <summary>Levels 5-7 reserve the common scale for the future blob antagonist.</summary>
public enum BiologicalHazardLevel : byte
{
    LocalGrowth = 1,
    InvasiveGrowth = 2,
    HostileEcosystem = 3,
    StationOvergrowth = 4,
    Blob = 5,
    Planetary = 6,
    Uncontained = 7,
}

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class EclipseIncidentComponent : Component
{
    [DataField(required: true)] public EclipseIncidentKind Kind;
    [DataField] public TimeSpan Warning = TimeSpan.FromSeconds(30);
    [DataField] public float Intensity = 180;
    [DataField] public float StrikeWidth = 3;
    [DataField] public int Salvos = 5;
    [DataField] public TimeSpan Interval = TimeSpan.FromSeconds(8);
    [DataField] public TimeSpan IdentificationDelay = TimeSpan.FromSeconds(90);
    [DataField] public BiologicalHazardLevel? HazardLevel;

    public EntityUid? Station;
    public EntityUid? Grid;
    public EntityCoordinates Target;
    public Vector2 Direction;
    public float HalfLength;
    public MapCoordinates StrikeOrigin;
    public Vector2 StrikeDirection;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextAction;
    public int Phase;
    public int Counter;
    public EntityUid? Subject;
    public bool SalvoCuePlayed;
    public readonly List<EntityUid> Markers = new();
}

/// <summary>Destructible delayed payload; its timer travels with it when moved.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class EclipseTimedPayloadComponent : Component
{
    [DataField] public TimeSpan Delay = TimeSpan.FromSeconds(30);
    [DataField] public float Intensity = 600;
    [DataField] public EntProtoId? Hatch;
    [DataField] public int HatchCount = 3;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan Deadline;
    public bool Armed;
}

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class EclipseColdSourceComponent : Component
{
    [DataField] public float Radius = 4;
    [DataField] public float CoolingPerSecond = 8;
    [DataField] public float MinimumTemperature = 170;
    [DataField] public TimeSpan Lifetime = TimeSpan.FromMinutes(8);
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextTick;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan Expires;
}

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class EclipseLivingCoreComponent : Component
{
    [DataField] public BiologicalHazardLevel Level = BiologicalHazardLevel.LocalGrowth;
    [DataField] public int MaxGrowth = 360;
    public readonly HashSet<EntityUid> Growth = new();
    public readonly HashSet<EntityUid> Creatures = new();
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextCreature;
}

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class EclipseLivingGrowthComponent : Component
{
    public EntityUid? Core;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextSpread;
}

/// <summary>Marker only: no blob, ghost role or antagonist is spawned yet.</summary>
[RegisterComponent]
public sealed partial class EclipseBlobSeedPlaceholderComponent : Component;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class EclipsePhototaxisComponent : Component
{
    public EntityCoordinates MigrationDestination;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextThink;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan Expires;
}
