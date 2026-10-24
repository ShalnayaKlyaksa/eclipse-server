namespace Content.Server._Eclipse.ProtoCore.Components;

[RegisterComponent]
public sealed partial class ProtoCoreActivationKeyComponent : Component
{
    /// <summary>
    /// Zero shift keys bypass the hacking device requirement and use a shorter meltdown timer.
    /// </summary>
    [DataField]
    public bool ZeroShift;

    [DataField]
    public float ZeroShiftMeltdownTime = 320f;
}
