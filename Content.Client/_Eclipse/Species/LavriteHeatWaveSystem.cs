using System.Numerics;
using Content.Shared._Eclipse.Species;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Eclipse.Species;

public sealed class LavriteHeatWaveSystem : EntitySystem
{
    private const float Duration = 0.52f;

    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly List<(LavriteHeatWaveEvent Wave, TimeSpan Start)> _waves = new();

    public override void Initialize()
    {
        _overlays.AddOverlay(new HeatWaveOverlay(this));
        SubscribeNetworkEvent<LavriteHeatWaveEvent>(OnWave);
    }

    public override void Shutdown() => _overlays.RemoveOverlay<HeatWaveOverlay>();

    private void OnWave(LavriteHeatWaveEvent wave)
    {
        if (wave.Rays.Length == LavriteHeatWaveEvent.RayCount)
            _waves.Add((wave, _timing.RealTime));
    }

    public override void FrameUpdate(float frameTime)
    {
        if (_waves.Count == 0)
            return;

        var expiry = _timing.RealTime - TimeSpan.FromSeconds(Duration);
        _waves.RemoveAll(wave => wave.Start <= expiry);
    }

    private sealed class HeatWaveOverlay(LavriteHeatWaveSystem system) : Overlay
    {
        private readonly Vector2[] _bandVertices = new Vector2[LavriteHeatWaveEvent.RayCount * 2];

        public override OverlaySpace Space => OverlaySpace.WorldSpace;

        protected override void Draw(in OverlayDrawArgs args)
        {
            if (system._waves.Count == 0)
                return;

            var now = system._timing.RealTime;
            var reduced = system._config.GetCVar(CCVars.ReducedMotion);
            foreach (var (wave, start) in system._waves)
            {
                var origin = wave.Origin.Position;
                var reach = origin - args.WorldAABB.ClosestPoint(origin);
                if (wave.Origin.MapId != args.MapId || reach.LengthSquared() > wave.Range * wave.Range)
                    continue;

                var progress = Math.Clamp((float) (now - start).TotalSeconds / Duration, 0f, 1f);
                var fade = 1f - progress;
                var radius = wave.Range * (reduced ? 1f : MathF.Min(1f, 0.08f + 1.35f * progress));
                var width = Math.Min(0.52f, wave.Range * 0.28f);
                var handle = args.WorldHandle;

                DrawBand(handle, wave, radius - width * 1.6f, width * 0.33f, new Color(1f, 0.25f, 0.08f, fade * 0.32f));
                DrawBand(handle, wave, radius, width, new Color(1f, 0.38f, 0.13f, fade * 0.38f));
                DrawBand(handle, wave, radius, width * 0.25f, new Color(1f, 0.82f, 0.45f, fade * 0.8f));

                if (reduced)
                    continue;

                for (var i = 1; i < wave.Rays.Length; i += 2)
                {
                    var distance = Math.Max(0f, radius - width * (1f + i % 3 * 0.5f));
                    var spark = PointAlongRay(wave.Rays[i], distance);
                    if (spark.LengthSquared() < 0.01f)
                        continue;

                    var point = origin + spark;
                    handle.DrawLine(point - spark * 0.15f, point,
                        new Color(1f, 0.73f, 0.29f, fade * 0.85f));
                }
            }
        }

        private void DrawBand(DrawingHandleWorld handle, LavriteHeatWaveEvent wave, float radius, float width, Color color)
        {
            if (radius <= 0f)
                return;

            for (var i = 0; i < wave.Rays.Length; i++)
            {
                var ray = wave.Rays[i];
                _bandVertices[2 * i] = wave.Origin.Position + PointAlongRay(ray, Math.Max(0f, radius - width));
                _bandVertices[2 * i + 1] = wave.Origin.Position + PointAlongRay(ray, radius);
            }

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _bandVertices, color);
        }

        private static Vector2 PointAlongRay(Vector2 ray, float distance)
        {
            var length = ray.Length();
            return length > distance ? ray * (distance / length) : ray;
        }
    }
}
