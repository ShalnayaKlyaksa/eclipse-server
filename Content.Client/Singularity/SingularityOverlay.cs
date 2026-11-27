using System.Numerics;
using Content.Client._Eclipse.Power;
using Content.Shared.CCVar;
using Content.Shared.Singularity.Components;
using Content.Shared._Eclipse.Power;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client.Singularity
{
    public sealed class SingularityOverlay : Overlay, IEntityEventSubscriber
    {
        private static readonly ProtoId<ShaderPrototype> Shader = "Singularity";

        [Dependency] private readonly IEntityManager _entMan = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IConfigurationManager _configManager = default!;
        private SharedTransformSystem? _xformSystem = null;

        /// <summary>
        ///     Maximum number of distortions that can be shown on screen at a time.
        ///     If this value is changed, the shader itself also needs to be updated.
        /// </summary>
        public const int MaxCount = 5;

        private const float MaxDistance = 20f;

        public override OverlaySpace Space => OverlaySpace.WorldSpace;
        public override bool RequestScreenTexture => true;

        private readonly ShaderInstance _shader;

        private bool _reducedMotion;

        public SingularityOverlay()
        {
            IoCManager.InjectDependencies(this);
            _shader = _prototypeManager.Index(Shader).Instance().Duplicate();
            _shader.SetParameter("maxDistance", MaxDistance * EyeManager.PixelsPerMeter);
            _entMan.EventBus.SubscribeEvent<PixelToMapEvent>(EventSource.Local, this, OnProjectFromScreenToMap);
            ZIndex = 101; // Should be drawn after the placement overlay so admins placing items near the singularity can tell where they're going.

            _configManager.OnValueChanged(CCVars.ReducedMotion, (b) => { _reducedMotion = b; }, invokeImmediately: true);
        }

        private readonly Vector2[] _positions = new Vector2[MaxCount];
        private readonly float[] _intensities = new float[MaxCount];
        private readonly float[] _falloffPowers = new float[MaxCount];
        private int _count = 0;

        protected override bool BeforeDraw(in OverlayDrawArgs args)
        {
            if (_reducedMotion)
                return false;
            if (args.Viewport.Eye == null)
                return false;
            if (_xformSystem is null && !_entMan.TrySystem(out _xformSystem))
                return false;

            _count = 0;
            var query = _entMan.EntityQueryEnumerator<SingularityDistortionComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var distortion, out var xform))
            {
                AddDistortion(args, uid, xform, distortion.Intensity, distortion.FalloffPower);
                if (_count == MaxCount)
                    break;
            }

            // Contained micro holes share the ordinary singularity lens and its cursor projection.
            var visuals = _entMan.System<MicroBlackHoleVisualSystem>();
            var microQuery = _entMan.EntityQueryEnumerator<MicroBlackHoleVisualComponent, TransformComponent>();
            while (_count < MaxCount && microQuery.MoveNext(out var uid, out var visual, out var xform))
            {
                var (activity, instability, radius) = visuals.GetDisplay(uid);
                if (activity <= .001f) continue;
                radius = MicroBlackHoleVisualSystem.DisplayRadius(radius, activity);
                if (visual.FieldRadius > 0)
                    radius = MathF.Min(radius, visual.FieldRadius);
                var strength = (18 + 24 * activity + 40 * instability) * MathF.Pow(radius / 2.5f, MathF.Sqrt(2));
                AddDistortion(args, uid, xform, strength, MathF.Sqrt(2));
            }

            return (_count > 0);
        }

        private void AddDistortion(in OverlayDrawArgs args, EntityUid uid, TransformComponent xform, float intensity, float falloff)
        {
            if (xform.MapID != args.MapId) return;
            var mapPos = _xformSystem!.GetWorldPosition(uid);
            if ((mapPos - args.WorldAABB.ClosestPoint(mapPos)).LengthSquared() > MaxDistance * MaxDistance) return;
            var coords = args.Viewport.WorldToLocal(mapPos);
            coords.Y = args.Viewport.Size.Y - coords.Y;
            _positions[_count] = coords;
            _intensities[_count] = intensity;
            _falloffPowers[_count++] = falloff;
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            if (ScreenTexture == null || args.Viewport.Eye == null)
                return;

            _shader?.SetParameter("renderScale", args.Viewport.RenderScale * args.Viewport.Eye.Scale);
            _shader?.SetParameter("count", _count);
            _shader?.SetParameter("position", _positions);
            _shader?.SetParameter("intensity", _intensities);
            _shader?.SetParameter("falloffPower", _falloffPowers);
            _shader?.SetParameter("SCREEN_TEXTURE", ScreenTexture);

            var worldHandle = args.WorldHandle;
            worldHandle.UseShader(_shader);
            worldHandle.DrawRect(args.WorldAABB, Color.White);
            worldHandle.UseShader(null);
        }

        /// <summary>
        /// Repeats the transformation applied by the shader in <see cref="Resources/Textures/Shaders/singularity.swsl"/>
        /// </summary>
        private void OnProjectFromScreenToMap(ref PixelToMapEvent args)
        {   // Mostly copypasta from the singularity shader.
            if (_reducedMotion)
                return;
            if (args.Viewport.Eye == null)
                return;
            var maxDistance = MaxDistance * EyeManager.PixelsPerMeter;
            var finalCoords = args.VisiblePosition;

            for (var i = 0; i < MaxCount && i < _count; i++)
            {
                // An explanation of pain:
                // The shader used by the singularity to create the neat distortion effect occurs in _fragment space_
                // All of these calculations are done in _local space_.
                // The only difference between the two is that in fragment space 'Y' is measured in pixels from the bottom of the viewport...
                // and in local space 'Y' is measured in pixels from the top of the viewport.
                // As a minor optimization the locations of the singularities are transformed into fragment space in BeforeDraw so the shader doesn't need to.
                // We need to undo that here or this will transform the cursor position as if the singularities were mirrored vertically relative to the center of the viewport.

                var localPosition = _positions[i];
                localPosition.Y = args.Viewport.Size.Y - localPosition.Y;
                var delta = args.VisiblePosition - localPosition;
                var distance = (delta / (args.Viewport.RenderScale * args.Viewport.Eye.Scale)).Length();

                var deformation = _intensities[i] / MathF.Pow(distance, _falloffPowers[i]);

                // ensure deformation goes to zero at max distance
                // avoids long-range single-pixel shifts that are noticeable when leaving PVS.

                if (distance >= maxDistance)
                    deformation = 0.0f;
                else
                    deformation *= 1.0f - MathF.Pow(distance / maxDistance, 4.0f);

                if (deformation > 0.8)
                    deformation = MathF.Pow(deformation, 0.3f);

                finalCoords -= delta * deformation;
            }

            finalCoords.X -= MathF.Floor(finalCoords.X / (args.Viewport.Size.X * 2)) * args.Viewport.Size.X * 2; // Manually handle the wrapping reflection behaviour used by the viewport texture.
            finalCoords.Y -= MathF.Floor(finalCoords.Y / (args.Viewport.Size.Y * 2)) * args.Viewport.Size.Y * 2;
            finalCoords.X = (finalCoords.X >= args.Viewport.Size.X) ? ((args.Viewport.Size.X * 2) - finalCoords.X) : finalCoords.X;
            finalCoords.Y = (finalCoords.Y >= args.Viewport.Size.Y) ? ((args.Viewport.Size.Y * 2) - finalCoords.Y) : finalCoords.Y;
            args.VisiblePosition = finalCoords;
        }
    }
}
