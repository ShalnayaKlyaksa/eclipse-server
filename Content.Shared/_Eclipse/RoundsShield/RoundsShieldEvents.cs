using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.RoundsShield;

[ByRefEvent]
public readonly record struct RoundsShieldProjectileBlockedEvent(EntityUid Projectile, EntityUid Defender);

[ByRefEvent]
public readonly record struct RoundsShieldMeleeBlockedEvent(EntityUid Attacker, EntityUid Defender, EntityUid Weapon);

[Serializable, NetSerializable]
public sealed class RoundsShieldAimEvent : EntityEventArgs
{
    public NetEntity? User;
    public Angle AimAngle;
}
