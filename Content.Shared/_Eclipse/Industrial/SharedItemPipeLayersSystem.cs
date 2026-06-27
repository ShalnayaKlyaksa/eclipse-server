using Content.Shared._Eclipse.Industrial;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;

namespace Content.Shared._Eclipse.Industrial;

public abstract partial class SharedItemPipeLayersSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedToolSystem _tools = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemPipeLayersComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ItemPipeLayersComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<ItemPipeLayersComponent, ItemPipeCycleLayerCompletedEvent>(OnCycleLayerCompleted);
        SubscribeLocalEvent<ItemPipeLayersComponent, ItemPipeSetLayerCompletedEvent>(OnSetLayerCompleted);
    }

    private void OnExamined(Entity<ItemPipeLayersComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("industrial-item-pipe-examine-layer",
            ("layer", GetPipeLayerName(ent.Comp.CurrentPipeLayer))));
    }

    private void OnInteractUsing(Entity<ItemPipeLayersComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.PipeLayersLocked || ent.Comp.NumberOfPipeLayers <= 1)
            return;

        if (!_tools.HasQuality(args.Used, ent.Comp.Tool))
            return;

        if (!TryComp<ToolComponent>(args.Used, out var tool))
            return;

        args.Handled = _tools.UseTool(args.Used, args.User, ent, 0.5f, ent.Comp.Tool, new ItemPipeCycleLayerCompletedEvent(), toolComponent: tool);
    }

    private void OnCycleLayerCompleted(Entity<ItemPipeLayersComponent> ent, ref ItemPipeCycleLayerCompletedEvent args)
    {
        if (args.Cancelled)
            return;

        var next = (ItemPipeLayer)(((int)ent.Comp.CurrentPipeLayer + 1) % ent.Comp.NumberOfPipeLayers);
        SetPipeLayer(ent, next, args.User);
    }

    private void OnSetLayerCompleted(Entity<ItemPipeLayersComponent> ent, ref ItemPipeSetLayerCompletedEvent args)
    {
        if (args.Cancelled)
            return;

        SetPipeLayer(ent, args.PipeLayer, args.User);
    }

    protected virtual void SetPipeLayer(Entity<ItemPipeLayersComponent> ent, ItemPipeLayer layer, EntityUid? user = null)
    {
        ent.Comp.CurrentPipeLayer = layer;
        Dirty(ent);
        _popup.PopupClient(Loc.GetString("industrial-item-pipe-layer-switched",
            ("layer", GetPipeLayerName(layer))), ent, user);
        OnLayerChanged(ent);
    }

    protected virtual void OnLayerChanged(Entity<ItemPipeLayersComponent> ent) { }

    private string GetPipeLayerName(ItemPipeLayer layer)
    {
        return Loc.GetString(layer switch
        {
            ItemPipeLayer.Secondary => "industrial-item-pipe-layer-secondary",
            ItemPipeLayer.Tertiary => "industrial-item-pipe-layer-tertiary",
            _ => "industrial-item-pipe-layer-primary",
        });
    }

    public bool TryGetAlternativePrototype(ItemPipeLayersComponent layers, ItemPipeLayer layer, out EntProtoId protoId)
    {
        protoId = default!;
        if (!layers.AlternativePrototypes.TryGetValue(layer, out var alt))
            return false;

        protoId = alt;
        return _proto.HasIndex<EntityPrototype>(protoId);
    }
}
