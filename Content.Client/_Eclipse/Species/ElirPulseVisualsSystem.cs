using System.Numerics;
using Content.Shared._Eclipse.Species;
using Content.Shared.CCVar;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Eclipse.Species;

public sealed class ElirPulseVisualsSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;

    private PulseOverlay _overlay = default!;
    private ElirPulseSample[] _samples = Array.Empty<ElirPulseSample>();
    private EntityUid? _listener;
    private TimeSpan _lastSample;
    private TimeSpan _started;

    public override void Initialize()
    {
        var heart = _sprites.Frame0(new SpriteSpecifier.Rsi(new ResPath("Effects/crayondecals.rsi"), "heart"));
        _overlay = new PulseOverlay(this, heart);
        _overlays.AddOverlay(_overlay);
        SubscribeNetworkEvent<ElirPulseSamplesEvent>(OnSamples);
    }

    public override void Shutdown() => _overlays.RemoveOverlay(_overlay);

    private void OnSamples(ElirPulseSamplesEvent args)
    {
        var now = _timing.CurTime;
        var listener = _players.LocalEntity;
        if (_listener != listener || now - _lastSample > TimeSpan.FromSeconds(0.6))
            _started = now;
        _listener = listener;
        _lastSample = now;
        _samples = args.Samples;
    }

    private sealed class PulseOverlay(ElirPulseVisualsSystem system, Texture heart) : Overlay
    {
        public override OverlaySpace Space => OverlaySpace.WorldSpace;

        protected override void Draw(in OverlayDrawArgs args)
        {
            var now = system._timing.CurTime;
            if (system._listener is null || system._listener != system._players.LocalEntity
                || now - system._lastSample > TimeSpan.FromSeconds(0.6))
                return;

            var elapsed = (float) (now - system._started).TotalSeconds;
            var reduced = system._config.GetCVar(CCVars.ReducedMotion);
            foreach (var sample in system._samples)
            {
                if (sample.Position.MapId != args.MapId || !args.WorldAABB.Contains(sample.Position.Position))
                    continue;

                var phase = elapsed * sample.BeatsPerMinute / 60f % 1f;
                var beat = MathF.Max(0f, 1f - 4f * phase);
                var size = 0.56f + (reduced ? 0f : 0.16f * beat);
                var center = sample.Position.Position + new Vector2(0f, 0.35f);
                var color = new Color(1f, 0.32f, 0.43f, 0.72f + 0.28f * beat);
                args.WorldHandle.DrawTextureRect(heart, Box2.CenteredAround(center, new Vector2(size)), color);
            }
        }
    }
}
