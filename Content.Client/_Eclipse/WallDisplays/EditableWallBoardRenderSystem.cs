using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client.Resources;
using Content.Shared._Eclipse.WallDisplays;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;

namespace Content.Client._Eclipse.WallDisplays;

/// <summary>Text is a lit sprite layer, so normal wall occlusion, rotation and lighting apply.</summary>
public sealed partial class EditableWallBoardRenderSystem : EntitySystem
{
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IResourceCache _resources = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;
    [Dependency] private readonly IOverlayManager _overlays = default!;
    private readonly HashSet<EntityUid> _pending = new();
    private readonly Dictionary<EntityUid, IRenderTexture> _textures = new();
    private BoardTextureOverlay _overlay = default!;
    private readonly Dictionary<int, Font> _fonts = new();
    private const string TextLayer = "wall-board-text";
    private const int Resolution = 4;

    public override void Initialize()
    {
        SubscribeLocalEvent<EditableWallBoardComponent, ComponentStartup>((uid, _, _) => Invalidate(uid));
        SubscribeLocalEvent<EditableWallBoardComponent, AfterAutoHandleStateEvent>((uid, _, _) => Invalidate(uid));
        SubscribeLocalEvent<EditableWallBoardComponent, ComponentShutdown>((uid, _, _) => Release(uid));
        _overlay = new BoardTextureOverlay(this); _overlays.AddOverlay(_overlay);
    }

    public void Invalidate(EntityUid uid) => _pending.Add(uid);

    private void Release(EntityUid uid)
    {
        _pending.Remove(uid);
        if (_textures.Remove(uid, out var texture))
        {
            if (TryComp<SpriteComponent>(uid, out var sprite)) _sprites.RemoveLayer((uid, sprite), TextLayer, false);
            texture.Dispose();
        }
    }

    public override void Shutdown()
    {
        foreach (var uid in _textures.Keys.ToArray()) Release(uid);
        _overlays.RemoveOverlay(_overlay);
        base.Shutdown();
    }

    /// <summary>Only changed boards are rendered. Targets are reused until size change or entity removal.</summary>
    public void RenderPending(DrawingHandleScreen screen)
    {
        foreach (var uid in _pending.ToArray())
        {
            _pending.Remove(uid);
            if (!TryComp<EditableWallBoardComponent>(uid, out var board) || !TryComp<SpriteComponent>(uid, out var sprite)) continue;
            var fontSize = EditableWallBoardComponent.ValidFontSize(board.FontSize) ? board.FontSize : 10;
            if (!_fonts.TryGetValue(fontSize, out var font))
                _fonts[fontSize] = font = _resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", fontSize);
            var size = new Vector2i(Math.Clamp(board.Width, 1, 3) * 32 * Resolution, 32 * Resolution);
            if (!_textures.TryGetValue(uid, out var target) || target.Size != size)
            {
                target?.Dispose();
                _textures[uid] = target = _clyde.CreateRenderTarget(size, new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "Wall board text");
            }
            var lines = Wrap(board.Text, size.X - 32, font);
            var lineHeight = font.GetLineHeight(1f);
            var maxLines = Math.Max(1, (int) ((size.Y - 32) / lineHeight));
            var transform = screen.GetTransform();
            screen.RenderInRenderTarget(target, () =>
            {
                screen.SetTransform(Matrix3x2.Identity);
                for (var i = 0; i < Math.Min(lines.Count, maxLines); i++)
                {
                    var text = lines[i];
                    if (i == maxLines - 1 && lines.Count > maxLines) text = text[..Math.Max(0, text.Length - 3)] + "…";
                    screen.DrawString(font, new Vector2(16, 14 + i * lineHeight), text, Color.FromHex("#E7EBDD"));
                }
            }, Color.Transparent);
            screen.SetTransform(transform);
            var layer = _sprites.LayerMapReserve((uid, sprite), TextLayer);
            _sprites.LayerSetTexture((uid, sprite), layer, target.Texture);
            _sprites.LayerSetScale((uid, sprite), layer, new Vector2(1f / Resolution));
            _sprites.LayerSetVisible((uid, sprite), layer, true);
        }
    }

    private static List<string> Wrap(string text, float width, Font font)
    {
        var lines = new List<string>(); var line = new StringBuilder(); var advance = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            var next = font.GetCharMetrics(rune, 1f)?.Advance ?? 0;
            if (rune.Value == '\n' || advance + next > width)
            {
                lines.Add(line.ToString()); line.Clear(); advance = 0;
                if (rune.Value == '\n') continue;
            }
            line.Append(rune.ToString()); advance += next;
        }
        lines.Add(line.ToString()); return lines;
    }
}

public sealed class BoardTextureOverlay(EditableWallBoardRenderSystem system) : Overlay
{
    public override OverlaySpace Space => OverlaySpace.ScreenSpaceBelowWorld;
    protected override void Draw(in OverlayDrawArgs args) => system.RenderPending(args.RenderHandle.DrawingHandleScreen);
}
