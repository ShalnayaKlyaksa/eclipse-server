using System.Linq;
using System.Diagnostics;
using System.Numerics;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Utility;

namespace Content.Client.PDA;

/// <summary>Bundled desktop artwork; no runtime network image requests.</summary>
public sealed class PdaDesktopCanvas : LayoutContainer
{
    public string Wallpaper = "orbit";
    public string Time = "00:00:00";
    public string Owner = "";
    public string Address = "";
    public string PublicId = "";
    public bool Card;
    public bool Decor;
    public bool Photo;
    public bool CustomWallpaper;
    public string CardText = "";
    public Texture? PersonalImage;
    public event Action<Vector2>? ContextRequested;
    public event Action<Vector2>? Arranging;
    private readonly Font _clockFont = IoCManager.Resolve<IResourceCache>().GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 34);
    private readonly Font _timerFont = IoCManager.Resolve<IResourceCache>().GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 18);
    private readonly Font _small = IoCManager.Resolve<IResourceCache>().GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 10);
    private readonly Font _micro = IoCManager.Resolve<IResourceCache>().GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 8);
    private readonly StyleBox _clockPanel = PdaTheme.Box("#152028", "#555B54", radius: 12);
    private readonly StyleBox _timerPanel = PdaTheme.Box("#263139", "#39434A", radius: 7);

    public PdaDesktopCanvas()
    {
        InheritChildMeasure = false;
        RectClipContent = true;
        MouseFilter = MouseFilterMode.Stop;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        Arranging?.Invoke(finalSize);
        foreach (var child in Children) child.Measure(PdaDesktopIcon.IconSize);
        return base.ArrangeOverride(finalSize);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        foreach (var child in Children) child.Measure(PdaDesktopIcon.IconSize);
        return Vector2.Zero;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIRightClick) return;
        ContextRequested?.Invoke(args.PointerLocation.Position / UIScale);
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen h)
    {
        base.Draw(h);
        DrawWallpaper(h, PixelSize, Wallpaper);
        if (CustomWallpaper && PersonalImage != null)
        {
            DrawPicture(h, PersonalImage, PixelSizeBox);
            h.DrawRect(PixelSizeBox, Color.FromHex("#08121C70"));
        }
        var right = PixelWidth - 220 * UIScale;
        var ink = Color.FromHex("#D9E6EF");
        var muted = Color.FromHex("#91ADB9");
        _clockPanel.Draw(h, new UIBox2(right, 14 * UIScale, PixelWidth - 14 * UIScale, 190 * UIScale), UIScale);
        var left = right + 18 * UIScale;
        var now = DateTime.Now;
        h.DrawCircle(new Vector2(left + 3 * UIScale, 34 * UIScale), 2 * UIScale, Color.FromHex("#CBC899"));
        h.DrawString(_micro, new Vector2(left + 13 * UIScale, 27 * UIScale), Loc.GetString("pda-os-local-time"), UIScale, muted);
        h.DrawString(_clockFont, new Vector2(left - 2 * UIScale, 42 * UIScale), now.ToString("HH:mm"), UIScale, ink);
        // Match the in-world calendar already used by the document printer.
        h.DrawString(_small, new Vector2(left, 96 * UIScale), now.AddYears(1000).ToString("dd.MM.yyyy"), UIScale, muted);
        if (PublicId.Length > 0) h.DrawString(_micro, new Vector2(left, 112 * UIScale), "ID / " + (PublicId.Length > 18 ? PublicId[..17] + "…" : PublicId), UIScale, muted);
        _timerPanel.Draw(h, new UIBox2(left - 4 * UIScale, 124 * UIScale, PixelWidth - 28 * UIScale, 176 * UIScale), UIScale);
        h.DrawString(_micro, new Vector2(left + 7 * UIScale, 131 * UIScale), Loc.GetString("pda-os-shift-time"), UIScale, muted);
        h.DrawString(_timerFont, new Vector2(left + 7 * UIScale, 146 * UIScale), Time, UIScale, ink);
        if (Card)
        {
            _clockPanel.Draw(h, new UIBox2(right, 235 * UIScale, PixelWidth - 14 * UIScale, 328 * UIScale), UIScale);
            var lines = WrapCard(CardText, 23);
            for (var i = 0; i < lines.Count; i++)
                h.DrawString(_small, new Vector2(right + 12 * UIScale, (249 + i * 17) * UIScale), lines[i], UIScale, ink);
        }
        if (Photo && PersonalImage != null)
        {
            var top = Card ? 342 : 235;
            var box = new UIBox2(right, top * UIScale, PixelWidth - 22 * UIScale, (top + 110) * UIScale);
            h.DrawRect(new UIBox2(box.TopLeft - new Vector2(4 * UIScale), box.BottomRight + new Vector2(4 * UIScale)), Color.FromHex("#B3CBCB"));
            DrawPicture(h, PersonalImage, box);
        }
        if (Decor)
        {
            var p = new Vector2(PixelWidth - 86 * UIScale, PixelHeight - 82 * UIScale);
            h.DrawCircle(p - new Vector2(0, 20 * UIScale), 17 * UIScale, Color.FromHex("#76DAC1"));
            h.DrawCircle(p - new Vector2(17, 12) * UIScale, 12 * UIScale, Color.FromHex("#479B88"));
            h.DrawCircle(p + new Vector2(16, -16) * UIScale, 12 * UIScale, Color.FromHex("#9BE0B5"));
            h.DrawRect(UIBox2.FromDimensions(p, new Vector2(3, 25) * UIScale), Color.FromHex("#728F7D"));
            h.DrawRect(UIBox2.FromDimensions(p + new Vector2(-15, 18) * UIScale, new Vector2(34, 25) * UIScale), Color.FromHex("#B7B5AA"));
        }
    }

    private static List<string> WrapCard(string text, int width)
    {
        var result = new List<string>();
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            for (var i = 0; i < Math.Max(1, line.Length) && result.Count < 4; i += width)
                result.Add(line.Substring(i, Math.Min(width, line.Length - i)));
        }
        return result;
    }

    private static void DrawPicture(DrawingHandleScreen h, Texture texture, UIBox2 target)
    {
        var size = (Vector2) texture.Size;
        var scale = Math.Max(target.Width / size.X, target.Height / size.Y);
        var source = target.Size / scale;
        var origin = (size - source) / 2;
        h.DrawTextureRectRegion(texture, target, UIBox2.FromDimensions(origin, source));
    }

    public static void DrawWallpaper(DrawingHandleScreen h, Vector2 size, string id)
    {
        var path = id switch
        {
            "aurora" => "/Textures/Eclipse/MainMenu/LobbyBackgrounds/aurora_eclipse.png",
            "sunset" => "/Textures/Eclipse/MainMenu/LobbyBackgrounds/sunset_rings.png",
            "blueprint" => "/Textures/Eclipse/MainMenu/LobbyBackgrounds/twilight_spire.png",
            _ => "/Textures/_Eclipse/Interface/PDA/empire-wallpaper.png",
        };
        var texture = IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>(path).Texture;
        DrawPicture(h, texture, UIBox2.FromDimensions(Vector2.Zero, size));
        if (id is "aurora" or "sunset" or "blueprint")
            h.DrawRect(UIBox2.FromDimensions(Vector2.Zero, size), Color.FromHex("#07131850"));
    }
}
public sealed class PdaWallpaperPreview(string id) : Control
{
    protected override void Draw(DrawingHandleScreen handle) => PdaDesktopCanvas.DrawWallpaper(handle, PixelSize, id);
}

