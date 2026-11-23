using System.Numerics;
using Content.Client._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.AdvancedHealth;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;

namespace Content.Client.HealthAnalyzer.UI;

public enum MedicalBodyViewMode : byte { General, Organs, Protection }

public sealed record MedicalOrganMarker(BodyPartSlot Region, string Category, string Name, EntityUid Entity, float Function = 1f);

public sealed partial class AdvancedHealthBodyDollControl
{
    private MedicalBodyViewMode _viewMode;
    public EntityUid? SelectedOrgan { get; set; }
    public event Action<EntityUid>? OnOrganSelected;
    public MedicalBodyViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            _viewMode = value;
            foreach (var image in _organViews.Values) image.View.Visible = value == MedicalBodyViewMode.Organs;
        }
    }
    private readonly Dictionary<BodyPartSlot, float> _layerValues = new();
    private readonly List<MedicalOrganMarker> _organs = new();
    private readonly Dictionary<(EntityUid Entity, string Sprite), (TextureRect View, OrganArt Art)> _organViews = new();
    private Font? _layerFont;
    private int _layerFontSize;

    // Whole square PNGs are scaled uniformly; transparent margins and all painted pixels are retained.
    private sealed record OrganArt(string Category, string Sprite, Vector2 Centre, float Side, int Depth = 0);
    private static readonly OrganArt[] OrganArtwork =
    [
        new("Brain", "brain", new(63.75f, 17.5f), 16, 4),
        new("Eyes", "eye_right_mirrored", new(58.5f, 21), 8.25f, 5),
        new("Eyes", "eye_right", new(69, 21), 8.25f, 5),
        new("Tongue", "tongue", new(63.75f, 28.75f), 3.8f, 5),
        new("Lungs", "lung_left", new(68, 40.25f), 16, 6),
        new("Lungs", "lung_right", new(59.5f, 40.25f), 16, 6),
        new("Heart", "heart", new(64.5f, 43.5f), 8, 8),
        new("Liver", "liver", new(62.5f, 49.5f), 12, 3),
        new("Stomach", "stomach", new(64, 49.5f), 8.5f, 4),
        new("Kidneys", "kidney_left", new(58.75f, 55), 6, 3),
        new("Kidneys", "kidney_right", new(69.25f, 55), 6, 3),
        new("Appendix", "appendix", new(59.5f, 61.75f), 4.5f, 5),
        new("Intestines", "intestines", new(63.75f, 59.75f), 14.5f, 2),
        new("Uterus", "uterus", new(64, 59.5f), 12, 4),
        new("OvaryLeft", "ovary_left", new(69, 58.25f), 3.75f, 4),
        new("OvaryRight", "ovary_right", new(59, 58.25f), 4.25f, 4),
        new("VaginalCanal", "vagina_internal", new(64, 64.5f), 4.75f, 5),
        new("Prostate", "prostate", new(63.75f, 64.25f), 4.25f, 4),
    ];

    public void SetLayerValues(IEnumerable<(BodyPartSlot Slot, float? Value)> values)
    {
        _layerValues.Clear();
        foreach (var (slot, value) in values)
        {
            if (value is not { } fraction || !float.IsFinite(fraction)) continue;
            var displayed = DisplaySlot(slot);
            var clamped = Math.Clamp(fraction, 0f, 1f);
            _layerValues[displayed] = _layerValues.TryGetValue(displayed, out var previous) ? Math.Min(previous, clamped) : clamped;
        }
    }

    public float? GetLayerValue(BodyPartSlot slot) => _layerValues.TryGetValue(DisplaySlot(slot), out var value) ? value : null;

    public void SetOrgans(IEnumerable<MedicalOrganMarker> organs)
    {
        _organs.Clear();
        _organs.AddRange(organs);
        var wanted = new HashSet<(EntityUid Entity, string Sprite)>();
        foreach (var organ in _organs)
        {
            foreach (var art in OrganArtwork)
            {
                if (art.Category != organ.Category) continue;
                var key = (organ.Entity, art.Sprite);
                wanted.Add(key);
                if (!_organViews.TryGetValue(key, out var image))
                {
                    var view = new MedicalOrganTexture(IoCManager.Resolve<IResourceCache>(),
                        new Robust.Shared.Utility.ResPath($"/Textures/_Eclipse/Interface/AdvancedHealth/Organs48/{art.Sprite}.png"))
                    {
                        Name = "MedicalOrgan_" + art.Sprite,
                        MouseFilter = MouseFilterMode.Stop, Stretch = TextureRect.StretchMode.KeepAspectCentered,
                        ToolTip = organ.Name,
                    };
                    view.OnKeyBindDown += args =>
                    {
                        if (args.Function != Robust.Shared.Input.EngineKeyFunctions.UIClick) return;
                        SelectedOrgan = organ.Entity;
                        OnOrganSelected?.Invoke(organ.Entity);
                        args.Handle();
                    };
                    image = (view, art);
                    _organViews.Add(key, image);
                    AddChild(view);
                }
                image.View.ToolTip = organ.Name;
                image.View.Modulate = OrganColor(organ.Function);
                image.View.Visible = ViewMode == MedicalBodyViewMode.Organs;
            }
        }
        var removed = new List<(EntityUid, string)>();
        foreach (var (key, image) in _organViews)
            if (!wanted.Contains(key))
            {
                RemoveChild(image.View);
                removed.Add(key);
            }
        foreach (var key in removed) _organViews.Remove(key);
        var ordered = new List<(TextureRect View, OrganArt Art)>(_organViews.Values);
        ordered.Sort((left, right) => left.Art.Depth.CompareTo(right.Art.Depth));
        for (var index = 0; index < ordered.Count; index++) ordered[index].View.SetPositionInParent(index);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var scale = finalSize.X / 128f;
        foreach (var image in _organViews.Values)
        {
            var size = new Vector2(image.Art.Side) * scale;
            image.View.SetSize = size;
            image.View.Measure(size);
            image.View.Arrange(UIBox2.FromDimensions(image.Art.Centre * scale - size / 2, size));
        }
        return finalSize;
    }

    private void DrawMedicalLayer(DrawingHandleScreen handle, UIBox2 box)
    {
        handle.DrawTextureRect(_base, box, Color.White.WithAlpha(ViewMode == MedicalBodyViewMode.Protection ? 0.8f : 0.18f));
        var scale = box.Width / 128f;
        if (ViewMode == MedicalBodyViewMode.Protection)
        {
            var fontSize = Math.Clamp((int) (scale * 2.5f), 9, 17);
            if (_layerFont == null || _layerFontSize != fontSize)
            {
                _layerFontSize = fontSize;
                _layerFont = HealthMenuStyle.Mono(fontSize, true);
            }
            var font = _layerFont;
            foreach (var (slot, fraction) in _layerValues)
            {
                if (_masks.TryGetValue(slot, out var mask))
                    handle.DrawTextureRect(mask, box, HealthMenuStyle.Amber.WithAlpha(0.08f + fraction * 0.45f));
            }
            foreach (var (slot, fraction) in _layerValues)
            {
                if (!Centroids.TryGetValue(slot, out var centre)) continue;
                var text = $"{MathF.Round(fraction * 100)}%";
                var dimensions = handle.GetDimensions(font, text, UIScale);
                var origin = box.TopLeft + centre * scale - dimensions / 2;
                handle.DrawRect(UIBox2.FromDimensions(origin - new Vector2(2), dimensions + new Vector2(4)), Color.FromHex("#100b06ee"));
                handle.DrawString(font, origin, text, UIScale, HealthMenuStyle.AmberBright);
            }
        }
        if (ViewMode == MedicalBodyViewMode.Organs)
        {
            foreach (var (key, image) in _organViews)
            {
                if (key.Entity != SelectedOrgan) continue;
                var size = new Vector2(image.Art.Side * scale);
                handle.DrawRect(UIBox2.FromDimensions(image.Art.Centre * scale - size / 2, size), HealthMenuStyle.AmberBright, false);
            }
        }
        else if ((SelectedSlot ?? HoveredSlot) is { } selected)
        {
            // A subdued tint leaves the organ artwork visible under selection.
            if (_masks.TryGetValue(selected, out var mask))
                handle.DrawTextureRect(mask, box, HealthMenuStyle.Amber.WithAlpha(0.12f));
        }
    }

    private static Color OrganColor(float value)
        => value >= 0.9f ? Color.White : value >= 0.7f ? Color.FromHex("#e8a54c")
            : value >= 0.4f ? Color.FromHex("#ed7552") : HealthMenuStyle.Red;
}
