using System.Numerics;
using Content.Shared.CCVar;
using Content.Shared.Movement.Components;
using Content.Shared.Stealth.Components;
using Content.Shared.Throwing;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client.Movement.Systems;

public sealed partial class JumpFlightVisualsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IEyeManager _eyes = default!;
    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<EntityUid, (SpriteAppearance Base, SpriteAppearance Applied)> _appearances = new();
    private JumpShadowOverlay _shadow = default!;

    private readonly record struct SpriteAppearance(Vector2 Offset, Color Color);

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(AnimationPlayerSystem));
        SubscribeLocalEvent<JumpFlightComponent, ComponentShutdown>(OnLanding);
        _shadow = new JumpShadowOverlay(this);
        _overlays.AddOverlay(_shadow);
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay(_shadow);
        foreach (var (uid, appearance) in _appearances)
            RestoreAppearance(uid, appearance);
        _appearances.Clear();
    }

    public override void FrameUpdate(float frameTime)
    {
        var query = EntityQueryEnumerator<JumpFlightComponent, ThrownItemComponent, SpriteComponent, TransformComponent>();
        var height = _config.GetCVar(CCVars.ReducedMotion) ? 0.18f : 0.52f;
        while (query.MoveNext(out var uid, out _, out var thrown, out var sprite, out var xform))
        {
            var basis = sprite.Offset;
            var color = sprite.Color;
            if (_appearances.TryGetValue(uid, out var previous))
            {
                if (basis == previous.Applied.Offset)
                    basis = previous.Base.Offset;
                if (color == previous.Applied.Color)
                    color = previous.Base.Color;
            }

            var viewAngle = _transform.GetWorldRotation(xform) + _eyes.CurrentEye.Rotation;
            if (sprite.SnapCardinals)
                viewAngle -= viewAngle.RoundToCardinalAngle();
            var arc = GetArc(thrown);
            var lift = new Vector2(0, height * arc);
            var offset = basis + (sprite.NoRotation ? lift : (-viewAngle).RotateVec(lift));
            var tint = color * new Color(1f - 0.14f * arc, 1f - 0.05f * arc, 1f, 1f);
            _sprites.SetOffset((uid, sprite), offset);
            _sprites.SetColor((uid, sprite), tint);
            _appearances[uid] = (new SpriteAppearance(basis, color), new SpriteAppearance(offset, tint));
        }
    }

    private void OnLanding(Entity<JumpFlightComponent> ent, ref ComponentShutdown args)
    {
        if (_appearances.Remove(ent.Owner, out var appearance))
            RestoreAppearance(ent.Owner, appearance);
    }

    private void RestoreAppearance(EntityUid uid, (SpriteAppearance Base, SpriteAppearance Applied) appearance)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        if (sprite.Offset == appearance.Applied.Offset)
            _sprites.SetOffset((uid, sprite), appearance.Base.Offset);
        if (sprite.Color == appearance.Applied.Color)
            _sprites.SetColor((uid, sprite), appearance.Base.Color);
    }

    private float GetArc(ThrownItemComponent thrown)
    {
        if (thrown.ThrownTime is not { } start || thrown.LandTime is not { } end || end <= start)
            return 0;

        var progress = (float) Math.Clamp((_timing.CurTime - start) / (end - start), 0, 1);
        return 4 * progress * (1 - progress);
    }

    private sealed class JumpShadowOverlay(JumpFlightVisualsSystem system) : Overlay
    {
        public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

        protected override void Draw(in OverlayDrawArgs args)
        {
            var handle = args.WorldHandle;
            var query = system.EntityManager.EntityQueryEnumerator<JumpFlightComponent, ThrownItemComponent, SpriteComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var thrown, out var sprite, out var xform))
            {
                if (xform.MapID != args.MapId || !sprite.AddToTree || sprite.Color.A <= 0f
                    || system.TryComp<StealthComponent>(uid, out var stealth) && stealth.Enabled)
                    continue;

                var position = system._transform.GetWorldPosition(xform);
                if (!args.WorldAABB.Contains(position))
                    continue;

                var arc = system.GetArc(thrown);
                var rotation = -(args.Viewport.Eye?.Rotation ?? Angle.Zero);
                handle.SetTransform(Matrix3x2.CreateScale(1f, 0.5f) * Matrix3x2.CreateRotation((float) rotation) * Matrix3x2.CreateTranslation(position));
                handle.DrawCircle(Vector2.Zero, 0.29f - 0.04f * arc, new Color(0f, 0f, 0f, 0.28f * arc));
            }
            handle.SetTransform(Matrix3x2.Identity);
        }
    }
}
