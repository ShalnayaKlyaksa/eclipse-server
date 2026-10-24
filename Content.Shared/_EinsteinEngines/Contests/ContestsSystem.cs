using Robust.Shared.Physics.Components;

namespace Content.Shared._EinsteinEngines.Contests;

public sealed partial class ContestsSystem : EntitySystem
{
    private const float AverageMass = 71f;
    private const float MaxMassContestDelta = 0.5f;

    public float MassContest(EntityUid performerUid, bool bypassClamp = false, float rangeFactor = 1f, float otherMass = AverageMass)
    {
        if (!TryComp<PhysicsComponent>(performerUid, out var performerPhysics) || performerPhysics.Mass == 0)
            return 1f;

        return ClampContest(performerPhysics.Mass / otherMass, bypassClamp, rangeFactor);
    }

    public float MassContest(EntityUid? performerUid, bool bypassClamp = false, float rangeFactor = 1f, float otherMass = AverageMass)
    {
        if (performerUid is not { } uid)
            return 1f;

        return MassContest(uid, bypassClamp, rangeFactor, otherMass);
    }

    public float MassContest(PhysicsComponent performerPhysics, bool bypassClamp = false, float rangeFactor = 1f, float otherMass = AverageMass)
    {
        if (performerPhysics.Mass == 0)
            return 1f;

        return ClampContest(performerPhysics.Mass / otherMass, bypassClamp, rangeFactor);
    }

    public float MassContest(EntityUid performerUid, EntityUid targetUid, bool bypassClamp = false, float rangeFactor = 1f)
    {
        if (!TryComp<PhysicsComponent>(performerUid, out var performerPhysics)
            || !TryComp<PhysicsComponent>(targetUid, out var targetPhysics)
            || performerPhysics.Mass == 0
            || targetPhysics.InvMass == 0)
            return 1f;

        return ClampContest(performerPhysics.Mass * targetPhysics.InvMass, bypassClamp, rangeFactor);
    }

    public float MassContest(EntityUid performerUid, PhysicsComponent targetPhysics, bool bypassClamp = false, float rangeFactor = 1f)
    {
        if (!TryComp<PhysicsComponent>(performerUid, out var performerPhysics)
            || performerPhysics.Mass == 0
            || targetPhysics.InvMass == 0)
            return 1f;

        return ClampContest(performerPhysics.Mass * targetPhysics.InvMass, bypassClamp, rangeFactor);
    }

    public float MassContest(PhysicsComponent performerPhysics, EntityUid targetUid, bool bypassClamp = false, float rangeFactor = 1f)
    {
        if (!TryComp<PhysicsComponent>(targetUid, out var targetPhysics)
            || performerPhysics.Mass == 0
            || targetPhysics.InvMass == 0)
            return 1f;

        return ClampContest(performerPhysics.Mass * targetPhysics.InvMass, bypassClamp, rangeFactor);
    }

    public float MassContest(PhysicsComponent performerPhysics, PhysicsComponent targetPhysics, bool bypassClamp = false, float rangeFactor = 1f)
    {
        if (performerPhysics.Mass == 0 || targetPhysics.InvMass == 0)
            return 1f;

        return ClampContest(performerPhysics.Mass * targetPhysics.InvMass, bypassClamp, rangeFactor);
    }

    private static float ClampContest(float value, bool bypassClamp, float rangeFactor)
    {
        if (bypassClamp)
            return value;

        var delta = MaxMassContestDelta * rangeFactor;
        return Math.Clamp(value, 1f - delta, 1f + delta);
    }
}
