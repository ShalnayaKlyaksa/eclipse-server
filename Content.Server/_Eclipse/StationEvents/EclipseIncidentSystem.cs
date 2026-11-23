using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Destructible;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Spreader;
using Content.Server.StationEvents.Events;
using Content.Shared.GameTicking.Components;
using Content.Shared.Maps;
using Content.Shared._Eclipse.StationEvents;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Eclipse.StationEvents;

/// <summary>Station incidents share targeting and scheduling, but keep their own consequences.</summary>
public sealed partial class EclipseIncidentSystem : StationEventSystem<EclipseIncidentComponent>
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly MapSystem _maps = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly ITileDefinitionManager _tiles = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly ExplosionSystem _explosions = default!;
    [Dependency] private readonly DestructibleSystem _destructible = default!;
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EclipseTimedPayloadComponent, MapInitEvent>(OnPayloadInit);
        SubscribeLocalEvent<EclipseColdSourceComponent, MapInitEvent>(OnColdInit);
        SubscribeLocalEvent<EclipsePhototaxisComponent, MapInitEvent>(OnMigrantInit);
        SubscribeLocalEvent<EclipseLivingGrowthComponent, SpreadNeighborsEvent>(OnGrowthSpread);
        SubscribeLocalEvent<EclipseLivingGrowthComponent, EntityTerminatingEvent>(OnGrowthRemoved);
    }

    protected override void Added(EntityUid uid, EclipseIncidentComponent comp, GameRuleComponent rule, GameRuleAddedEvent args)
    {
        // Kinetic cuts need the main grid's bounds, not an arbitrary breathable tile. Random tile sampling
        // used to occasionally fail on sparse stations and silently discard the first admin invocation.
        if (comp.Kind == EclipseIncidentKind.Gungnir)
        {
            if (!TryGetRandomStation(out var station,
                    candidate => StationSystem.GetLargestGrid(candidate) is { } grid && TryComp<MapGridComponent>(grid, out var mapGrid) && !mapGrid.LocalAABB.IsEmpty())
                || StationSystem.GetLargestGrid(station.Value) is not { } mainGrid)
            {
                Sawmill.Warning("Gungnir could not start: no event-eligible station with a nonempty grid exists.");
                ForceEndSelf(uid, rule);
                return;
            }
            comp.Station = station;
            comp.Grid = mainGrid;
            comp.Target = new EntityCoordinates(mainGrid, Comp<MapGridComponent>(mainGrid).LocalAABB.Center);
        }
        else
        {
            if (!TryFindRandomTile(out _, out var station, out var grid, out var coords))
            {
                ForceEndSelf(uid, rule);
                return;
            }
            comp.Station = station;
            comp.Grid = grid;
            comp.Target = coords;
        }
        comp.Direction = RobustRandom.NextAngle().ToVec();
        comp.HalfLength = Comp<MapGridComponent>(comp.Grid.Value).LocalAABB.Size.Length() + 12;
        base.Added(uid, comp, rule, args);
    }

    protected override void Started(EntityUid uid, EclipseIncidentComponent comp, GameRuleComponent rule, GameRuleStartedEvent args)
    {
        base.Started(uid, comp, rule, args);
        if (comp.Grid == null || !Exists(comp.Grid))
        {
            ForceEndSelf(uid, rule);
            return;
        }

        comp.NextAction = Timing.CurTime + comp.Warning;
        switch (comp.Kind)
        {
            case EclipseIncidentKind.Gungnir:
                MarkIncomingBearing(comp);
                break;
            case EclipseIncidentKind.Hammer:
            case EclipseIncidentKind.Damocles:
                comp.Markers.Add(Spawn("EclipseImpactWarning", comp.Target));
                break;
            case EclipseIncidentKind.ZeroPoint:
                comp.Subject = Spawn("EclipseZeroPoint", comp.Target);
                AnnounceLocation(comp, "eclipse-incident-source-location");
                break;
            case EclipseIncidentKind.LivingHull:
                StartLivingHull(comp);
                break;
        }
    }

    protected override void ActiveTick(EntityUid uid, EclipseIncidentComponent comp, GameRuleComponent rule, float frameTime)
    {
        var sweeping = comp.Kind == EclipseIncidentKind.Gungnir && comp.Phase == 1;
        if (!sweeping && (comp.Grid == null || !Exists(comp.Grid) || !comp.Target.IsValid(EntityManager)))
        {
            ForceEndSelf(uid, rule);
            return;
        }

        if (comp.Kind == EclipseIncidentKind.Hammer && !comp.SalvoCuePlayed
            && Timing.CurTime >= comp.NextAction - TimeSpan.FromSeconds(1.4))
        {
            PlayStrikeSound("hammer_incoming.wav", _transform.ToMapCoordinates(comp.Target), -3);
            comp.SalvoCuePlayed = true;
        }

        if (Timing.CurTime < comp.NextAction)
            return;

        switch (comp.Kind)
        {
            case EclipseIncidentKind.Gungnir:
                TickGungnir(uid, comp, rule);
                break;
            case EclipseIncidentKind.Hammer:
                TickHammer(uid, comp, rule);
                break;
            case EclipseIncidentKind.Damocles:
                TickDamocles(uid, comp, rule);
                break;
            case EclipseIncidentKind.DistressVessel:
            case EclipseIncidentKind.OtherShift:
                SpawnVessel(comp);
                ForceEndSelf(uid, rule);
                break;
            case EclipseIncidentKind.ZeroPoint:
                if (IsGone(comp.Subject))
                    ForceEndSelf(uid, rule);
                else
                    comp.NextAction = Timing.CurTime + TimeSpan.FromSeconds(2);
                break;
            case EclipseIncidentKind.LivingHull:
                TickLivingHull(uid, comp, rule);
                break;
            case EclipseIncidentKind.BlackTide:
                TickBlackTide(uid, comp, rule);
                break;
        }
    }

    protected override void Ended(EntityUid uid, EclipseIncidentComponent comp, GameRuleComponent rule, GameRuleEndedEvent args)
    {
        foreach (var marker in comp.Markers)
            QueueDel(marker);
        comp.Markers.Clear();
        if (comp.Kind == EclipseIncidentKind.Gungnir && Exists(comp.Grid))
            RemComp<IncomingGungnirComponent>(comp.Grid.Value);

        // Ending an event stops its sources, while leaving wreckage and vegetation for the crew to clear.
        if (comp.Kind is EclipseIncidentKind.LivingHull or EclipseIncidentKind.ZeroPoint or EclipseIncidentKind.Damocles)
            QueueDel(comp.Subject);

        base.Ended(uid, comp, rule, args);
    }

    private void Announce(EclipseIncidentComponent comp, string key, params (string, object)[] args)
    {
        if (comp.Station is { } station && Exists(station))
            ChatSystem.DispatchStationAnnouncement(station, Loc.GetString(key, args), colorOverride: Color.Gold);
    }

    private void AnnounceLocation(EclipseIncidentComponent comp, string key)
    {
        var map = _transform.ToMapCoordinates(comp.Target);
        Announce(comp, key, ("x", (int) map.X), ("y", (int) map.Y));
    }

    private EntityCoordinates Offset(EclipseIncidentComponent comp, Vector2 offset) => comp.Target.Offset(offset);

    private bool IsGone(EntityUid? uid) => !Exists(uid) || EntityManager.IsQueuedForDeletion(uid.Value);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdatePayloads();
        UpdateColdSources();
        UpdatePhototaxis();
    }
}
