using Content.Server._Eclipse.StationEvents;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Interaction;

namespace Content.Server._Eclipse.AdvancedHealth;

/// <summary>Only living growth cores emit exposure; individual growth tiles do not scan the map.</summary>
public sealed class ClinicalEnvironmentSystem : EntitySystem
{
    [Dependency] private readonly AdvancedDiseaseSystem _clinical = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    private float _elapsed;

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 5) return;
        _elapsed = 0;
        var sources = EntityQueryEnumerator<EclipseLivingCoreComponent, TransformComponent>();
        while (sources.MoveNext(out var core, out _, out var transform))
        {
            var hosts = new HashSet<Entity<AdvancedHealthComponent>>();
            _lookup.GetEntitiesInRange(transform.Coordinates, 2.5f, hosts);
            foreach (var host in hosts)
                if (_interaction.InRangeUnobstructed(core, host.Owner, range: 2.5f))
                    _clinical.TryInfect(host, "ClinicalFungalPneumonia");
        }
    }
}