/// <summary>Double click opens; dragging moves a shortcut, and right click exposes its actions.</summary>
public sealed class PdaDesktopIcon : ContainerButton
{
    public static readonly Vector2 IconSize = new(88, 72);
    public static readonly Vector2 CellSize = new(94, 80);
    public readonly string AppId;
    public bool Unread;
    public event Action? OpenRequested;
    public event Action<Vector2>? ContextRequested;
    public event Action? MoveFinished;
    private Vector2 _pointerStart;
    private Vector2 _positionStart;
    private bool _moving;
    private bool _dragged;
    public bool IsDragging => _moving && _dragged;
    private long _lastClick;

    public PdaDesktopIcon(string id, string title, Color tint, SpriteSpecifier? sprite = null)
    {
        AppId = id;
        ToolTip = title;
        SetSize = IconSize;
        ModulateSelfOverride = Color.White;
        StyleBoxOverride = new StyleBoxFlat();
        _title = title;
        var iconName = IconName(id);
        Control image;
        if (iconName != null || sprite == null)
            image = new TextureRect { TexturePath = "/Textures/_Eclipse/Interface/PDA/Icons/Field/" + (iconName ?? "fallback") + ".png", CanShrink = true, Stretch = TextureRect.StretchMode.KeepAspectCentered };
        else
        {
            var animated = new AnimatedTextureRect();
            animated.SetFromSpriteSpecifier(sprite);
            image = animated;
        }
        image.SetSize = new Vector2(36, 36);
        image.HorizontalAlignment = HAlignment.Center;
        image.VerticalAlignment = VAlignment.Top;
        image.Margin = new Thickness(0);
        image.MouseFilter = MouseFilterMode.Ignore;
        AddChild(image);
        OnButtonDown += _ =>
        {
            if (Parent == null) return;
            foreach (var sibling in Parent.Children)
                if (sibling is PdaDesktopIcon icon) icon._selected = icon == this;
        };
        OnPressed += _ =>
        {
            if (_dragged) return;
            var now = Stopwatch.GetTimestamp();
            if (_lastClick != 0 && Stopwatch.GetElapsedTime(_lastClick, now).TotalSeconds < .45)
            { _lastClick = 0; OpenRequested?.Invoke(); }
            else _lastClick = now;
        };
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.UIRightClick)
        { ContextRequested?.Invoke(args.PointerLocation.Position / UIScale); args.Handle(); return; }
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick) return;
        _pointerStart = args.PointerLocation.Position / UIScale;
        _positionStart = Position;
        _moving = true;
        _dragged = false;
        SetPositionLast();
        args.Handle();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (!_moving || Parent == null) return;
        var delta = args.GlobalPixelPosition.Position / UIScale - _pointerStart;
        if (delta.LengthSquared() > 25) _dragged = true;
        if (!_dragged) return;
        var position = Vector2.Clamp(_positionStart + delta, Vector2.Zero, Vector2.Max(Vector2.Zero, Parent.Size - Size));
        Place(position);
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        if (args.Function != EngineKeyFunctions.UIClick) { base.KeyBindUp(args); return; }
        var moved = _moving && _dragged;
        _moving = false;
        base.KeyBindUp(args);
        if (moved) { _lastClick = 0; MoveFinished?.Invoke(); }
        args.Handle();
    }

    protected override void ControlFocusExited()
    {
        var moved = _moving && _dragged;
        _moving = false;
        base.ControlFocusExited();
        if (moved) MoveFinished?.Invoke();
    }

    public void Place(Vector2 position)
    {
        LayoutContainer.SetPosition(this, position);
        LayoutContainer.SetMarginLeft(this, position.X);
        LayoutContainer.SetMarginTop(this, position.Y);
        LayoutContainer.SetMarginRight(this, position.X + IconSize.X);
        LayoutContainer.SetMarginBottom(this, position.Y + IconSize.Y);
    }

    private readonly string _title;
    private readonly Font _captionFont = IoCManager.Resolve<IResourceCache>().GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 9);
    private bool _selected;
    private string[]? _captionLines;
    private float _captionScale;

    public static string? IconName(string id)
    {
        if (!id.StartsWith("cartridge:")) return id;
        if (id.Contains("mail")) return "mail";
        if (id.Contains("manifest")) return "crew";
        if (id.Contains("note")) return "notes";
        if (id.Contains("task")) return "tasks";
        if (id.Contains("news")) return "news";
        if (id.Contains("net-probe")) return "network";
        if (id.Contains("log-probe")) return "logs";
        if (id.Contains("wanted")) return "wanted";
        if (id.Contains("tracker")) return "tracker";
        return null;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (Unread) handle.DrawCircle(new Vector2(PixelWidth / 2 + 17 * UIScale, 5 * UIScale), 3 * UIScale, Color.FromHex("#E0B972"));
        if (_captionLines == null || _captionScale != UIScale)
        {
            _captionScale = UIScale;
            var lines = new List<string>();
            var line = "";
            foreach (var word in _title.Split(' '))
            {
                var next = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && handle.GetDimensions(_captionFont, next, UIScale).X > PixelWidth - 4 * UIScale)
                { lines.Add(line); line = word; }
                else line = next;
            }
            if (line.Length > 0) lines.Add(line);
            _captionLines = lines.Take(2).ToArray();
        }
        if (_selected || IsHovered)
            _selection.Draw(handle, new UIBox2(1, 37 * UIScale, PixelWidth - 1, PixelHeight - 1), UIScale);
        for (var i = 0; i < _captionLines.Length; i++)
        {
            var text = _captionLines[i];
            var width = handle.GetDimensions(_captionFont, text, UIScale).X;
            var position = new Vector2((PixelWidth - width) / 2, (38 + i * 14) * UIScale);
            handle.DrawString(_captionFont, position + new Vector2(UIScale), text, UIScale, Color.FromHex("#000000CC"));
            handle.DrawString(_captionFont, position, text, UIScale, Color.FromHex(IsHovered ? "#FFFFFF" : "#E3EBEC"));
        }
    }

    private readonly StyleBox _selection = PdaTheme.Box("#363A32", "#7E785F", radius: 5);
}

