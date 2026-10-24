using System.Numerics;
using Content.Shared._Eclipse.AdvancedHealth;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.HealthAnalyzer.UI;

public sealed class AdvancedHealthBodyDollControl : Control
{
    private readonly Texture _base;
    private readonly Texture _bloodLight;
    private readonly Texture _bloodMedium;
    private readonly Texture _bloodHeavy;

    /// <summary>Per-zone white silhouette masks, tinted at draw time to follow the real body shape.</summary>
    private readonly Dictionary<BodyPartSlot, Texture> _masks = new();

    /// <summary>Pixel-space centroids of each mask silhouette (128x128 texture space).</summary>
    private static readonly Dictionary<BodyPartSlot, Vector2> Centroids = new()
    {
        [BodyPartSlot.Head] = new(63.5f, 19.9f),
        [BodyPartSlot.Neck] = new(63.5f, 31.5f),
        [BodyPartSlot.Chest] = new(63.5f, 42.5f),
        [BodyPartSlot.Abdomen] = new(63.5f, 58.7f),
        [BodyPartSlot.Pelvis] = new(63.5f, 63.5f),
        [BodyPartSlot.LeftUpperArm] = new(47.5f, 39.0f),
        [BodyPartSlot.LeftForearm] = new(30.7f, 40.1f),
        [BodyPartSlot.LeftHand] = new(14.7f, 39.8f),
        [BodyPartSlot.RightUpperArm] = new(79.5f, 39.0f),
        [BodyPartSlot.RightForearm] = new(96.3f, 40.0f),
        [BodyPartSlot.RightHand] = new(112.3f, 39.8f),
        [BodyPartSlot.LeftThigh] = new(55.5f, 75.3f),
        [BodyPartSlot.LeftShin] = new(55.2f, 93.5f),
        [BodyPartSlot.LeftFoot] = new(54.3f, 109.2f),
        [BodyPartSlot.RightThigh] = new(71.5f, 75.3f),
        [BodyPartSlot.RightShin] = new(71.8f, 93.5f),
        [BodyPartSlot.RightFoot] = new(72.7f, 109.2f),
    };

    private readonly Dictionary<BodyPartSlot, BodyPartUiState> _states = new();
    private readonly HashSet<BodyPartSlot> _failedFunctions = new();

    /// <summary>Accumulated time driving the severe/critical tremble and glitch effect.</summary>
    private float _effectTime;

    public event Action<BodyPartSlot>? OnPartSelected;
    public event Action<BodyPartSlot>? OnPartHovered;
    /// <summary>Fired when a part is clicked (used to act on it, e.g. treat it).</summary>
    public event Action<BodyPartSlot>? OnPartActivated;
    public BodyPartSlot? SelectedSlot { get; set; }
    public BodyPartSlot? HoveredSlot { get; private set; }

    /// <summary>Neutral mannequin for aim selection — no wound overlays, all parts clickable.</summary>
    public bool AimMode { get; private set; }

    /// <summary>When set, moving the cursor over a part selects it (click is reserved for actions).</summary>
    public bool HoverSelect { get; set; }
    public bool EffectsEnabled { get; set; } = true;
    public bool GroupPelvisWithAbdomen { get; set; }
    public bool GroupNeckWithHead { get; set; }
    public BodyPartSlot DisplaySlot(BodyPartSlot slot) => slot switch
    {
        BodyPartSlot.Pelvis when GroupPelvisWithAbdomen => BodyPartSlot.Abdomen,
        BodyPartSlot.Neck when GroupNeckWithHead => BodyPartSlot.Head,
        _ => slot,
    };

    private readonly HashSet<BodyPartSlot> _enabled = new();

    private static readonly BodyPartSlot[] AimSlots =
    [
        BodyPartSlot.Head,
        BodyPartSlot.Chest,
        BodyPartSlot.Abdomen,
        BodyPartSlot.LeftUpperArm,
        BodyPartSlot.LeftForearm,
        BodyPartSlot.LeftHand,
        BodyPartSlot.RightUpperArm,
        BodyPartSlot.RightForearm,
        BodyPartSlot.RightHand,
        BodyPartSlot.LeftThigh,
        BodyPartSlot.LeftShin,
        BodyPartSlot.LeftFoot,
        BodyPartSlot.RightThigh,
        BodyPartSlot.RightShin,
        BodyPartSlot.RightFoot,
    ];

