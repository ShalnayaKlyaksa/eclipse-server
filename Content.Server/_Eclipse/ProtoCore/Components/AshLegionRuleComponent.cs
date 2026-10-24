using Content.Shared._Eclipse.ProtoCore;
namespace Content.Server._Eclipse.ProtoCore.Components;

[RegisterComponent]
public sealed partial class AshLegionRuleComponent : Component
{
    [DataField]
    public ProtoCoreState Result = ProtoCoreState.Idle;

    [ViewVariables]
    public EntityUid? ShuttleGrid;
}
