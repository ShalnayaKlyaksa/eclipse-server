using Content.Shared.Actions;

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

public enum RacialInstantAbility : byte
{
    DwarfBrace,
    LavriteVent,
    CanineScent,
    TherianScurry,
    ShadowVeil,
    SlimeReform,
}

public enum RacialTargetAbility : byte
{
    MoleiSilk,
    LavriteCoolTouch,
    SaurianTailSweep,
    ElirReadPulse,
    SlimeAdhesive,
}
