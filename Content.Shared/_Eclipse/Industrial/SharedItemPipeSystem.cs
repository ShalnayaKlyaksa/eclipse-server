using Content.Shared._Eclipse.Industrial;
using Content.Shared.Atmos;
using Content.Shared.Construction.Components;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.Shared._Eclipse.Industrial;

public abstract partial class SharedItemPipeSystem : EntitySystem
{
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedToolSystem _tools = default!;
    [Dependency] protected readonly SharedMapSystem Map = default!;
    [Dependency] protected readonly SharedTransformSystem TransformSys = default!;
    [Dependency] protected readonly SharedAppearanceSystem Appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemPipeComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ItemPipeComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<ItemPipeComponent, AfterInteractUsingEvent>(OnAfterInteractUsing);
        SubscribeLocalEvent<ItemPipeComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<ItemPipeComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<ItemPipeComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ItemPipeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ItemPipeComponent, AnchorAttemptEvent>(OnAnchorAttempt);
    }

    private void OnStartup(Entity<ItemPipeComponent> ent, ref ComponentStartup args)
    {
        ApplyTierSettings(ent);
        if (RejectInvalidPipePlacement(ent))
            return;

        UpdateConnections(ent);
        UpdateAdjacentConnections(ent);
    }

    private void OnMapInit(Entity<ItemPipeComponent> ent, ref MapInitEvent args)
    {
        if (RejectInvalidPipePlacement(ent))
            return;

        UpdateConnections(ent);
        UpdateAdjacentConnections(ent);
    }

    private void OnAnchorAttempt(Entity<ItemPipeComponent> ent, ref AnchorAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var xform = Transform(ent);
        if (xform.GridUid is { } gridUid && TryComp<MapGridComponent>(gridUid, out var grid))
        {
            var indices = Map.TileIndicesFor(gridUid, grid, xform.Coordinates);
            if (IndustrialPipingOccupancyHelper.IsPipeBlockedByProcessor(
                    ent, gridUid, grid, indices, EntityManager, Map, TransformSys) ||
                IndustrialPipingOccupancyHelper.TileBlocksItemPipePlacement(ent, gridUid, grid, indices, EntityManager, Map))
            {
                _popup.PopupClient(Loc.GetString("industrial-piping-tile-occupied"), ent, args.User);
                args.Cancel();
                return;
            }
        }

        if (CheckOverlap(ent))
        {
            _popup.PopupClient(Loc.GetString("industrial-item-pipe-overlap-blocked"), ent, args.User);
            args.Cancel();
        }
    }

    private void OnAnchorChanged(Entity<ItemPipeComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored && CheckOverlap(ent))
        {
            _popup.PopupEntity(Loc.GetString("industrial-item-pipe-overlap-blocked"), ent);
            var xform = Transform(ent);
            xform.Anchored = false;
            Dirty(ent, xform);
            return;
        }

        if (args.Anchored && RejectInvalidPipePlacement(ent))
            return;

        UpdateConnections(ent);
        UpdateAdjacentConnections(ent);
        OnPipeTopologyChanged(ent);

        if (args.Anchored)
            TryAutoBindAdjacentProcessors(ent);
    }

    protected virtual void TryAutoBindAdjacentProcessors(Entity<ItemPipeComponent> ent) { }

    private void OnTerminating(Entity<ItemPipeComponent> ent, ref EntityTerminatingEvent args)
    {
        UpdateAdjacentConnections(ent);
        OnPipeRemoved(ent);
    }

    protected virtual void OnPipeRemoved(Entity<ItemPipeComponent> ent) { }

    protected virtual void OnPipeTopologyChanged(Entity<ItemPipeComponent> ent) { }

    private void OnAfterInteractUsing(Entity<ItemPipeComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach || !_tools.HasQuality(args.Used, SharedToolSystem.PulseQuality))
            return;

        CycleTransferMode(ent, args.User);
        args.Handled = true;
    }

    private void OnGetVerbs(Entity<ItemPipeComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var held = args.Using;
        if (held == null || !_tools.HasQuality(held.Value, SharedToolSystem.PulseQuality))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("industrial-pipe-toggle-mode"),
            Act = () => CycleTransferMode(ent, user),
        });
    }

    private void OnExamined(Entity<ItemPipeComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("industrial-pipe-examine-tier", ("tier", GetPipeTierName(ent.Comp.Tier))));
        args.PushMarkup(Loc.GetString("industrial-pipe-examine-layer",
            ("layer", GetPipeLayerName(ItemPipeConnectionHelper.GetLayer(ent, EntityManager)))));
        args.PushMarkup(Loc.GetString("industrial-pipe-examine-mode", ("mode", GetTransferModeName(ent.Comp.TransferMode))));
        args.PushMarkup(Loc.GetString("industrial-pipe-examine-throughput",
            ("throughput", ent.Comp.ThroughputPerSecond)));
        args.PushMarkup(Loc.GetString("industrial-item-pipe-examine-detach"));
        PushNetworkExamine(ent, args);
    }

    protected virtual void PushNetworkExamine(Entity<ItemPipeComponent> ent, ExaminedEvent args) { }

    public void ApplyTierSettings(Entity<ItemPipeComponent> ent)
    {
        var specs = PipeTierHelper.GetSpecs(ent.Comp.Tier);
        ent.Comp.ThroughputPerSecond = specs.ThroughputPerSecond;
        ent.Comp.TransferDelay = specs.TransferDelay;
        Dirty(ent);
    }

    public void UpdateConnections(Entity<ItemPipeComponent> ent)
    {
        var connected = ItemPipeConnectionHelper.GetConnectedDirections(ent, EntityManager, Map);
        if (ent.Comp.ConnectedDirections != connected)
        {
            ent.Comp.ConnectedDirections = connected;
            Dirty(ent);
        }

        Appearance.SetData(ent, ItemPipeVisuals.ConnectedDirections, (int) connected);
    }

    public void UpdateAdjacentConnections(Entity<ItemPipeComponent> ent)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not EntityUid gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var pos = Map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        foreach (var direction in ItemPipeConnectionHelper.CardinalDirections)
        {
            var enumerator = Map.GetAnchoredEntitiesEnumerator(gridUid, grid, pos.Offset(direction));
            while (enumerator.MoveNext(out var entity) && entity != null)
            {
                if (entity == ent.Owner)
                    continue;

                if (TryComp<ItemPipeComponent>(entity, out var other))
                    UpdateConnections((entity.Value, other));

                if (HasComp<IndustrialProcessorComponent>(entity))
                    OnProcessorAdjacentChanged(entity.Value);
            }
        }
    }

    protected virtual void OnProcessorAdjacentChanged(EntityUid processor) { }

    /// <summary>
    /// Returns true when the pipe was rejected for overlapping a machine tile or body.
    /// </summary>
    protected virtual bool RejectInvalidPipePlacement(Entity<ItemPipeComponent> ent)
    {
        var xform = Transform(ent);
        if (!xform.Anchored || xform.GridUid is not EntityUid gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return false;
        }

        var indices = Map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        if (!IndustrialPipingOccupancyHelper.IsPipeBlockedByProcessor(
                ent, gridUid, grid, indices, EntityManager, Map, TransformSys))
        {
            return false;
        }

        _popup.PopupEntity(Loc.GetString("industrial-piping-tile-occupied"), ent);
        xform.Anchored = false;
        Dirty(ent, xform);
        return true;
    }

    public bool CheckOverlap(Entity<ItemPipeComponent> ent)
    {
        if (!TryComp<ItemPipeRestrictOverlapComponent>(ent, out _))
            return false;

        var xform = Transform(ent);
        if (xform.GridUid is not EntityUid gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return false;

        var layer = ItemPipeConnectionHelper.GetLayer(ent, EntityManager);
        var mask = ItemPipeConnectionHelper.GetRotatedMask(ent, EntityManager);
        var indices = Map.TileIndicesFor(gridUid, grid, xform.Coordinates);

        var enumerator = Map.GetAnchoredEntitiesEnumerator(gridUid, grid, indices);
        while (enumerator.MoveNext(out var other) && other != null)
        {
            if (other == ent.Owner || !TryComp<ItemPipeComponent>(other, out _))
                continue;

            if (ItemPipeConnectionHelper.GetLayer(other.Value, EntityManager) != layer)
                continue;

            var otherMask = ItemPipeConnectionHelper.GetRotatedMask(other.Value, EntityManager);
            if (ItemPipeConnectionHelper.MasksOverlap(mask, otherMask))
                return true;
        }

        return false;
    }

    public void CycleTransferMode(Entity<ItemPipeComponent> ent, EntityUid user)
    {
        ent.Comp.TransferMode = ent.Comp.TransferMode switch
        {
            PipeTransferMode.Transit => PipeTransferMode.Extract,
            PipeTransferMode.Extract => PipeTransferMode.Insert,
            _ => PipeTransferMode.Transit,
        };

        Dirty(ent);
        _popup.PopupClient(Loc.GetString("industrial-pipe-mode-switched",
            ("mode", GetTransferModeName(ent.Comp.TransferMode))), ent, user);
    }

    public string GetTransferModeName(PipeTransferMode mode)
    {
        return Loc.GetString(mode switch
        {
            PipeTransferMode.Extract => "industrial-pipe-mode-extract",
            PipeTransferMode.Insert => "industrial-pipe-mode-insert",
            _ => "industrial-pipe-mode-transit",
        });
    }

    public string GetPipeTierName(PipeTier tier)
    {
        return Loc.GetString(tier switch
        {
            PipeTier.Industrial => "industrial-item-pipe-tier-mp",
            PipeTier.Perfect => "industrial-item-pipe-tier-hp",
            _ => "industrial-item-pipe-tier-lp",
        });
    }

    public string GetPipeLayerName(ItemPipeLayer layer)
    {
        return Loc.GetString(layer switch
        {
            ItemPipeLayer.Secondary => "industrial-item-pipe-layer-secondary",
            ItemPipeLayer.Tertiary => "industrial-item-pipe-layer-tertiary",
            _ => "industrial-item-pipe-layer-primary",
        });
    }
}
