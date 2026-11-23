using Content.Server.StationEvents.Events;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Eclipse.AdvancedHealth;

[RegisterComponent]
public sealed partial class ClinicalOutbreakComponent : Component
{
    [DataField] public List<ProtoId<AdvancedDiseasePrototype>> Conditions = new() { "SpaceFlu", "ClinicalGastroenteritis", "ClinicalCellulitis" };
}

/// <summary>A single patient starts a clinical incident; no station-wide instant infection.</summary>
public sealed class ClinicalOutbreakSystem : StationEventSystem<ClinicalOutbreakComponent>
{
    [Dependency] private readonly AdvancedDiseaseSystem _clinical = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    protected override void Started(EntityUid uid, ClinicalOutbreakComponent comp, GameRuleComponent rule, GameRuleStartedEvent args)
    {
        if (!TryGetRandomStation(out var station) || comp.Conditions.Count == 0) return;
        var candidates = new List<EntityUid>();
        var query = EntityQueryEnumerator<ActorComponent, AdvancedHealthComponent, TransformComponent>();
        while (query.MoveNext(out var patient, out _, out _, out var transform))
            if (StationSystem.GetOwningStation(patient, transform) == station) candidates.Add(patient);
        _random.Shuffle(candidates);
        foreach (var patient in candidates)
            if (_clinical.ApplyCondition(patient, _random.Pick(comp.Conditions), 0.25f))
            {
                ChatSystem.DispatchStationAnnouncement(station.Value, Loc.GetString("clinical-outbreak-announcement"));
                return;
            }
    }
}
