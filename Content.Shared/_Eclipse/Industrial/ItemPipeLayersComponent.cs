using Content.Shared.DoAfter;
using Content.Shared.Tools;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedItemPipeLayersSystem), Other = AccessPermissions.ReadWriteExecute)]
public sealed partial class ItemPipeLayersComponent : Component
{
    [DataField]
    public byte NumberOfPipeLayers = 3;

    [DataField, AutoNetworkedField]
    public ItemPipeLayer CurrentPipeLayer = ItemPipeLayer.Primary;

    [DataField]
    public bool PipeLayersLocked;

    [DataField]
    public ProtoId<ToolQualityPrototype> Tool = "Screwing";

    [DataField]
    public Dictionary<ItemPipeLayer, EntProtoId> AlternativePrototypes = new();

    [DataField]
    public Dictionary<ItemPipeLayer, string> SpriteRsiPaths = new();
}

[Serializable, NetSerializable]
public sealed partial class ItemPipeSetLayerCompletedEvent : SimpleDoAfterEvent
{
    public ItemPipeLayer PipeLayer;

    public ItemPipeSetLayerCompletedEvent(ItemPipeLayer pipeLayer)
    {
        PipeLayer = pipeLayer;
    }
}

[Serializable, NetSerializable]
public sealed partial class ItemPipeCycleLayerCompletedEvent : SimpleDoAfterEvent;
