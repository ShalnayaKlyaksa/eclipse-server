using Content.Shared._Eclipse.Industrial;

namespace Content.Client._Eclipse.Industrial;

public sealed class IndustrialProcessorSystem : SharedIndustrialProcessorSystem
{
    protected override void TryStartProcessing(Entity<IndustrialProcessorComponent> ent)
    {
        // Server-authoritative.
    }

    protected override void EjectOutput(Entity<IndustrialProcessorComponent> ent, EntityUid item, EntityUid user)
    {
        // Server-authoritative.
    }

    protected override void UpdateAppearance(Entity<IndustrialProcessorComponent> ent)
    {
        // Server-authoritative; client uses appearance replication.
    }
}
