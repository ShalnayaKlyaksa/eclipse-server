using Content.Shared.Examine;

namespace Content.Shared._Eclipse.Industrial;

public abstract class SharedLiquidPipeSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LiquidPipeComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<LiquidPipeComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("industrial-liquid-pipe-examine-tier",
            ("tier", GetPipeTierName(ent.Comp.Tier))));
        args.PushMarkup(Loc.GetString("industrial-liquid-pipe-examine-throughput",
            ("throughput", ent.Comp.ThroughputPerSecond)));
        PushNetworkExamine(ent, args);
    }

    protected virtual void PushNetworkExamine(Entity<LiquidPipeComponent> ent, ExaminedEvent args) { }

    private string GetPipeTierName(PipeTier tier)
    {
        return Loc.GetString(tier switch
        {
            PipeTier.Industrial => "industrial-machine-tier-industrial",
            PipeTier.Perfect => "industrial-machine-tier-perfect",
            _ => "industrial-machine-tier-basic",
        });
    }
}
