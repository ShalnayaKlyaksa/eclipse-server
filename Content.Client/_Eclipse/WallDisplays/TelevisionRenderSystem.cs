using System.Linq;
using System.Numerics;
using Content.Client.Resources;
using Content.Shared._Eclipse.WallDisplays;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Eclipse.WallDisplays;

/// <summary>One reusable video viewport per public camera; the same feed is drawn into TV sprites and viewing windows.</summary>
public sealed partial class TelevisionRenderSystem : EntitySystem
{
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;
    [Dependency] private readonly SharedTransformSystem _transforms = default!;
    [Dependency] private readonly IResourceCache _resources = default!;
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private readonly Dictionary<EntityUid, IRenderTexture> _screens = new();
    private readonly Dictionary<EntityUid, Feed> _feeds = new();
    private TelevisionOverlay _overlay = default!;
    private Font? _font;
    private TimeSpan _nextRender;
    private bool _rendering;
    private const string ScreenLayer = "television-live-screen";
    private sealed class Feed(IClydeViewport viewport)
    {
        public IClydeViewport Viewport = viewport;
        public FixedEye Eye = new() { Zoom = Vector2.One };
        public TimeSpan NextRender;
        public TimeSpan DetailedUntil;
    }

    public override void Initialize()
    {
        SubscribeLocalEvent<TelevisionComponent, ComponentShutdown>((uid, _, _) => Release(uid));
        _overlay = new TelevisionOverlay(this); _overlays.AddOverlay(_overlay);
    }

    public Texture? RenderFeed(EntityUid camera, bool detailed = false)
    {
        if (!TryComp<PublicBroadcastCameraComponent>(camera, out var source) || !source.OnAir || !HasComp<EyeComponent>(camera)) return null;
        if (!_feeds.TryGetValue(camera, out var feed))
        {
            feed = new Feed(CreateViewport(new Vector2i(384, 216)));
            feed.Viewport.Eye = feed.Eye; _feeds[camera] = feed;
        }
        if (detailed) feed.DetailedUntil = _timing.RealTime + TimeSpan.FromSeconds(.3);
        var highQuality = _timing.RealTime < feed.DetailedUntil;
        var size = highQuality ? new Vector2i(960, 540) : new Vector2i(384, 216);
        if (feed.Viewport.Size != size)
        {
            feed.Viewport.Dispose(); feed.Viewport = CreateViewport(size);
            feed.Viewport.Eye = feed.Eye; feed.NextRender = TimeSpan.Zero;
        }
        // Preserve the same field of view when promoting a feed to the large viewing window.
        feed.Eye.Zoom = new Vector2(384f / size.X);
        feed.Eye.Position = _transforms.GetMapCoordinates(camera);
        if (_timing.RealTime >= feed.NextRender)
        {
            feed.NextRender = _timing.RealTime + TimeSpan.FromSeconds(highQuality ? 1d / 24 : .1);
            feed.Viewport.Render();
        }
        return feed.Viewport.RenderTarget.Texture;
    }

    private IClydeViewport CreateViewport(Vector2i size)
    {
        var viewport = _clyde.CreateViewport(size, name: "Public television broadcast");
        viewport.AutomaticRender = false; viewport.ClearColor = Color.Black;
        return viewport;
    }

