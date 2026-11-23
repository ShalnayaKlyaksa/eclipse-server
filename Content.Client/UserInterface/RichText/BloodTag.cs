using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.RichText;

/// <summary>A short inline caption with animated blood drips, used by Zero Shift announcements.</summary>
[UsedImplicitly]
public sealed class BloodTag : IMarkupTagHandler
{
    public string Name => "blood";

    public bool TryCreateControl(MarkupNode node, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        if (node.Closing || !node.Value.TryGetString(out var text) ||
            string.IsNullOrWhiteSpace(text) || text.Length > 32 || text.Contains('\n') || text.Contains('\r'))
            return false;

        control = new BloodTextLabel(text);
        return true;
    }

    private sealed class BloodTextLabel : Label
    {
        private const float DripDepth = 10f;
        private static readonly Color BloodColor = Color.FromHex("#B72B38");
        private static readonly Color ShadowColor = Color.FromHex("#35090F");
        private float _elapsed;

        public BloodTextLabel(string text)
        {
            Text = text;
            FontColorOverride = BloodColor;
            FontColorShadowOverride = ShadowColor;
            ShadowOffsetXOverride = 1;
            ShadowOffsetYOverride = 1;
            VAlign = VAlignMode.Top;
            MouseFilter = MouseFilterMode.Ignore;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var size = base.MeasureOverride(availableSize);
            return size + new Vector2(0, DripDepth);
        }

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);
            _elapsed = (_elapsed + args.DeltaSeconds) % 16f;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            var bottom = PixelSize.Y - DripDepth * UIScale - 3 * UIScale;
            for (var i = 0; i < 5; i++)
            {
                var phase = (_elapsed * 0.25f + i * 0.19f) % 1f;
                var x = PixelSize.X * (0.1f + i * 0.18f);
                var length = (1 + 6 * MathF.Min(phase / 0.65f, 1)) * UIScale;
                var color = BloodColor.WithAlpha(1f - MathF.Max(0, (phase - 0.8f) / 0.2f));
                handle.DrawRect(new UIBox2(x, bottom, x + UIScale, bottom + length), ShadowColor);
                var fall = MathF.Max(0, (phase - 0.65f) / 0.35f) * 4 * UIScale;
                handle.DrawCircle(new Vector2(x + UIScale * 0.5f, bottom + length + fall), UIScale * 1.1f, color);
            }
        }
    }
}
