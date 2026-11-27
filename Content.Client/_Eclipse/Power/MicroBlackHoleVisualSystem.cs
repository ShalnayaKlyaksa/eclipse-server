using System.Numerics;
using Content.Shared._Eclipse.Power;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Eclipse.Power;

public sealed class MicroBlackHoleVisualSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;
    private readonly Dictionary<EntityUid, (float Activity, float Instability, float Radius)> _display = new();

    public override void Initialize()
    {
        _overlays.AddOverlay(new MicroBlackHoleOverlay(this));
        SubscribeLocalEvent<MicroBlackHoleVisualComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Shutdown() => _overlays.RemoveOverlay<MicroBlackHoleOverlay>();

    public override void FrameUpdate(float frameTime)
    {
        // Blend network updates once per frame so the disk and the lens grow together.
        var blend = 1 - MathF.Exp(-3.5f * frameTime);
        var query = EntityQueryEnumerator<MicroBlackHoleVisualComponent>();
        while (query.MoveNext(out var uid, out var visual))
        {
            var (activity, instability, radius) = GetDisplay(uid);
            var targetActivity = Math.Clamp(visual.Activity, 0, 1);
            activity += (targetActivity - activity) * blend;
            instability += (Math.Clamp(visual.Instability, 0, 1) - instability) * blend;
            radius += (Math.Clamp(visual.Radius, .5f, 25) - radius) * blend;
            if (targetActivity == 0 && activity <= .001f)
                _display.Remove(uid);
            else
                _display[uid] = (activity, instability, radius);
        }
    }

    internal (float Activity, float Instability, float Radius) GetDisplay(EntityUid uid) => _display.GetValueOrDefault(uid);

    internal static float DisplayRadius(float radius, float activity)
    {
        var visible = Math.Clamp(activity / .55f, 0, 1);
        return radius * visible * (1.65f - .65f * visible);
    }

    private void OnShutdown(Entity<MicroBlackHoleVisualComponent> ent, ref ComponentShutdown args) => _display.Remove(ent);
}

/// <summary>At most four bounded quads, one shared shader, no render targets or texture creation per entity.</summary>
public sealed class MicroBlackHoleOverlay : Overlay
{
    private const int MaximumVisible = 4;
    private static readonly ProtoId<ShaderPrototype> BlackHoleShader = "EclipseMicroBlackHole";
    private readonly IEntityManager _entities = IoCManager.Resolve<IEntityManager>();
    private readonly IConfigurationManager _config = IoCManager.Resolve<IConfigurationManager>();
    private readonly IGameTiming _timing = IoCManager.Resolve<IGameTiming>();
    private readonly MicroBlackHoleVisualSystem _visuals;
    private readonly ShaderInstance _shader = IoCManager.Resolve<IPrototypeManager>()
        .Index(BlackHoleShader).InstanceUnique();
    private readonly List<(Vector2 Position, float Radius, float Field, float Activity, float Instability)> _visible = new(MaximumVisible);
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public MicroBlackHoleOverlay(MicroBlackHoleVisualSystem visuals)
    {
        _visuals = visuals;
        ZIndex = 102;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _visible.Clear();
        if (args.Viewport.Eye == null) return false;
        var transforms = _entities.System<SharedTransformSystem>();
        var query = _entities.EntityQueryEnumerator<MicroBlackHoleVisualComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var visual, out var xform))
        {
            var (activity, instability, smoothedRadius) = _visuals.GetDisplay(uid);
            if (xform.MapID != args.MapId || activity <= .001f) continue;
            var radius = MicroBlackHoleVisualSystem.DisplayRadius(smoothedRadius, activity);
            var position = transforms.GetWorldPosition(uid);
            var bounds = DrawRadius(radius, visual.FieldRadius);
            if ((position - args.WorldAABB.ClosestPoint(position)).LengthSquared() > bounds * bounds) continue;
            _visible.Add((position, radius, visual.FieldRadius, activity, instability));
            if (_visible.Count == MaximumVisible) break;
        }
        return _visible.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null) return;
        var reducedMotion = _config.GetCVar(CCVars.ReducedMotion);
        _shader.SetParameter("pixel_scale", args.Viewport.RenderScale * args.Viewport.Eye.Scale);
        _shader.SetParameter("elapsed", reducedMotion ? 0 : (float) _timing.RealTime.TotalSeconds);
        foreach (var (position, radius, field, activity, instability) in _visible)
        {
            var screen = args.Viewport.WorldToLocal(position);
            screen.Y = args.Viewport.Size.Y - screen.Y;
            _shader.SetParameter("center", screen);
            _shader.SetParameter("radius", radius * EyeManager.PixelsPerMeter);
            _shader.SetParameter("field_limit", field * EyeManager.PixelsPerMeter);
            _shader.SetParameter("activity", activity);
            _shader.SetParameter("instability", instability);
            args.WorldHandle.UseShader(_shader);
            args.WorldHandle.DrawRect(Box2.CenteredAround(position, new Vector2(DrawRadius(radius, field) * 2)), Color.White);
        }
        args.WorldHandle.UseShader(null);
    }

    private static float DrawRadius(float radius, float field) => field > 0 ? field * 1.1f : radius;

    protected override void DisposeBehavior()
    {
        _shader.Dispose();
        base.DisposeBehavior();
    }
}
