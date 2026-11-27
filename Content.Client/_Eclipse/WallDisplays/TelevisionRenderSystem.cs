using System.Linq;
using System.Numerics;
using Content.Client.Resources;
using Content.Shared._Eclipse.WallDisplays;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Map;
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
    private const string ScreenLayer = "television-live-screen";
    private static readonly Vector2i SmallFeedSize = new(384, 216);
    private static readonly Vector2i DetailedFeedSize = new(960, 540);

    private sealed class Feed
    {
        public IClydeViewport? Viewport;
        public readonly FixedEye Eye = new() { Zoom = Vector2.One };
        public TimeSpan NextRender;
        public TimeSpan DetailedUntil;
        public bool UsedByTelevision;
    }

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(EyeSystem));
        SubscribeLocalEvent<TelevisionComponent, ComponentShutdown>((uid, _, _) => Release(uid));
        _overlay = new TelevisionOverlay(this); _overlays.AddOverlay(_overlay);
    }

    public Texture? GetFeed(EntityUid camera) =>
        _feeds.TryGetValue(camera, out var feed) ? feed.Viewport?.RenderTarget.Texture : null;

    public void RequestDetailedFeed(EntityUid camera)
    {
        if (IsOnAir(camera))
            GetOrCreateFeed(camera).DetailedUntil = _timing.RealTime + TimeSpan.FromSeconds(.3);
    }

    public override void FrameUpdate(float frameTime)
    {
        var now = _timing.RealTime;
        var televisions = EntityQueryEnumerator<TelevisionComponent, SpriteComponent>();
        while (televisions.MoveNext(out _, out var television, out _))
        {
            if (television.Powered && television.Channel == TelevisionChannel.Broadcast &&
                television.Camera is { } camera && IsOnAir(camera))
            {
                GetOrCreateFeed(camera).UsedByTelevision = true;
            }
        }

        foreach (var (camera, feed) in _feeds)
        {
            var detailed = now < feed.DetailedUntil;
            if ((!feed.UsedByTelevision && !detailed) || !IsOnAir(camera) ||
                !TryComp<TransformComponent>(camera, out var xform) || xform.MapID == MapId.Nullspace)
            {
                feed.Viewport?.Dispose();
                _feeds.Remove(camera);
                continue;
            }

            feed.UsedByTelevision = false;
            var size = detailed ? DetailedFeedSize : SmallFeedSize;
            if (feed.Viewport?.Size != size)
            {
                feed.Viewport?.Dispose();
                feed.Viewport = _clyde.CreateViewport(size, name: "Public television broadcast");
                feed.Viewport.Eye = feed.Eye;
                // Higher quality changes pixel density while keeping the same field of view.
                feed.Viewport.RenderScale = new Vector2((float) size.X / SmallFeedSize.X);
                feed.NextRender = TimeSpan.Zero;
            }

            // Render before the UI draws so both displays can use the completed frame.
            feed.Viewport.AutomaticRender = now >= feed.NextRender;
            if (!feed.Viewport.AutomaticRender)
                continue;

            feed.NextRender = now + TimeSpan.FromSeconds(detailed ? 1d / 24 : .1);
            feed.Eye.Position = _transforms.GetMapCoordinates(xform);
            feed.Eye.Rotation = xform.GridUid is { } grid ? -_transforms.GetWorldRotation(grid) : Angle.Zero;
        }
    }

    private bool IsOnAir(EntityUid camera) =>
        TryComp<PublicBroadcastCameraComponent>(camera, out var source) && source.OnAir && HasComp<EyeComponent>(camera);

    private Feed GetOrCreateFeed(EntityUid camera)
    {
        if (!_feeds.TryGetValue(camera, out var feed))
            _feeds[camera] = feed = new Feed();

        return feed;
    }

    public void RenderPending(DrawingHandleScreen screen)
    {
        if (_timing.RealTime < _nextRender) return;
        _nextRender = _timing.RealTime + TimeSpan.FromSeconds(.1);
        _font ??= _resources.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 9);
        var televisions = EntityQueryEnumerator<TelevisionComponent, SpriteComponent>();
        while (televisions.MoveNext(out var uid, out var television, out var sprite))
        {
            if (!television.Powered)
            {
                if (_sprites.LayerMapTryGet((uid, sprite), ScreenLayer, out var old, false)) _sprites.LayerSetVisible((uid, sprite), old, false);
                continue;
            }

            var video = television.Channel == TelevisionChannel.Broadcast && television.Camera is { } camera
                ? GetFeed(camera)
                : null;
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
        foreach (var feed in _feeds.Values) feed.Viewport?.Dispose();
        _feeds.Clear(); _overlays.RemoveOverlay(_overlay); base.Shutdown();
    }
}

public sealed class TelevisionOverlay(TelevisionRenderSystem system) : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpaceBelowWorld;
    protected override void Draw(in OverlayDrawArgs args) => system.RenderPending(args.RenderHandle.DrawingHandleScreen);
}
