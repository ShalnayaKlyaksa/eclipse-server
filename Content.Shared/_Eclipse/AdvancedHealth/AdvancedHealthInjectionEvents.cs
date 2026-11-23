using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.AdvancedHealth;

[Serializable, NetSerializable]
public enum InjectionInputAction : byte { Start, Adjust, Stop, BreakNeedle }

[Serializable, NetSerializable]
public sealed class AdvancedHealthInjectionInputEvent(int id, NetEntity target, NetEntity tool, BodyPartSlot slot,
    InjectionInputAction action, float depth = 0, bool held = false) : EntityEventArgs
{
    public int Id = id;
    public NetEntity Target = target;
    public NetEntity Tool = tool;
    public BodyPartSlot Slot = slot;
    public InjectionInputAction Action = action;
    public float Depth = depth;
    public bool Held = held;
}

[Serializable, NetSerializable]
public sealed class AdvancedHealthInjectionStatusEvent(int id, float injected, float remaining, bool stopped, bool failed = false, bool needleBroken = false) : EntityEventArgs
{
    public int Id = id;
    public float Injected = injected;
    public float Remaining = remaining;
    public bool Stopped = stopped;
    public bool Failed = failed;
    public bool NeedleBroken = needleBroken;
}

public static class AdvancedInjectionFlow
{
    // Units per second. The client supplies needle depth, never elapsed time or an administered dose.
    public static float Rate(float depth) => !float.IsFinite(depth) || depth < 12 || depth > 44
        ? 0 : 5 * Math.Clamp(depth / 32, 0, 1.3f);
}

/// <summary>A manual syringe was used on a character; the server opens its treatment UI.</summary>
[ByRefEvent]
public readonly record struct AdvancedHealthSyringeUseEvent(EntityUid User, EntityUid Target);

[Serializable, NetSerializable]
public sealed class AdvancedHealthOpenTreatmentEvent(NetEntity target, NetEntity tool, BodyPartSlot slot, AdvancedTreatmentType treatment = AdvancedTreatmentType.Injection) : EntityEventArgs
{
    public NetEntity Target = target;
    public NetEntity Tool = tool;
    public BodyPartSlot Slot = slot;
    public AdvancedTreatmentType Treatment = treatment;
}

[Serializable, NetSerializable]
public sealed class AdvancedHealthUseItemOnPartEvent(NetEntity target, NetEntity tool, BodyPartSlot slot) : EntityEventArgs
{
    public NetEntity Target = target;
    public NetEntity Tool = tool;
    public BodyPartSlot Slot = slot;
}
