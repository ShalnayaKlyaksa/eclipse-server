using Content.Server.Station.Systems;
using Content.Server.Tesla.Components;
using Content.Shared.Singularity.Components;

namespace Content.Server.AlertLevel;

public sealed class ContainmentBreachAlertSystem : EntitySystem
{
    private const string EnigmaAlertLevel = "enigma";
    private const float ContainmentSearchRange = 10f;
    private const float UpdateInterval = 2f;

    private static readonly HashSet<string> HigherAlertLevels = new()
    {
        "tau",
        "kappa",
        "sigma",
        "gamma",
        "delta",
        "epsilon",
    };

    [Dependency] private readonly AlertLevelSystem _alertLevel = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private float _accumulator;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        _accumulator -= UpdateInterval;
        CheckLooseContainmentThreats();
    }

    private void CheckLooseContainmentThreats()
    {
        var teslaQuery = EntityQueryEnumerator<TeslaEnergyBallComponent>();
        while (teslaQuery.MoveNext(out var uid, out _))
        {
            if (!HasActiveContainmentNearby(uid))
                TrySetEnigma(uid);
        }

        var singularityQuery = EntityQueryEnumerator<SingularityComponent>();
        while (singularityQuery.MoveNext(out var uid, out var singularity))
        {
            if (singularity.Level > 0 && !HasActiveContainmentNearby(uid))
                TrySetEnigma(uid);
        }
    }

    private bool HasActiveContainmentNearby(EntityUid uid)
    {
        var xform = Transform(uid);
        var mapId = xform.MapID;
        var position = _transform.GetWorldPosition(xform);

        var generators = EntityQueryEnumerator<ContainmentFieldGeneratorComponent, TransformComponent>();
        while (generators.MoveNext(out _, out var generator, out var generatorXform))
        {
            if (!generator.IsConnected || generatorXform.MapID != mapId)
                continue;

            if ((_transform.GetWorldPosition(generatorXform) - position).Length() <= ContainmentSearchRange)
                return true;
        }

        return false;
    }

    private void TrySetEnigma(EntityUid uid)
    {
        var station = _station.GetOwningStation(uid) ?? _station.GetStationInMap(Transform(uid).MapID);
        if (station == null)
            return;

        var currentLevel = _alertLevel.GetLevel(station.Value);
        if (currentLevel == EnigmaAlertLevel || HigherAlertLevels.Contains(currentLevel))
            return;

        _alertLevel.SetLevel(station.Value, EnigmaAlertLevel, true, true, true);
    }
}
