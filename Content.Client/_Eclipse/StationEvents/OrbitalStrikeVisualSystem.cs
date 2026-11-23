using System.Numerics;
using Content.Shared._Eclipse.StationEvents;
using Content.Shared.Camera;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Eclipse.StationEvents;

public sealed partial class OrbitalStrikeVisualSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedCameraRecoilSystem _recoil = default!;
    private OrbitalStrikeOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new OrbitalStrikeOverlay();
        _overlays.AddOverlay(_overlay);
        SubscribeNetworkEvent<OrbitalStrikeEffectEvent>(OnStrike);
    }

    private void OnStrike(OrbitalStrikeEffectEvent args)
    {
        _overlay.Add(args);
        if (_config.GetCVar(CCVars.ReducedMotion) || !TryComp<TransformComponent>(_players.LocalEntity, out var xform)
            || xform.MapID != args.Origin.MapId)
            return;
        var distance = Vector2.Distance(_transform.GetWorldPosition(xform), args.Origin.Position);
        var passage = args.Effect == OrbitalStrikeEffect.GungnirPassage;
        var strength = passage ? 0.24f : 0.32f * Math.Clamp(1 - distance / 30, 0, 1);
        _recoil.KickCamera(_players.LocalEntity!.Value, new Vector2(0.6f, 0.8f) * strength);
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay(_overlay);
        base.Shutdown();
    }
}