    // Bounding boxes (128-space) used only for click hit-testing.
    private static readonly Dictionary<BodyPartSlot, UIBox2> Regions = new()
    {
        [BodyPartSlot.Head] = new(53, 8, 74, 32),
        [BodyPartSlot.Neck] = new(59, 29, 68, 34),
        [BodyPartSlot.Chest] = new(54, 32, 73, 52),
        [BodyPartSlot.Abdomen] = new(53, 52, 74, 66),
        [BodyPartSlot.Pelvis] = new(54, 60, 74, 67),
        [BodyPartSlot.LeftUpperArm] = new(39, 32, 57, 45),
        [BodyPartSlot.LeftForearm] = new(22, 35, 39, 45),
        [BodyPartSlot.LeftHand] = new(7, 32, 22, 47),
        [BodyPartSlot.RightUpperArm] = new(70, 32, 88, 45),
        [BodyPartSlot.RightForearm] = new(88, 35, 105, 45),
        [BodyPartSlot.RightHand] = new(105, 32, 120, 47),
        [BodyPartSlot.LeftThigh] = new(48, 64, 63, 86),
        [BodyPartSlot.LeftShin] = new(49, 86, 61, 102),
        [BodyPartSlot.LeftFoot] = new(47, 102, 60, 116),
        [BodyPartSlot.RightThigh] = new(64, 64, 79, 86),
        [BodyPartSlot.RightShin] = new(66, 86, 78, 102),
        [BodyPartSlot.RightFoot] = new(67, 102, 80, 116),
    };

    public AdvancedHealthBodyDollControl()
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        const string dir = "/Textures/_Eclipse/Interface/AdvancedHealth";
        _base = cache.GetResource<TextureResource>($"{dir}/body_base.png").Texture;
        _bloodLight = cache.GetResource<TextureResource>($"{dir}/blood_s.png").Texture;
        _bloodMedium = cache.GetResource<TextureResource>($"{dir}/blood.png").Texture;
        _bloodHeavy = cache.GetResource<TextureResource>($"{dir}/blood_b.png").Texture;

        foreach (var slot in Regions.Keys)
        {
            var path = new ResPath($"{dir}/parts/{slot.ToString().ToLowerInvariant()}.png");
            if (cache.TryGetResource(path, out TextureResource? maskRes))
                _masks[slot] = maskRes.Texture;
        }

