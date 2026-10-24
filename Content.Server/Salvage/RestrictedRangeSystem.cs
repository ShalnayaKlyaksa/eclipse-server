using System.Numerics;
using Content.Shared.Movement.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Salvage;
using Robust.Shared.Map;

namespace Content.Server.Salvage;

public sealed class RestrictedRangeSystem : SharedRestrictedRangeSystem
{
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RestrictedRangeComponent, MapInitEvent>(OnRestrictedMapInit);
    }

    private void OnRestrictedMapInit(EntityUid uid, RestrictedRangeComponent component, MapInitEvent args)
    {
        component.BoundaryEntity = CreateBoundary(new EntityCoordinates(uid, component.Origin), component.Range);
    }

    public EntityUid CreateBoundary(EntityCoordinates coordinates, float range)
    {
        var boundaryUid = Spawn(null, coordinates);
        AddComp<BoundaryComponent>(boundaryUid);
        return boundaryUid;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var rangeQuery = EntityQueryEnumerator<RestrictedRangeComponent>();

        while (rangeQuery.MoveNext(out var rangeUid, out var range))
        {
            if (range.BoundaryEntity == EntityUid.Invalid ||
                Deleted(range.BoundaryEntity) ||
                !TryComp<BoundaryComponent>(range.BoundaryEntity, out var boundary))
            {
                continue;
            }

            var center = _xform.GetWorldPosition(range.BoundaryEntity);
            var targetRadius = MathF.Max(0f, range.Range - boundary.Offset);
            var mobQuery = EntityQueryEnumerator<MobStateComponent, TransformComponent>();

            while (mobQuery.MoveNext(out var mobUid, out _, out var mobXform))
            {
                if (mobUid == rangeUid || mobXform.MapUid != rangeUid)
                    continue;

                var offset = _xform.GetWorldPosition(mobXform) - center;
                var distance = offset.Length();

                if (distance <= range.Range || distance <= 0f)
                    continue;

                _xform.SetWorldPosition((mobUid, mobXform), center + offset.Normalized() * targetRadius);
            }
        }
    }
}
