using Content.Shared.Eclipse.LightDestroyer;
using Content.Shared.Eclipse.LightDestroyer.Components;
using Robust.Client.GameObjects;

namespace Content.Client.Eclipse.LightDestroyer;

public sealed class LightDestroyerSystem : SharedLightDestroyerSystem
{
    [Dependency] private readonly PointLightSystem _pointLight = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DestroyedByLightDestroyerComponent, ComponentInit>(OnDestroyedInit);
    }

    private void OnDestroyedInit(Entity<DestroyedByLightDestroyerComponent> ent, ref ComponentInit args)
    {
        _pointLight.SetEnabled(ent, false);
    }
}