        SetSize = new Vector2(128, 128);
        MinSize = Vector2.Zero;
        MouseFilter = MouseFilterMode.Stop;
    }

    public void EnableAimSelection()
    {
        AimMode = true;
        _states.Clear();
        _enabled.Clear();
        foreach (var slot in AimSlots)
            _enabled.Add(slot);
    }

    /// <summary>Centroid of a body zone in this control's local pixel space.</summary>
    public Vector2 GetSlotCentroidLocal(BodyPartSlot slot)
    {
        var scale = AimMode ? SetSize.X / 128f : Size.X / 128f;
        if (!Centroids.TryGetValue(slot, out var centroid))
            return (AimMode ? SetSize : Size) * 0.5f;
        return centroid * scale;
    }

    public void SetState(IEnumerable<BodyPartUiState>? states)
    {
        AimMode = false;
        _states.Clear();
        _failedFunctions.Clear();
        _enabled.Clear();
        if (states != null)
        {
            foreach (var state in states)
            {
                _states[state.Slot] = state;
                _enabled.Add(state.Slot);
            }
        }
        if (GroupPelvisWithAbdomen) MergeRegion(BodyPartSlot.Abdomen, BodyPartSlot.Pelvis);
        if (GroupNeckWithHead) MergeRegion(BodyPartSlot.Head, BodyPartSlot.Neck);
    }

    private void MergeRegion(BodyPartSlot parent, BodyPartSlot child)
    {
        if (!_states.Remove(child, out var pelvis))
            return;
        _enabled.Remove(child);
        _enabled.Add(parent);
        if (!_states.TryGetValue(parent, out var abdomen))
            abdomen = pelvis with { Slot = parent };
        _states[parent] = abdomen with
        {
            Severity = (WoundSeverity) Math.Max((int) abdomen.Severity, (int) pelvis.Severity),
            Damaged = abdomen.Damaged || pelvis.Damaged,
            Bleeding = (BleedingLevel) Math.Max((int) abdomen.Bleeding, (int) pelvis.Bleeding),
            Fractured = abdomen.Fractured || pelvis.Fractured,
            Bandaged = abdomen.Bandaged || pelvis.Bandaged,
            Splinted = abdomen.Splinted || pelvis.Splinted,
            Tourniquet = abdomen.Tourniquet || pelvis.Tourniquet,
            Destroyed = abdomen.Destroyed || pelvis.Destroyed,
            ForeignBody = abdomen.ForeignBody || pelvis.ForeignBody,
        };
    }

    public void SetFunction(BodyPartSlot slot, float function)
    {
        if (function <= 0.01f)
            _failedFunctions.Add(slot);
        else
            _failedFunctions.Remove(slot);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        if (TryHitTest(args.RelativePosition) is { } slot)
        {
            SelectedSlot = slot;
            // In hover-select mode a click acts on the part (treatment); otherwise it selects.
            if (HoverSelect)
                OnPartActivated?.Invoke(slot);
            else
                OnPartSelected?.Invoke(slot);
            args.Handle();
        }
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        var hit = TryHitTest(args.RelativePosition);
        if (HoveredSlot == hit)
            return;

        HoveredSlot = hit;

        if (hit is { } slot)
        {
            OnPartHovered?.Invoke(slot);
            if (HoverSelect)
            {
                SelectedSlot = slot;
                OnPartSelected?.Invoke(slot);
            }
        }
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        if (HoveredSlot == null)
            return;

        HoveredSlot = null;
    }

    private Vector2 LayoutSize => AimMode && SetSize != Vector2.Zero ? SetSize : Size;

    private BodyPartSlot? TryHitTest(Vector2 pos)
    {
        var layout = LayoutSize;
        if (layout.X <= 0 || layout.Y <= 0)
            return null;

        var scaleX = layout.X / 128f;
        var scaleY = layout.Y / 128f;

        BodyPartSlot? best = null;
        var bestArea = float.MaxValue;
        foreach (var (slot, region) in Regions)
        {
            if (_enabled.Count > 0 && !_enabled.Contains(slot)
                && !_enabled.Contains(DisplaySlot(slot)))
                continue;

            var rect = ScaleRegion(region, scaleX, scaleY);
            if (!rect.Contains(pos))
                continue;

            var area = rect.Width * rect.Height;
            if (area < bestArea)
            {
                bestArea = area;
                best = slot;
            }
        }

        return best is { } hit ? DisplaySlot(hit) : null;
    }

    private static UIBox2 ScaleRegion(UIBox2 region, float scaleX, float scaleY)
        => new(region.Left * scaleX, region.Top * scaleY, region.Right * scaleX, region.Bottom * scaleY);

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _effectTime += args.DeltaSeconds;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        handle.DrawTextureRect(_base, box);

        var scale = LayoutSize.X / 128f;

        if (!AimMode)
        {
            foreach (var (slot, state) in _states)
            {
                if (!_masks.TryGetValue(slot, out var mask))
                {
                    if (Regions.TryGetValue(slot, out var region))
                        handle.DrawRect(ScaleRegion(region, scale, scale).Translated(box.TopLeft), GetColor(state));
                    continue;
                }

                var color = GetColor(state);
                if (color.A <= 0f)
                    continue;

                // Severe/critical parts tremble (small, pixel-aligned) and flicker like static.
                if (EffectsEnabled && state.Damaged && !state.Destroyed &&
                    (state.Severity == WoundSeverity.Severe || state.Severity == WoundSeverity.Critical))
                {
                    var seed = (int) slot * 1.7f;
                    var amp = state.Severity == WoundSeverity.Critical ? 1.4f : 0.7f;
                    // Round to whole pixels so the fill stays crisp instead of smearing.
                    var jx = MathF.Round(MathF.Sin(_effectTime * 27f + seed) * amp);
                    var jy = MathF.Round(MathF.Cos(_effectTime * 23f + seed * 1.3f) * amp);
                    var flicker = 0.78f + 0.22f * MathF.Sin(_effectTime * 20f + seed * 2f);
                    var fbox = new UIBox2(box.Left, box.Top, box.Right, box.Bottom);
                    handle.DrawTextureRect(mask, fbox.Translated(new Vector2(jx, jy)),
                        color.WithAlpha(color.A * flicker));
                }
                else
                {
                    handle.DrawTextureRect(mask, box, color);
                }
            }

            foreach (var (slot, state) in _states)
            {
                if (state.Bleeding == BleedingLevel.None || !Centroids.TryGetValue(slot, out var centroid))
                    continue;

                var (icon, sizeMul) = state.Bleeding switch
                {
                    BleedingLevel.Light => (_bloodLight, 0.9f),
                    BleedingLevel.Moderate => (_bloodMedium, 1.15f),
                    BleedingLevel.Heavy => (_bloodHeavy, 1.45f),
                    _ => ((Texture?) null, 0f),
                };

                if (icon == null)
                    continue;

                // Pulse each bleeding marker so its intensity reads at a glance per limb: heavier
                // bleeding throbs faster and larger, like the Casualties: Unknown bleed indicators.
                var level = (int) state.Bleeding;
                var phase = _effectTime * (2.4f + level * 1.3f) + (int) slot;
                var pulse = EffectsEnabled ? 1f + 0.18f * MathF.Sin(phase) : 1f;
                var alpha = EffectsEnabled ? 0.7f + 0.3f * (0.5f + 0.5f * MathF.Sin(phase)) : 1f;

                var iconSize = Math.Clamp(30f * scale * sizeMul * pulse, 20f, 84f);
                var half = iconSize * 0.5f;
                var cx = centroid.X * scale;
                var cy = centroid.Y * scale;
                handle.DrawTextureRect(icon, new UIBox2(cx - half, cy - half, cx + half, cy + half),
                    Color.White.WithAlpha(alpha));
            }

            // Foreign-body placeholder markers (embedded shrapnel/glass/bullets) — a pulsing steel dot.
            foreach (var (slot, state) in _states)
            {
                if (!state.ForeignBody || !Centroids.TryGetValue(slot, out var centroid))
                    continue;

                var pulse = EffectsEnabled ? 0.6f + 0.4f * (0.5f + 0.5f * MathF.Sin(_effectTime * 4.5f + (int) slot)) : 1f;
                var radius = Math.Clamp(5f * scale, 3.5f, 12f);
                var cx = centroid.X * scale + 9f * scale;
                var cy = centroid.Y * scale + 9f * scale;
                var pos = new Vector2(cx, cy);
                handle.DrawCircle(pos, radius, Color.FromHex("#c8ccd0").WithAlpha(pulse));
                handle.DrawCircle(pos, radius, Color.FromHex("#2a2d31"), filled: false);
            }
        }

        if (!AimMode)
        {
            foreach (var slot in _failedFunctions)
            {
                if (DisplaySlot(slot) != slot && _failedFunctions.Contains(DisplaySlot(slot)))
                    continue;
                var visualSlot = DisplaySlot(slot);
                if (!Centroids.TryGetValue(visualSlot, out var point))
                    continue;
                var p = point * scale;
                var radius = Math.Max(5f, 5f * scale);
                handle.DrawCircle(p, radius, Color.FromHex("#e8b65b"), filled: false);
                handle.DrawLine(p - new Vector2(radius, 0), p + new Vector2(radius, 0), Color.FromHex("#e8b65b"));
            }
            foreach (var (slot, state) in _states)
            {
                if (!Centroids.TryGetValue(slot, out var point))
                    continue;
                var p = point * scale + new Vector2(-10f * scale, 9f * scale);
                var size = Math.Max(3f, 3f * scale);
                if (state.Fractured)
                {
                    handle.DrawLine(p - new Vector2(size), p + new Vector2(size), Color.Orange);
                    handle.DrawLine(p + new Vector2(-size, size), p + new Vector2(size, -size), Color.Orange);
                }
                if (state.Bandaged || state.Splinted || state.Tourniquet)
                    handle.DrawRect(new UIBox2(p.X - size, p.Y + size + 2, p.X + size, p.Y + size + 5),
                        Color.FromHex("#6fd0e0"));
            }
        }

        var highlight = AimMode ? HoveredSlot : SelectedSlot ?? HoveredSlot;
        if (highlight is { } sel)
            DrawHighlight(handle, box, scale, sel);
    }

    private void DrawHighlight(DrawingHandleScreen handle, UIBox2 box, float scale, BodyPartSlot slot)
    {
        var color = Color.FromHex("#4be06e99");
        if (_masks.TryGetValue(slot, out var mask))
        {
            handle.DrawTextureRect(mask, box, color);
            return;
        }

        if (Regions.TryGetValue(slot, out var region))
        {
            var rect = ScaleRegion(region, scale, scale);
            handle.DrawRect(rect.Translated(box.TopLeft), color, filled: true);
        }
    }

    private static Color GetColor(BodyPartUiState state)
    {
        if (state.Destroyed) return Color.FromHex("#481018ee");
        if (state.Severity == WoundSeverity.Critical) return Color.FromHex("#8a0f18ee");
        if (state.Severity == WoundSeverity.Severe) return Color.FromHex("#d81b2ade");
        if (state.Severity == WoundSeverity.Moderate) return Color.FromHex("#ef6a6ac6");
        if (state.Severity == WoundSeverity.Minor && state.Damaged) return Color.FromHex("#e0a3a3a0");
        return Color.Transparent;
    }
}
