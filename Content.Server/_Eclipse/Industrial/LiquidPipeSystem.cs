using Content.Shared._Eclipse.Industrial;
using Content.Shared.Construction.Components;
using Content.Shared.Examine;
using Content.Shared.Popups;

namespace Content.Server._Eclipse.Industrial;

public sealed class LiquidPipeSystem : SharedLiquidPipeSystem
{
    [Dependency] private readonly LiquidPipeNetworkSystem _network = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LiquidPipeComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<LiquidPipeComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<LiquidPipeComponent, UserUnanchoredEvent>(OnUserUnanchored);
    }

    private void OnUserUnanchored(Entity<LiquidPipeComponent> ent, ref UserUnanchoredEvent args)
    {
        _popup.PopupEntity(Loc.GetString("industrial-liquid-pipe-detached"), ent, args.User);
        QueueDel(ent);
    }

    private void OnInit<T>(Entity<LiquidPipeComponent> ent, ref T args)
    {
        ApplyTierSettings(ent);
        _network.RebuildNetworkFrom(ent);
    }

    private void ApplyTierSettings(Entity<LiquidPipeComponent> ent)
    {
        var specs = PipeTierHelper.GetSpecs(ent.Comp.Tier);
        ent.Comp.ThroughputPerSecond = specs.ThroughputPerSecond;
        ent.Comp.TransferDelay = specs.TransferDelay;
        Dirty(ent);
    }

    protected override void PushNetworkExamine(Entity<LiquidPipeComponent> ent, ExaminedEvent args)
    {
        if (ent.Comp.NetworkId < 0)
        {
            args.PushMarkup(Loc.GetString("industrial-liquid-pipe-examine-disconnected"));
            return;
        }

        if (_network.TryGetNetwork(ent.Comp.NetworkId, out var network))
        {
            args.PushMarkup(Loc.GetString("industrial-liquid-pipe-examine-network",
                ("id", network.Id), ("pipes", network.Pipes.Count)));
        }
    }
}
