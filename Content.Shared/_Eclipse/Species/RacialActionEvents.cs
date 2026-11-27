using System.Numerics;
using Content.Shared.Actions;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Species;

public sealed partial class RacialInstantActionEvent : InstantActionEvent
{
    [DataField(required: true)] public RacialInstantAbility Ability;
}

public sealed partial class RacialTargetActionEvent : EntityTargetActionEvent
{
    [DataField(required: true)] public RacialTargetAbility Ability;
}

public sealed partial class AvianThrowFeatherEvent : WorldTargetActionEvent;
public sealed partial class SaurianTailSweepEvent : WorldTargetActionEvent;
public sealed partial class LavriteThermalBurstEvent : WorldTargetActionEvent;

public enum RacialInstantAbility : byte
{
    DwarfBrace,
    LavriteVent,
    CanineScent,
    TherianScurry,
    ShadowVeil,
    SlimeReform,
    ElirReadPulse,
}

public enum RacialTargetAbility : byte
{
    MoleiSilk,
    SlimeAdhesive,
}

[Serializable, NetSerializable]
public readonly record struct ElirPulseSample(MapCoordinates Position, float BeatsPerMinute);

[Serializable, NetSerializable]
public sealed class ElirPulseSamplesEvent(ElirPulseSample[] samples) : EntityEventArgs
{
    public readonly ElirPulseSample[] Samples = samples;
}

[Serializable, NetSerializable]
public sealed class LavriteHeatWaveEvent(MapCoordinates origin, float range, Vector2[] rays) : EntityEventArgs
{
    public const int RayCount = 17;
    public readonly MapCoordinates Origin = origin;
    public readonly float Range = range;
    public readonly Vector2[] Rays = rays;
}