    public void RenderPending(DrawingHandleScreen screen)
    {
        if (_rendering || _timing.RealTime < _nextRender) return;
        _nextRender = _timing.RealTime + TimeSpan.FromSeconds(.1);
        _rendering = true;
        try
        {
            _font ??= _resources.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 9);
            var usedCameras = new HashSet<EntityUid>();
            var televisions = EntityQueryEnumerator<TelevisionComponent, SpriteComponent>();
            while (televisions.MoveNext(out var uid, out var television, out var sprite))
            {
                if (!television.Powered)
                {
                    if (_sprites.LayerMapTryGet((uid, sprite), ScreenLayer, out var old, false)) _sprites.LayerSetVisible((uid, sprite), old, false);
                    continue;
                }
                Texture? video = null;
                if (television.Channel == TelevisionChannel.Broadcast && television.Camera is { } camera)
                { usedCameras.Add(camera); video = RenderFeed(camera); }
                if (!_screens.TryGetValue(uid, out var target))
                    _screens[uid] = target = _clyde.CreateRenderTarget(new Vector2i(256, 128), new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "Television screen");
                var transform = screen.GetTransform();
                screen.RenderInRenderTarget(target, () =>
                {
                    screen.SetTransform(Matrix3x2.Identity);
                    var rect = new UIBox2(8, 8, 248, 112);
                    screen.DrawRect(rect, Color.FromHex("#0B141D"));
                    if (video != null)
                    {
                        screen.DrawTextureRect(video, new UIBox2(36, 8, 220, 112));
                        screen.DrawRect(new UIBox2(12, 12, 51, 28), Color.FromHex("#A3282C"));
                        screen.DrawString(_font, new Vector2(15, 12), "LIVE", Color.White);
                    }
                    else if (television.Channel == TelevisionChannel.Broadcast)
                    {
                        screen.DrawString(_font, new Vector2(18, 43), Loc.GetString("television-no-signal"), Color.FromHex("#91B6B9"));
                        screen.DrawString(_font, new Vector2(18, 65), Loc.GetString("television-start-camera"), Color.FromHex("#668185"));
                    }
                    else DrawArcade(screen, television.Channel);
                }, Color.Transparent);
                screen.SetTransform(transform);
                var layer = _sprites.LayerMapReserve((uid, sprite), ScreenLayer);
                _sprites.LayerSetTexture((uid, sprite), layer, target.Texture);
                _sprites.LayerSetScale((uid, sprite), layer, new Vector2(.25f));
                sprite.LayerSetShader(layer, "unshaded");
                _sprites.LayerSetVisible((uid, sprite), layer, true);
            }
            foreach (var camera in _feeds.Keys.Except(usedCameras).ToArray())
            { _feeds[camera].Viewport.Dispose(); _feeds.Remove(camera); }
        }
        finally { _rendering = false; }
    }

    private void DrawArcade(DrawingHandleScreen screen, TelevisionChannel channel)
    {
        var time = (float) _timing.RealTime.TotalSeconds;
        if (channel == TelevisionChannel.Flappy)
        {
            for (var i = 0; i < 3; i++)
            {
                var x = 16 + (i * 73 + 220 - time * 22 % 220) % 220;
                screen.DrawRect(new UIBox2(x, 21, x + 12, 43), Color.FromHex("#3C936B"));
                screen.DrawRect(new UIBox2(x, 79, x + 12, 103), Color.FromHex("#3C936B"));
            }
            var y = 57 + MathF.Sin(time * 2) * 10;
            screen.DrawRect(new UIBox2(95, y, 104, y + 7), Color.FromHex("#E3BE64"));
        }
        else
        {
            var colors = new[] { "#66B8C2", "#D7A859", "#B48AC5", "#7CB890" };
            for (var i = 0; i < 28; i++)
            {
                var x = 85 + i % 10 * 10; var y = 91 - i / 10 * 10;
                screen.DrawRect(new UIBox2(x, y, x + 9, y + 9), Color.FromHex(colors[i % 4]));
            }
        }
        screen.DrawString(_font!, new Vector2(30, 22), Loc.GetString("television-arcade"), Color.White);
    }

    private void Release(EntityUid uid)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite)) _sprites.RemoveLayer((uid, sprite), ScreenLayer, false);
        if (_screens.Remove(uid, out var target)) target.Dispose();
    }
    public override void Shutdown()
    {
        foreach (var uid in _screens.Keys.ToArray()) Release(uid);
        foreach (var feed in _feeds.Values) feed.Viewport.Dispose();
        _feeds.Clear(); _overlays.RemoveOverlay(_overlay); base.Shutdown();
    }
}

public sealed class TelevisionOverlay(TelevisionRenderSystem system) : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpaceBelowWorld;
    protected override void Draw(in OverlayDrawArgs args) => system.RenderPending(args.RenderHandle.DrawingHandleScreen);
}
