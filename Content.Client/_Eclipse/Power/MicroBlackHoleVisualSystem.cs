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
    public override void Initialize() => _overlays.AddOverlay(new MicroBlackHoleOverlay());
    public override void Shutdown() => _overlays.RemoveOverlay<MicroBlackHoleOverlay>();
}

/// <summary>At most four bounded quads, one shared shader, no render targets or texture creation per entity.</summary>
public sealed class MicroBlackHoleOverlay : Overlay
{
    private const int MaximumVisible = 4;
    private readonly IEntityManager _entities = IoCManager.Resolve<IEntityManager>();
    private readonly IConfigurationManager _config = IoCManager.Resolve<IConfigurationManager>();
    private readonly IGameTiming _timing = IoCManager.Resolve<IGameTiming>();
    private readonly ShaderInstance _shader = IoCManager.Resolve<IPrototypeManager>()
        .Index<ShaderPrototype>("EclipseMicroBlackHole").InstanceUnique();
    private readonly List<(Vector2 Position, float Radius, float Activity, float Instability)> _visible = new(MaximumVisible);
    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public MicroBlackHoleOverlay() { ZIndex = 102; }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        _visible.Clear();
        if (args.Viewport.Eye == null) return false;
        var transforms = _entities.System<SharedTransformSystem>();
        var query = _entities.EntityQueryEnumerator<MicroBlackHoleVisualComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var visual, out var xform))
        {
            if (xform.MapID != args.MapId || visual.Activity <= .001f) continue;
            var activity = Math.Clamp(visual.Activity, 0, 1);
            var radius = Math.Clamp(visual.Radius, .5f, 4) * MathF.Sqrt(activity);
            var position = transforms.GetWorldPosition(uid);
            if ((position - args.WorldAABB.ClosestPoint(position)).LengthSquared() > radius * radius) continue;
            _visible.Add((position, radius, activity, Math.Clamp(visual.Instability, 0, 1)));
            if (_visible.Count == MaximumVisible) break;
        }
        return _visible.Count > 0;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null) return;
        var reducedMotion = _config.GetCVar(CCVars.ReducedMotion);
        _shader.SetParameter("pixel_scale", args.Viewport.RenderScale * args.Viewport.Eye.Scale);
        _shader.SetParameter("elapsed", reducedMotion ? 0 : (float) (_timing.RealTime.TotalSeconds % 3600));
        _shader.SetParameter("motion", reducedMotion ? 0f : 1f);
        foreach (var (position, radius, activity, instability) in _visible)
        {
            var screen = args.Viewport.WorldToLocal(position);
            screen.Y = args.Viewport.Size.Y - screen.Y;
            _shader.SetParameter("center", screen);
            _shader.SetParameter("radius", radius * EyeManager.PixelsPerMeter);
            _shader.SetParameter("activity", activity);
            _shader.SetParameter("instability", instability);
            args.WorldHandle.UseShader(_shader);
            args.WorldHandle.DrawRect(Box2.CenteredAround(position, new Vector2(radius * 2)), Color.White);
        }
        args.WorldHandle.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        _shader.Dispose();
        base.DisposeBehavior();
    }
}
