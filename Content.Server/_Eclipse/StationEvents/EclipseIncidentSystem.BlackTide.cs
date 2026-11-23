using System.Numerics;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared.GameTicking.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Eclipse.StationEvents;

public sealed partial class EclipseIncidentSystem
{
    [Dependency] private readonly NPCSteeringSystem _steering = default!;

    private void OnMigrantInit(Entity<EclipsePhototaxisComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Expires = Timing.CurTime + TimeSpan.FromMinutes(6);
    }

    private void TickBlackTide(EntityUid uid, EclipseIncidentComponent comp, GameRuleComponent rule)
    {
        // A finite migration: three waves, with a global population cap per event.
        if (comp.Counter >= 3)
        {
            Announce(comp, "eclipse-incident-tide-end");
            ForceEndSelf(uid, rule);
            return;
        }

        var origin = _transform.ToMapCoordinates(comp.Target);
        var direction = _transform.GetWorldRotation(comp.Target.EntityId).RotateVec(comp.Direction);
        var perpendicular = new Vector2(-direction.Y, direction.X);
        for (var i = 0; i < 6; i++)
        {
            var coordinates = new MapCoordinates(origin.Position - direction * comp.HalfLength + perpendicular * (i - 2.5f) * 7, origin.MapId);
            var creature = Spawn("EclipseMobTideMoth", coordinates);
            var phototaxis = Comp<EclipsePhototaxisComponent>(creature);
            phototaxis.Expires = Timing.CurTime + TimeSpan.FromMinutes(6);
            phototaxis.MigrationDestination = comp.Target;
            _steering.Register(creature, comp.Target);
        }
        comp.Counter++;
        comp.NextAction = Timing.CurTime + TimeSpan.FromSeconds(35);
    }

    private void UpdatePhototaxis()
    {
        var query = EntityQueryEnumerator<EclipsePhototaxisComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var moth, out var xform))
        {
            if (Paused(uid) || Timing.CurTime < moth.NextThink)
                continue;
            moth.NextThink = Timing.CurTime + TimeSpan.FromSeconds(4);
            if (Timing.CurTime >= moth.Expires)
            {
                // Live migrants eventually leave. Corpses remain for salvage and research.
                if (!TryComp(uid, out MobStateComponent? state) || !_mobState.IsDead(uid, state))
                    QueueDel(uid);
                else
                    RemCompDeferred<EclipsePhototaxisComponent>(uid);
                continue;
            }
            if (TryComp(uid, out MobStateComponent? mob) && _mobState.IsDead(uid, mob))
                continue;

            EntityUid? target = null;
            var best = 0f;
            var position = _transform.GetMapCoordinates(uid, xform);
            foreach (var light in _lookup.GetEntitiesInRange<PointLightComponent>(position, 45))
            {
                if (!light.Comp.Enabled || light.Owner == uid || HasComp<EclipsePhototaxisComponent>(light))
                    continue;
                var distance = Vector2.DistanceSquared(position.Position, _transform.GetWorldPosition(light.Owner));
                var attraction = light.Comp.Energy * light.Comp.Radius / MathF.Max(1, distance);
                if (attraction <= best)
                    continue;
                best = attraction;
                target = light.Owner;
            }

            if (target is { } lightTarget)
            {
                var targetCoords = Transform(lightTarget).Coordinates;
                _steering.Register(uid, targetCoords);
                if (Vector2.DistanceSquared(position.Position, _transform.ToMapCoordinates(targetCoords).Position) <= 2.25f)
                {
                    // They gnaw on light sources, so turning lights off or placing a decoy changes the attack.
                    _damage.TryChangeDamage(lightTarget, new DamageSpecifier { DamageDict = { ["Blunt"] = 8 } });
                }
                else
                {
                    // A light behind a window draws them onto the hull; repeated gnawing can open a breach.
                    foreach (var obstacle in _lookup.GetEntitiesInRange<DamageableComponent>(position, 1.2f, flags: LookupFlags.Static))
                    {
                        if (!Transform(obstacle).Anchored)
                            continue;
                        _damage.TryChangeDamage(obstacle.AsNullable(), new DamageSpecifier { DamageDict = { ["Blunt"] = 6 } });
                        break;
                    }
                }
            }
            else
            {
                // In darkness the swarm passes by instead of selecting living crew as targets.
                var destination = moth.MigrationDestination.IsValid(EntityManager)
                    ? moth.MigrationDestination
                    : xform.Coordinates.Offset(new Vector2(12, 0));
                if (Vector2.DistanceSquared(_transform.ToMapCoordinates(destination).Position, position.Position) <= 25)
                    moth.MigrationDestination = destination.Offset(new Vector2(60, 0));
                _steering.Register(uid, destination);
            }
        }
    }
}
