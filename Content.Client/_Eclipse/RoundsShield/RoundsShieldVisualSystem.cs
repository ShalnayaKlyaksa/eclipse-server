using Content.Shared._Eclipse.RoundsShield.Components;
using Robust.Shared.Prototypes;
namespace Content.Client._Eclipse.RoundsShield;

/// <summary>
/// Client-side shield visuals. Spawned effect entities do not reliably replicate when parented on the server.
/// </summary>
public sealed partial class RoundsShieldVisualSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, (EntityUid Visual, string Direction)> _visuals = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundsShieldComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<RoundsShieldComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<RoundsShieldComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (!component.Raising && !component.Active)
            {
                ClearVisual(uid);
                continue;
            }

            UpdateVisual(uid, component);
        }
    }

    private void OnStartup(EntityUid uid, RoundsShieldComponent component, ComponentStartup args)
    {
        if (component.Raising || component.Active)
            UpdateVisual(uid, component);
    }

    private void OnShutdown(EntityUid uid, RoundsShieldComponent component, ComponentShutdown args)
    {
        ClearVisual(uid);
    }

    private void UpdateVisual(EntityUid owner, RoundsShieldComponent component)
    {
        var visualDirection = GetVisualDirection(component.AimAngle);

        if (!_visuals.TryGetValue(owner, out var tracked) ||
            Deleted(tracked.Visual) ||
            tracked.Direction != visualDirection)
        {
            ClearVisual(owner);

            var visual = Spawn(GetVisualPrototype(component, visualDirection), Transform(owner).Coordinates);
            _visuals[owner] = (visual, visualDirection);

            var visualTransform = Transform(visual);
            _transform.SetParent(visual, visualTransform, owner);
            tracked = (visual, visualDirection);
        }

        var ownerRotation = _transform.GetWorldRotation(owner);
        var localOffset = (component.AimAngle - ownerRotation).ToWorldVec() * component.VisualOffset;

        _transform.SetLocalPosition(tracked.Visual, localOffset);
        _transform.SetLocalRotation(tracked.Visual, Angle.Zero);
    }

    private static string GetVisualDirection(Angle angle)
    {
        return angle.GetCardinalDir().ToString();
    }

    private static EntProtoId GetVisualPrototype(RoundsShieldComponent component, string direction)
    {
        return direction switch
        {
            "East" => component.VisualEastPrototype,
            "South" => component.VisualSouthPrototype,
            "West" => component.VisualWestPrototype,
            _ => component.VisualNorthPrototype,
        };
    }

    private void ClearVisual(EntityUid owner)
    {
        if (!_visuals.TryGetValue(owner, out var tracked))
            return;

        if (Exists(tracked.Visual))
            QueueDel(tracked.Visual);

        _visuals.Remove(owner);
    }
}
