using System.Numerics;
using Content.Shared._Eclipse.StationEvents;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Server._Eclipse.StationEvents;

public sealed partial class EclipseIncidentSystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;

    private void MarkIncomingBearing(EclipseIncidentComponent comp)
    {
        var bearing = MathF.Round(MathF.Atan2(-comp.Direction.Y, -comp.Direction.X) / (MathF.PI / 4)) * (MathF.PI / 4);
        var warning = EnsureComp<IncomingGungnirComponent>(comp.Grid!.Value);
        warning.SourceBearing = new Vector2(MathF.Cos(bearing), MathF.Sin(bearing));
        Dirty(comp.Grid.Value, warning);
        Announce(comp, "eclipse-incident-gungnir-source");
    }

    private void PlayStrikeSound(string file, MapCoordinates position, float volume = -4)
    {
        Audio.PlayStatic(new SoundPathSpecifier($"/Audio/_Eclipse/Weapons/{file}"), Filter.Pvs(position).AddInRange(position, 80),
            _transform.ToCoordinates(position), true, AudioParams.Default.WithVolume(volume).WithMaxDistance(90).WithRolloffFactor(0.8f).WithVariation(0.06f));
    }

    private void StrikeEffect(OrbitalStrikeEffect effect, MapCoordinates position, Vector2 direction, float halfLength = 0, float duration = 2,
        Filter? filter = null)
    {
        RaiseNetworkEvent(new OrbitalStrikeEffectEvent(effect, position, direction, halfLength, duration),
            filter ?? Filter.Pvs(position).AddInRange(position, 80));
        if (effect != OrbitalStrikeEffect.GungnirPassage)
            Spawn("EclipseStrikeFlash", position);
    }

    private void TickGungnir(EntityUid uid, EclipseIncidentComponent comp, GameRuleComponent rule)
    {
        if (comp.Phase == 0)
        {
            comp.StrikeOrigin = _transform.ToMapCoordinates(comp.Target);
            comp.StrikeDirection = _transform.GetWorldRotation(comp.Target.EntityId).RotateVec(comp.Direction);
            var speed = 220f;
            RemComp<IncomingGungnirComponent>(comp.Grid!.Value);
            var viewers = StationSystem.GetInOwningStation(comp.Grid.Value);
            StrikeEffect(OrbitalStrikeEffect.GungnirPassage, comp.StrikeOrigin, comp.StrikeDirection, comp.HalfLength,
                2 * comp.HalfLength / speed, viewers);
            Audio.PlayGlobal(new SoundPathSpecifier("/Audio/_Eclipse/Weapons/gungnir_passage.wav"), viewers, true,
                AudioParams.Default.WithVolume(-2));
            comp.Phase = 1;
            comp.Counter = 0;
            // The visual crosses the whole target before the destruction starts.
            comp.NextAction = Timing.CurTime + TimeSpan.FromSeconds(2 * comp.HalfLength / speed + 0.25);
            Announce(comp, "eclipse-incident-gungnir-passage");
            return;
        }

        // Sweep the entire segment, rather than sampling the moving projectile each frame (which skips tiles at high speed).
        for (var i = 0; i < 6 && comp.Counter <= 2 * comp.HalfLength; i++, comp.Counter++)
        {
            var position = new MapCoordinates(comp.StrikeOrigin.Position + comp.StrikeDirection * (comp.Counter - comp.HalfLength), comp.StrikeOrigin.MapId);
            var hit = DestroyCorridor(position, comp.StrikeWidth / 2);
            if (hit && comp.Counter % 5 == 0)
            {
                StrikeEffect(OrbitalStrikeEffect.GungnirBreach, position, comp.StrikeDirection, duration: 2.5f);
                PlayStrikeSound("hull_rupture.wav", position, -8);
                _explosions.QueueExplosion(position, "Default", comp.Intensity, 4, 12, uid, canCreateVacuum: true);
            }
        }

        comp.NextAction = Timing.CurTime + TimeSpan.FromSeconds(0.12);
        if (comp.Counter > 2 * comp.HalfLength)
            ForceEndSelf(uid, rule);
    }

    private bool DestroyCorridor(MapCoordinates mapPosition, float radius)
    {
        var hit = false;
        // Resolve all intersecting grids afresh: the first part of a cut may already have split the original grid.
        foreach (var entity in _lookup.GetEntitiesInRange(mapPosition, radius, flags: LookupFlags.Uncontained))
        {
            if (IsGone(entity) || HasComp<MapGridComponent>(entity) || HasComp<EclipseIncidentComponent>(entity))
                continue;

            if (HasComp<PhysicsComponent>(entity) && HasComp<DamageableComponent>(entity))
            {
                _damage.TryChangeDamage(entity, new DamageSpecifier { DamageDict = { ["Blunt"] = 800 } }, ignoreResistances: true);
                _destructible.DestroyEntity(entity);
            }
        }

        var grids = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (grids.MoveNext(out var gridUid, out var grid, out var xform))
        {
            if (xform.MapID != mapPosition.MapId)
                continue;
            var local = _transform.ToCoordinates(gridUid, mapPosition);
            var center = _maps.CoordinatesToTile(gridUid, grid, local);
            var tiles = new List<(Vector2i, Tile)>();
            var extent = (int) MathF.Ceiling(radius);
            for (var x = -extent; x <= extent; x++)
            for (var y = -extent; y <= extent; y++)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                var index = center + new Vector2i(x, y);
                if (_maps.TryGetTileRef(gridUid, grid, index, out var tile) && !tile.Tile.IsEmpty)
                    tiles.Add((index, Tile.Empty));
            }
            if (tiles.Count > 0)
            {
                _maps.SetTiles(gridUid, grid, tiles);
                hit = true;
            }
        }
        return hit;
    }

    private void TickHammer(EntityUid uid, EclipseIncidentComponent comp, GameRuleComponent rule)
    {
        if (comp.Phase == 0)
        {
            AnnounceLocation(comp, "eclipse-incident-hammer-ranging");
            HammerImpact(uid, comp, _transform.ToMapCoordinates(comp.Target), ranging: true);
            comp.Phase = 1;
        }
        else
        {
            foreach (var marker in comp.Markers)
            {
                if (Exists(marker))
                {
                    HammerImpact(uid, comp, _transform.GetMapCoordinates(marker));
                }
                QueueDel(marker);
            }
            comp.Markers.Clear();
            comp.Counter++;
            if (comp.Counter >= comp.Salvos)
            {
                Announce(comp, "eclipse-incident-hammer-end");
                ForceEndSelf(uid, rule);
                return;
            }
        }

        foreach (var marker in comp.Markers)
            QueueDel(marker);
        comp.Markers.Clear();
        var perpendicular = new Vector2(-comp.Direction.Y, comp.Direction.X);
        for (var i = -1; i <= 1; i++)
        {
            var position = Offset(comp, comp.Direction * (comp.Counter + 1) * 6 + perpendicular * i * 7);
            comp.Markers.Add(Spawn("EclipseImpactWarning", position));
        }
        comp.NextAction = Timing.CurTime + comp.Interval;
        comp.SalvoCuePlayed = false;
    }

    private void HammerImpact(EntityUid uid, EclipseIncidentComponent comp, MapCoordinates position, bool ranging = false)
    {
        StrikeEffect(OrbitalStrikeEffect.HammerImpact, position, comp.Direction, duration: 3);
        PlayStrikeSound("siege_impact.wav", position, ranging ? -8 : -6);
        if (!ranging)
            DestroyCorridor(position, 1.25f);
        _explosions.QueueExplosion(position, ranging ? "Default" : "EclipseSiege",
            comp.Intensity * (ranging ? 0.2f : 1), 4, ranging ? 16 : 45, uid, maxTileBreak: 3, canCreateVacuum: true);
    }

    private void TickDamocles(EntityUid uid, EclipseIncidentComponent comp, GameRuleComponent rule)
    {
        if (comp.Phase == 0)
        {
            comp.Subject = Spawn("EclipseDamoclesIncoming", Offset(comp, -comp.Direction * comp.HalfLength));
            var direction = _transform.GetWorldRotation(comp.Target.EntityId).RotateVec(comp.Direction);
            var visual = Comp<SiegeProjectileVisualComponent>(comp.Subject.Value);
            visual.Direction = direction;
            Dirty(comp.Subject.Value, visual);
            _physics.SetLinearVelocity(comp.Subject.Value, direction * (comp.HalfLength / 60));
            _physics.SetBodyStatus(comp.Subject.Value, Comp<PhysicsComponent>(comp.Subject.Value), BodyStatus.InAir);
            _transform.SetWorldRotation(comp.Subject.Value, direction.ToWorldAngle());
            comp.Phase = 1;
            comp.Counter = 0;
            AnnounceLocation(comp, "eclipse-incident-damocles-track");
            comp.NextAction = Timing.CurTime + TimeSpan.FromSeconds(1);
            return;
        }

        if (IsGone(comp.Subject))
        {
            Announce(comp, "eclipse-incident-damocles-intercepted");
            ForceEndSelf(uid, rule);
            return;
        }

        // One minute of visible approach; interception destroys the actual approaching object.
        comp.Counter++;
        var fraction = MathF.Min(1, comp.Counter / 60f);
        if (fraction < 1)
        {
            comp.NextAction = Timing.CurTime + TimeSpan.FromSeconds(1);
            return;
        }

        QueueDel(comp.Subject);
        StrikeEffect(OrbitalStrikeEffect.DamoclesImpact, _transform.ToMapCoordinates(comp.Target), comp.Direction, duration: 3.5f);
        PlayStrikeSound("siege_impact.wav", _transform.ToMapCoordinates(comp.Target), -1);
        _explosions.QueueExplosion(_transform.ToMapCoordinates(comp.Target), "Default", 90, 4, 8, uid);
        Spawn("EclipseDamoclesPayload", comp.Target);
        Announce(comp, "eclipse-incident-damocles-landed");
        ForceEndSelf(uid, rule);
    }

    private void OnPayloadInit(Entity<EclipseTimedPayloadComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Deadline = Timing.CurTime + ent.Comp.Delay;
        ent.Comp.Armed = true;
    }

    private void UpdatePayloads()
    {
        var query = EntityQueryEnumerator<EclipseTimedPayloadComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var payload, out var xform))
        {
            if (!payload.Armed || IsGone(uid) || Paused(uid) || Timing.CurTime < payload.Deadline)
                continue;
            payload.Armed = false;
            if (payload.Hatch is { } hatch)
            {
                for (var i = 0; i < payload.HatchCount; i++)
                    Spawn(hatch, xform.Coordinates.Offset(RobustRandom.NextVector2(0.2f)));
            }
            else
            {
                StrikeEffect(OrbitalStrikeEffect.DamoclesImpact, _transform.GetMapCoordinates(uid, xform), Vector2.UnitX, duration: 4);
                PlayStrikeSound("siege_impact.wav", _transform.GetMapCoordinates(uid, xform), -2);
                _explosions.QueueExplosion(_transform.GetMapCoordinates(uid, xform), "Default", payload.Intensity, 3, 30, uid);
            }
            QueueDel(uid);
        }
    }
}
