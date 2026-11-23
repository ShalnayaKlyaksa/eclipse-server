using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using System.Linq;
using System.Numerics;
using Content.Client.Resources;
using Content.Client.Stylesheets;
using Robust.Client.ResourceManagement;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._Eclipse.Industrial;

public sealed partial class IndustrialProcessorWindow
{
    private static readonly Color Amber = ForgeStyle.Accent;
    private static readonly Color Muted = ForgeStyle.Muted;
    private static readonly Color Ink = ForgeStyle.Text;
    private static readonly Color Background = ForgeStyle.Background;
    private static readonly Color Surface = ForgeStyle.Surface;
    private static readonly Color Border = ForgeStyle.Border;
    private static readonly Color Ready = Color.FromHex("#A3BEA0");

    private static Stylesheet CreateStyles(Stylesheet? stationStyle, IResourceCache resources)
    {
        var body = resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 12);
        var small = resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 10);
        var recipeName = resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 11);
        var caption = resources.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 11);
        var heading = resources.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 14);
        StyleRule[] rules =
        [
            E<Label>().Font(body),
            E<RichTextLabel>().Font(body),
            E<LineEdit>().Font(body),
            E<Label>().Class(StyleClass.FontSmall).Font(small),
            E<RichTextLabel>().Class(StyleClass.FontSmall).Font(small),
            E<RichTextLabel>().Class("industrial-recipe-name").Font(recipeName),
            E<Label>().Class("industrial-caption").Font(caption),
            E<Label>().Class("industrial-heading").Font(heading),
            E<Label>().Class("industrial-number").Font(caption),
            E<ContainerButton>().Class("industrial-control")
                .Prop(ContainerButton.StylePropertyStyleBox, ButtonBox("#202020", Border))
                .Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("industrial-control").PseudoNormal().Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("industrial-control").PseudoHovered()
                .Prop(ContainerButton.StylePropertyStyleBox, ButtonBox("#303030", Muted))
                .Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("industrial-control").PseudoPressed()
                .Prop(ContainerButton.StylePropertyStyleBox, ButtonBox("#33251B", Amber))
                .Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("industrial-recipe")
                .Prop(ContainerButton.StylePropertyStyleBox, RecipeBox(false, false))
                .Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("industrial-recipe").PseudoHovered()
                .Prop(ContainerButton.StylePropertyStyleBox, RecipeBox(false, true)),
            E<ContainerButton>().Class("industrial-recipe").PseudoPressed()
                .Prop(ContainerButton.StylePropertyStyleBox, RecipeBox(true, false)),
            E<Label>().Class(OptionButton.StyleClassOptionButton).Prop(Label.StylePropertyAlignMode, Label.AlignMode.Left),
            E<PanelContainer>().Class(OptionButton.StyleClassOptionsBackground).Prop(PanelContainer.StylePropertyPanel, new StyleBoxFlat(Surface)),
            E<LineEdit>().Class("industrial-search")
                .Prop(LineEdit.StylePropertyStyleBox, ButtonBox("#141414", Border)),
            E<VScrollBar>().Prop(ScrollBar.StylePropertyGrabber, ScrollThumb(Border)),
            E<VScrollBar>().PseudoHovered().Prop(ScrollBar.StylePropertyGrabber, ScrollThumb(Muted)),
            E<VScrollBar>().PseudoPressed().Prop(ScrollBar.StylePropertyGrabber, ScrollThumb(Amber)),
        ];
        // A local stylesheet replaces the station stylesheet; retain its fonts and text colours.
        return new Stylesheet((stationStyle?.Rules ?? Array.Empty<StyleRule>()).Concat(rules).ToArray());
    }

    private static StyleBoxFlat ScrollThumb(Color color) => new(color)
    {
        ContentMarginLeftOverride = 6, ContentMarginTopOverride = 12,
    };

    private static StyleBoxFlat ButtonBox(string background, Color border) => new()
    {
        BackgroundColor = Color.FromHex(background), BorderColor = border, BorderThickness = new Thickness(1),
        ContentMarginLeftOverride = 8, ContentMarginRightOverride = 8,
        ContentMarginTopOverride = 5, ContentMarginBottomOverride = 5,
    };

    private static StyleBoxFlat RecipeBox(bool selected, bool hovered) => new()
    {
        BackgroundColor = selected ? Color.FromHex("#33251B") : hovered ? Color.FromHex("#303030") : Background,
        BorderColor = selected ? Amber.WithAlpha(0.8f) : Border.WithAlpha(0.3f),
        BorderThickness = new Thickness(selected ? 2 : 0, 0, 0, 1),
        ContentMarginLeftOverride = 8, ContentMarginRightOverride = 6,
        ContentMarginTopOverride = 5, ContentMarginBottomOverride = 5,
    };

    private sealed class ProcessBoard : PanelContainer
    {
        public bool Running;
        private Vector2 _offset;
        // One direction per window instance. The accumulated offset freezes while the machine is stopped.
        private readonly Vector2 _velocity;

        public ProcessBoard(Vector2 velocity)
        {
            _velocity = velocity;
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#202020"), BorderColor = Border, BorderThickness = new Thickness(1),
            };
        }

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);
            if (Running)
            {
                _offset += _velocity * MathF.Min(args.DeltaSeconds, .1f);
                _offset = new Vector2((_offset.X % 20 + 20) % 20, (_offset.Y % 20 + 20) % 20);
            }
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            var grid = Color.FromHex("#CBD8E409");
            var step = 20 * UIScale;
            for (var x = _offset.X * UIScale; x < PixelWidth - 1; x += step)
            {
                if (x < 1) continue;
                handle.DrawLine(new Vector2(x, 1), new Vector2(x, PixelHeight - 1), grid);
            }
            for (var y = _offset.Y * UIScale; y < PixelHeight - 1; y += step)
            {
                if (y < 1) continue;
                handle.DrawLine(new Vector2(1, y), new Vector2(PixelWidth - 1, y), grid);
            }
        }
    }

    private sealed class SlotButton : ContainerButton
    {
        private static readonly StyleBoxFlat Normal = new()
        {
            BackgroundColor = Color.FromHex("#202020"), BorderColor = Color.FromHex("#505050"), BorderThickness = new Thickness(1),
        };
        private static readonly StyleBoxFlat Hover = new()
        {
            BackgroundColor = Color.FromHex("#303030"), BorderColor = Amber, BorderThickness = new Thickness(1),
        };
        public bool Empty;
        public bool IsInput;

        public SlotButton()
        {
            MinSize = MaxSize = new Vector2(42, 42);
            ModulateSelfOverride = Color.White;
        }

        protected override void DrawModeChanged()
        {
            base.DrawModeChanged();
            StyleBoxOverride = DrawMode is DrawModeEnum.Hover or DrawModeEnum.Pressed ? Hover : Normal;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            if (!Empty)
                return;
            var center = PixelSize / 2f;
            var radius = 4 * UIScale;
            handle.DrawLine(center - new Vector2(radius, 0), center + new Vector2(radius, 0), Muted);
            if (IsInput)
                handle.DrawLine(center - new Vector2(0, radius), center + new Vector2(0, radius), Muted);
        }
    }
}
