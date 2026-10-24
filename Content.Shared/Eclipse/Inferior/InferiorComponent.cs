using Content.Shared.Eclipse.Inferior;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Eclipse.Inferior.Components;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedInferiorSystem))]
public sealed partial class InferiorComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public EntityUid? Overlord = null;
    [DataField]
    public SoundSpecifier InfStartSound = new SoundPathSpecifier("/Audio/Ambience/Antag/headrev_start.ogg");
    [DataField]
    public HashSet<ProtoId<NpcFactionPrototype>>? OldFactions;
}
