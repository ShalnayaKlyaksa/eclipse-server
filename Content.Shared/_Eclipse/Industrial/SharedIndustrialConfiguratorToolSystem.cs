using Content.Shared.Construction.Components;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>
/// Restricts the industrial port wrench to Eclipse piping/port mechanics only.
/// </summary>
public sealed class SharedIndustrialConfiguratorToolSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AnchorableComponent, UnanchorAttemptEvent>(OnUnanchorAttempt);
    }

    private void OnUnanchorAttempt(Entity<AnchorableComponent> ent, ref UnanchorAttemptEvent args)
    {
        if (!HasComp<IndustrialConfiguratorComponent>(args.Tool))
            return;

        if (HasComp<ItemPipeComponent>(ent) || HasComp<LiquidPipeComponent>(ent))
            return;

        args.Cancel();
    }
}