/// <summary>Transient local geometry and one procedural dust shader; no networked particle entities.</summary>
public sealed class OrbitalStrikeOverlay : Overlay
{
    private const int MaximumEffects = 96;
    private readonly List<(OrbitalStrikeEffectEvent Event, TimeSpan Started)> _effects = new();
    private readonly IGameTiming _timing = IoCManager.Resolve<IGameTiming>();
    private readonly IConfigurationManager _config = IoCManager.Resolve<IConfigurationManager>();
    private readonly IEntityManager _entities = IoCManager.Resolve<IEntityManager>();
    private readonly ShaderInstance _unshaded = IoCManager.Resolve<IPrototypeManager>().Index<ShaderPrototype>("unshaded").Instance();
    private readonly ShaderInstance _cloud = IoCManager.Resolve<IPrototypeManager>().Index<ShaderPrototype>("EclipseStrikeCloud").InstanceUnique();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public void Add(OrbitalStrikeEffectEvent effect)
    {
        if (_effects.Count >= MaximumEffects)
            _effects.RemoveAt(0);
        _effects.Add((effect, _timing.CurTime));
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        _effects.RemoveAll(effect => (_timing.CurTime - effect.Started).TotalSeconds > effect.Event.Duration + 0.3f);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var reduced = _config.GetCVar(CCVars.ReducedMotion);
        handle.UseShader(_unshaded);
        var projectiles = _entities.EntityQueryEnumerator<SiegeProjectileVisualComponent, TransformComponent>();
        while (projectiles.MoveNext(out var uid, out var projectile, out var xform))
        {
            if (!projectile.Exhaust || xform.MapID != args.MapId)
                continue;
            var position = _entities.System<SharedTransformSystem>().GetWorldPosition(uid);
            if ((position - args.WorldAABB.ClosestPoint(position)).LengthSquared() > 9)
                continue;
            var pulse = reduced ? 1 : 0.85f + 0.15f * MathF.Sin((float) _timing.CurTime.TotalSeconds * 23);
            var tail = position - projectile.Direction * (2.3f * pulse);
            Stroke(handle, position, tail, 0.65f, new Color(1f, 0.25f, 0.03f, 0.13f));
            Stroke(handle, position, tail, 0.25f, new Color(1f, 0.65f, 0.15f, 0.55f));
            Stroke(handle, position, position - projectile.Direction * pulse, 0.1f, new Color(1f, 0.95f, 0.8f, 0.95f));
        }
        foreach (var (effect, started) in _effects)
        {
            if (effect.Origin.MapId != args.MapId)
                continue;
            var age = (float) (_timing.CurTime - started).TotalSeconds;
            var position = effect.Origin.Position;
            handle.UseShader(_unshaded);
            if (effect.Effect == OrbitalStrikeEffect.GungnirPassage)
            {
                var progress = Math.Clamp(age / Math.Max(0.01f, effect.Duration), 0, 1);
                var head = position + effect.Direction * (-effect.HalfLength + 2 * effect.HalfLength * progress);
                var fade = 1 - Math.Clamp((age - effect.Duration) / 0.3f, 0, 1);
                var tail = head - effect.Direction * (reduced ? 7 : 17);
                for (var layer = 5; layer >= 1; layer--)
                    Stroke(handle, tail, head, layer * 0.16f, new Color(0.26f, 0.7f, 1f, fade * 0.065f));
                Stroke(handle, tail, head, 0.08f, new Color(0.8f, 0.94f, 1f, fade * 0.85f));
                Stroke(handle, head - effect.Direction * 2.4f, head, 0.22f, new Color(0.4f, 0.55f, 0.63f, fade));
                Stroke(handle, head - effect.Direction * 2.2f, head + effect.Direction * 0.15f, 0.075f, Color.White.WithAlpha(fade));
                continue;
            }

            var kinetic = effect.Effect == OrbitalStrikeEffect.GungnirBreach;
            var size = effect.Effect == OrbitalStrikeEffect.DamoclesImpact ? 8f : kinetic ? 4f : 6f;
            if ((position - args.WorldAABB.ClosestPoint(position)).LengthSquared() > size * size)
                continue;
            // White-hot splinters cool into orange fragments; a fixed seed keeps the shape stable between frames.
            var seed = position.X * 0.71f + position.Y * 0.31f;
            var sparkFade = MathF.Pow(Math.Clamp(1 - age / 1.4f, 0, 1), 2);
            for (var i = 0; i < 28; i++)
            {
                var angle = i * 2.399963f + seed;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var speed = 1.3f + (i % 7) * 0.65f;
                var travel = reduced ? 0.25f : age;
                var end = position + direction * travel * speed + new Vector2(0, -travel * travel * 0.4f);
                var heat = Math.Clamp(age, 0, 1);
                var color = new Color(1f, 0.93f - heat * 0.65f, 0.74f - heat * 0.7f);
                Stroke(handle, end - direction * 0.35f, end, 0.035f, color.WithAlpha(sparkFade));
            }

            if (args.Viewport.Eye == null)
                continue;
            var screen = args.Viewport.WorldToLocal(position);
            screen.Y = args.Viewport.Size.Y - screen.Y;
            _cloud.SetParameter("center", screen);
            _cloud.SetParameter("pixel_scale", args.Viewport.RenderScale * args.Viewport.Eye.Scale);
            _cloud.SetParameter("radius", size * EyeManager.PixelsPerMeter);
            _cloud.SetParameter("age", age);
            _cloud.SetParameter("seed", seed);
            _cloud.SetParameter("kinetic", kinetic ? 1f : 0f);
            _cloud.SetParameter("motion", reduced ? 0f : 1f);
            handle.UseShader(_cloud);
            handle.DrawRect(Box2.CenteredAround(position, new Vector2(size * 2)), Color.White);
        }
        handle.UseShader(null);
    }

    private static void Stroke(DrawingHandleWorld handle, Vector2 from, Vector2 to, float width, Color color)
    {
        var direction = to - from;
        if (direction.LengthSquared() < 0.0001f || color.A <= 0)
            return;
        var side = Vector2.Normalize(new Vector2(-direction.Y, direction.X)) * width * 0.5f;
        Span<Vector2> vertices = new Vector2[] { from - side, from + side, to + side, to - side };
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, vertices, color);
    }

    protected override void DisposeBehavior()
    {
        _cloud.Dispose();
        base.DisposeBehavior();
    }
}
