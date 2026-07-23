using Content.Shared._Eclipse.Industrial;
using Content.Shared.Construction.Components;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Server._Eclipse.Industrial;

public sealed partial class ItemPipeSystem : SharedItemPipeSystem
{
    [Dependency] private ItemPipeNetworkSystem _network = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemPipeComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<ItemPipeComponent, UserUnanchoredEvent>(OnUserUnanchored);
    }

    private void OnUserUnanchored(Entity<ItemPipeComponent> ent, ref UserUnanchoredEvent args)
    {
        _popup.PopupEntity(Loc.GetString("industrial-item-pipe-detached"), ent, args.User);
        QueueDel(ent);
    }

    private void OnMove(Entity<ItemPipeComponent> ent, ref MoveEvent args)
    {
        if (args.NewRotation.EqualsApprox(args.OldRotation))
            return;

        UpdateConnections(ent);
        UpdateAdjacentConnections(ent);
        OnPipeTopologyChanged(ent);
    }

    protected override void OnPipeTopologyChanged(Entity<ItemPipeComponent> ent)
    {
        _network.RebuildNetworkFrom(ent);
        _network.RebuildAdjacentPipeNetworks(ent);
    }

    protected override void OnPipeRemoved(Entity<ItemPipeComponent> ent)
    {
        _network.HandlePipeRemoved(ent);
    }

    protected override void TryAutoBindAdjacentProcessors(Entity<ItemPipeComponent> ent)
    {
        var procConnect = EntityManager.System<SharedIndustrialProcessorPipeConnectSystem>();
        if (!procConnect.TryAutoBindAdjacentProcessors(ent))
            return;

        UpdateConnections(ent);
        UpdateAdjacentConnections(ent);
        OnPipeTopologyChanged(ent);
    }

    protected override void OnProcessorAdjacentChanged(EntityUid processor)
    {
        base.OnProcessorAdjacentChanged(processor);
        _network.RebuildNetworksNearProcessor((processor, Comp<IndustrialProcessorComponent>(processor)));
    }

    protected override void PushNetworkExamine(Entity<ItemPipeComponent> ent, ExaminedEvent args)
    {
        if (ent.Comp.NetworkId < 0)
        {
            args.PushMarkup(Loc.GetString("industrial-pipe-no-network"));
            return;
        }

        if (_network.TryGetNetwork(ent.Comp.NetworkId, out var network))
        {
            args.PushMarkup(Loc.GetString("industrial-pipe-examine-network",
                ("pipes", network.Pipes.Count), ("tier", GetPipeTierName(network.EffectiveTier))));
        }
    }
}
