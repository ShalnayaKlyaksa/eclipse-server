using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Poltergeist;

/// <summary>The spirit persists while its mind temporarily controls an ordinary object.</summary>
[RegisterComponent]
public sealed partial class PoltergeistComponent : Component
{
    [DataField] public float Energy = 100;
    [DataField] public float MaxEnergy = 100;
    [DataField] public float EnergyPerSecond = 2;
    [DataField] public float PossessionRange = 4;
    [DataField] public float MaxMoveMass = 50;
    [DataField] public float MaxThrowMass = 10;
    [ViewVariables] public EntityUid? Host;
    [ViewVariables] public EntityUid? Mind;
    [ViewVariables] public TimeSpan PossessAfter;
    [ViewVariables] public List<EntityUid> Actions = new();
    // Only components installed by possession are removed when the original object is restored.
    public List<Type> AddedComponents = new();
    public bool Transferring;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class PoltergeistPossessedComponent : Component
{
    [ViewVariables] public EntityUid Spirit;
    [ViewVariables] public TimeSpan RevealedUntil;
    [ViewVariables] public TimeSpan SealedUntil;
}

[RegisterComponent]
public sealed partial class PoltergeistDetectorComponent : Component
{
    [DataField] public float Range = 4;
    [DataField] public TimeSpan SealDuration = TimeSpan.FromSeconds(12);
    public TimeSpan NextUse;
}

public sealed partial class PoltergeistPossessActionEvent : EntityTargetActionEvent;
public sealed partial class PoltergeistLeaveActionEvent : InstantActionEvent;
public sealed partial class PoltergeistMoveActionEvent : WorldTargetActionEvent;
public sealed partial class PoltergeistThrowActionEvent : WorldTargetActionEvent;
public sealed partial class PoltergeistRotateActionEvent : InstantActionEvent;
public sealed partial class PoltergeistKnockActionEvent : InstantActionEvent;
public sealed partial class PoltergeistUseActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class PoltergeistExorciseDoAfterEvent : SimpleDoAfterEvent;
