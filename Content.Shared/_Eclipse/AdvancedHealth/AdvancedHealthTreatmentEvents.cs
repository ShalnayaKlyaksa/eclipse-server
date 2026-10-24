using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>
/// Client finished a treatment minigame and requests the server to apply the result.
/// </summary>
[Serializable, NetSerializable]
public sealed class AdvancedHealthTreatmentCompleteEvent : EntityEventArgs
{
    public NetEntity Target;
    public BodyPartSlot Slot;
    public AdvancedTreatmentType Treatment;
    /// <summary>0 = failed, 1 = perfect. Server clamps and scales effectiveness.</summary>
    public float Quality;
    /// <summary>Null when using bare hands (only valid for foreign-body removal).</summary>
    public NetEntity? Tool;
    /// <summary>
    /// For bandaging: how many 1%-durability segments the player wound on. Each removes 0.01 L/min of
    /// bleeding and spends 1% of the roll. The server re-clamps to the roll's durability and the wound.
    /// </summary>
    public int Segments;

    public AdvancedHealthTreatmentCompleteEvent(
        NetEntity target,
        BodyPartSlot slot,
        AdvancedTreatmentType treatment,
        float quality,
        NetEntity? tool,
        int segments = 0)
    {
        Target = target;
        Slot = slot;
        Treatment = treatment;
        Quality = quality;
        Tool = tool;
        Segments = segments;
    }
}

/// <summary>
/// Client requests a transfusion into <see cref="Target"/> from a held blood/fluid pack.
/// </summary>
[Serializable, NetSerializable]
public sealed class AdvancedHealthTransfusionEvent : EntityEventArgs
{
    public NetEntity Target;
    public NetEntity Pack;

    public AdvancedHealthTransfusionEvent(NetEntity target, NetEntity pack)
    {
        Target = target;
        Pack = pack;
    }
}
