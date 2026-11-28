using Robust.Shared.Prototypes;

namespace Content.Server.Power.Components;

[RegisterComponent]
public sealed partial class IndustrialPipePackComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Pack;
}
