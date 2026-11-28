namespace Content.Shared._Eclipse.Industrial;

/// <summary>
/// Auto-binds heat-input ports when heat buffers or pipes are placed against heat-powered machines.
/// </summary>
public sealed class SharedIndustrialHeatConnectSystem : EntitySystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;

    public void TryAutoBindAdjacentProcessors(EntityUid source)
    {
        foreach (var (processor, face) in IndustrialHeatLinkHelper.GetAdjacentProcessors(source, EntityManager, _map))
            IndustrialProcessorConnectionHelper.TryAutoBindHeatPort(processor, source, face, EntityManager, _map);
    }

    public void TryAutoBindPortsFromAdjacentHeatConnections(Entity<IndustrialProcessorComponent> processor)
    {
        if (!HasComp<IndustrialHeatPoweredComponent>(processor))
            return;

        foreach (var direction in SharedIndustrialProcessorSystem.CardinalDirections)
        {
            if (IndustrialProcessorConnectionHelper.TryFindHeatConnectionOnFace(
                    processor, direction, EntityManager, _map, out var source))
            {
                IndustrialProcessorConnectionHelper.TryAutoBindHeatPort(processor, source, direction, EntityManager, _map);
            }
        }
    }
}
