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

    private readonly Dictionary<EntityUid, (Vector2 Base, Vector2 Applied)> _spriteOffsets = new();
    private JumpHighlightOverlay _highlight = default!;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(AnimationPlayerSystem));
        SubscribeLocalEvent<JumpFlightComponent, ComponentShutdown>(OnLanding);
        _highlight = new JumpHighlightOverlay(this);
        _overlays.AddOverlay(_highlight);
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay(_highlight);
        foreach (var (uid, offsets) in _spriteOffsets)
        {
            if (TryComp<SpriteComponent>(uid, out var sprite) && sprite.Offset == offsets.Applied)
                _sprites.SetOffset((uid, sprite), offsets.Base);
        }
        _spriteOffsets.Clear();
    }

    public override void FrameUpdate(float frameTime)
    {
        var query = EntityQueryEnumerator<JumpFlightComponent, ThrownItemComponent, SpriteComponent, TransformComponent>();
        var height = _config.GetCVar(CCVars.ReducedMotion) ? 0.18f : 0.52f;
        while (query.MoveNext(out var uid, out _, out var thrown, out var sprite, out var xform))
        {
            var basis = sprite.Offset;
            if (_spriteOffsets.TryGetValue(uid, out var previous) && basis == previous.Applied)
                basis = previous.Base;

            var viewAngle = _transform.GetWorldRotation(xform) + _eyes.CurrentEye.Rotation;
            if (sprite.SnapCardinals)
                viewAngle -= viewAngle.RoundToCardinalAngle();
            var lift = new Vector2(0, height * GetArc(thrown));
            var offset = basis + (sprite.NoRotation ? lift : (-viewAngle).RotateVec(lift));
            _sprites.SetOffset((uid, sprite), offset);
            _spriteOffsets[uid] = (basis, offset);
        }
    }

    private void OnLanding(Entity<JumpFlightComponent> ent, ref ComponentShutdown args)
    {
        if (_spriteOffsets.Remove(ent.Owner, out var offsets)
            && TryComp<SpriteComponent>(ent, out var sprite) && sprite.Offset == offsets.Applied)
            _sprites.SetOffset((ent.Owner, sprite), offsets.Base);
    }

    private float GetArc(ThrownItemComponent thrown)
    {
        if (thrown.ThrownTime is not { } start || thrown.LandTime is not { } end || end <= start)
            return 0;

        var progress = (float) Math.Clamp((_timing.CurTime - start) / (end - start), 0, 1);
        return 4 * progress * (1 - progress);
    }

    private sealed class JumpHighlightOverlay(JumpFlightVisualsSystem system) : Overlay
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
                handle.SetTransform(Matrix3x2.CreateScale(1f, 0.58f) * Matrix3x2.CreateRotation((float) rotation) * Matrix3x2.CreateTranslation(position));
                handle.DrawCircle(Vector2.Zero, 0.32f, new Color(0.04f, 0.1f, 0.15f, 0.35f * arc));
                handle.DrawCircle(Vector2.Zero, 0.34f + 0.06f * arc, new Color(0.42f, 0.87f, 1f, 0.9f * arc), filled: false);
            }
            handle.SetTransform(Matrix3x2.Identity);
        }
    }
}
