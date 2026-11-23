using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.AdvancedHealth;

/// <summary>Initialises real organ entities once; no per-tick anatomy simulation.</summary>
public sealed class AdvancedHumanAnatomySystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AdvancedHumanAnatomyComponent, MapInitEvent>(OnMapInit, after: [typeof(InitialBodySystem)]);
        SubscribeLocalEvent<AdvancedHumanAnatomyComponent, SexChangedEvent>(OnSexChanged);
    }

    private void OnMapInit(Entity<AdvancedHumanAnatomyComponent> ent, ref MapInitEvent args)
    {
        if (!HasComp<HumanoidProfileComponent>(ent)
            || !_containers.TryGetContainer(ent, BodyComponent.ContainerID, out var body)) return;

        // Follow the installed anatomy rather than a species ID. Slimes, plants,
        // skeletons and synthetic bodies do not have this digestive organ set.
        var categories = new HashSet<string>();
        foreach (var uid in body.ContainedEntities)
            if (TryComp<OrganComponent>(uid, out var organ) && organ.Category is { } category)
                categories.Add(category.Id);
        if (!categories.IsSupersetOf(["Stomach", "Liver", "Kidneys"])) return;

        ent.Comp.Ready = true;
        if (!categories.Contains("Intestines")) AddOrgan(ent, "EclipseOrganHumanIntestines");
        Synchronize(ent);
    }

    private void OnSexChanged(Entity<AdvancedHumanAnatomyComponent> ent, ref SexChangedEvent args)
    {
        if (ent.Comp.Ready) Synchronize(ent);
    }

    private void Synchronize(Entity<AdvancedHumanAnatomyComponent> ent)
    {
        if (!TryComp<HumanoidProfileComponent>(ent, out var profile)
            || !_containers.TryGetContainer(ent, BodyComponent.ContainerID, out var body)) return;

        var sex = _prototypes.TryIndex(profile.Species, out var species) && species.Sexes.Contains(profile.Sex)
            ? profile.Sex : Sex.Unsexed;
        if (ent.Comp.AppliedSex == sex) return;
        foreach (var organ in ent.Comp.ReproductiveOrgans)
            if (Exists(organ) && body.Contains(organ)) Del(organ);
        ent.Comp.ReproductiveOrgans.Clear();
        ent.Comp.AppliedSex = sex;
        var organs = sex switch
        {
            Sex.Male => new[] { "EclipseOrganHumanProstate" },
            Sex.Female => new[] { "EclipseOrganHumanUterus", "EclipseOrganHumanOvaryLeft",
                "EclipseOrganHumanOvaryRight", "EclipseOrganHumanVaginalCanal" },
            _ => Array.Empty<string>(),
        };
        foreach (var prototype in organs)
            if (AddOrgan(ent, prototype) is { } organ) ent.Comp.ReproductiveOrgans.Add(organ);
    }

    private EntityUid? AddOrgan(EntityUid body, string prototype)
    {
        if (!_containers.TryGetContainer(body, BodyComponent.ContainerID, out var container)) return null;
        var organ = Spawn(prototype, Transform(body).Coordinates);
        if (_containers.Insert(organ, container)) return organ;
        Del(organ);
        return null;
    }
}
