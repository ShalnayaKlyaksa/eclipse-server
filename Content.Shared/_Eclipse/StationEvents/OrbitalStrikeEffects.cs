using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.StationEvents;

/// <summary>Only a coarse source bearing is exposed to mass scanners, never the impact corridor.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class IncomingGungnirComponent : Component
{
    [DataField, AutoNetworkedField] public Vector2 SourceBearing;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class SiegeProjectileVisualComponent : Component
{
    [DataField, AutoNetworkedField] public Vector2 Direction = Vector2.UnitX;
    [DataField] public bool Exhaust = true;
}

[Serializable, NetSerializable]
public enum OrbitalStrikeEffect : byte
{
    GungnirPassage,
    GungnirBreach,
    HammerImpact,
    DamoclesImpact,
}

/// <summary>Clients animate the very fast passage locally, independently of entity replication rate.</summary>
[Serializable, NetSerializable]
public sealed class OrbitalStrikeEffectEvent : EntityEventArgs
{
    public OrbitalStrikeEffect Effect;
    public MapCoordinates Origin;
    public Vector2 Direction;
    public float HalfLength;
    public float Duration;

    public OrbitalStrikeEffectEvent(OrbitalStrikeEffect effect, MapCoordinates origin, Vector2 direction, float halfLength = 0, float duration = 2)
    {
        Effect = effect;
        Origin = origin;
        Direction = direction;
        HalfLength = halfLength;
        Duration = duration;
    }
}
