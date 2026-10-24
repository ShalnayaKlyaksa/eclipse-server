using Content.Shared._Eclipse.ProtoCore;
using Robust.Shared.Audio;
using Content.Shared.Explosion;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Eclipse.ProtoCore.Components;

[RegisterComponent, Access(typeof(ProtoCoreSystem))]
public sealed partial class ProtoCoreComponent : Component
{
    [DataField]
    public float MeltdownTime = 360f;

    [DataField]
    public float CriticalThreshold = 120f;

    [DataField]
    public float WarningThreshold = 240f;

    [DataField]
    public float PowerLossPenalty = 120f;

    [DataField]
    public float EmergencyShuttleTime = 60f;

    [DataField]
    public float EmergencyShuttleDockTime = 30f;

    [DataField]
    public SoundSpecifier MeltdownMusic = new SoundCollectionSpecifier("NukeMusic");

    [DataField]
    public float MeltdownMusicDuration = 208f;

    [DataField]
    public ProtoId<ExplosionPrototype> ExplosionType = "Default";

    [DataField]
    public float ExplosionTotalIntensity = 200000f;

    [DataField]
    public float ExplosionIntensitySlope = 5f;

    [DataField]
    public float ExplosionMaxTileIntensity = 200f;

    [DataField]
    public float RoundEndDelay = 60f;

    [DataField]
    public float RemainingTime;

    [DataField]
    public ProtoCoreState State = ProtoCoreState.Idle;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextStatusAt;

    [DataField]
    public bool EmergencyShuttleCalled;

    [DataField]
    public bool WarningStageStarted;

    [DataField]
    public bool CriticalStageStarted;

    [DataField]
    public float LastStorageCharge;

    [DataField]
    public float LastStorageCapacity;

    [DataField]
    public bool HadChargedStorage;

    [DataField]
    public bool Exploded;

    /// <summary>
    /// Alert level active on the station before meltdown forced delta.
    /// </summary>
    [DataField]
    public string PreMeltdownAlertLevel = string.Empty;

    /// <summary>
    /// When set, critical stage does not call an emergency shuttle for the station.
    /// </summary>
    [DataField]
    public bool SkipEmergencyShuttle;
}
