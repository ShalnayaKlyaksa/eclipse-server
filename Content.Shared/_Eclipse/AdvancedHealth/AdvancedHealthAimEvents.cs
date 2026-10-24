using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.AdvancedHealth;

[Serializable, NetSerializable]
public sealed class AdvancedHealthSetAimTargetEvent : EntityEventArgs
{
    public BodyPartTarget Target;

    public AdvancedHealthSetAimTargetEvent(BodyPartTarget target)
    {
        Target = target;
    }
}
