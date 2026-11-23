using Content.Client.Stylesheets;
using System.Linq;
using System.Numerics;
using Content.Client.Resources;
using Content.Client.UserInterface.Controls;
using Content.Shared._Eclipse.Beekeeping;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._Eclipse.UserInterface;

/// <summary>Common visual language for the kitchen and apiary's bounded workstation windows.</summary>
public static class WorkshopUi
{
    public static readonly Color Background = ForgeStyle.Background;
    public static readonly Color Surface = ForgeStyle.Surface;
    public static readonly Color Border = ForgeStyle.Border;
    public static readonly Color Ink = ForgeStyle.Text;
    public static readonly Color Muted = ForgeStyle.Muted;
    public static readonly Color Amber = ForgeStyle.Accent;
    public static readonly Color Green = Color.FromHex("#80C8AE");

    public static BoxContainer Setup(FancyWindow window, Vector2 size, Color accent)
    {
        accent = ForgeStyle.Accent;
        window.MinSize = size - new Vector2(60, 70);
        window.MaxSize = size + new Vector2(100, 100);
        window.SetSize = size;
        window.FindControl<PanelContainer>("WindowHeader").PanelOverride = new StyleBoxFlat(Background);
        window.FindControl<Label>("WindowTitle").FontColorOverride = accent;
        window.ContentsContainer.AddChild(new PanelContainer { PanelOverride = new StyleBoxFlat(Background) });
        var root = Column();
        root.Name = "WorkshopRoot";
        root.Margin = new Thickness(12);
        root.MaxWidth = size.X + 72;
        root.VerticalExpand = true;
        var ui = IoCManager.Resolve<IUserInterfaceManager>();
        var resources = IoCManager.Resolve<IResourceCache>();
        window.FindControl<Label>("WindowTitle").FontOverride = resources.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 14);
        var font = resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 12);
        StyleRule[] rules =
        [
            E<Label>().Font(font), E<RichTextLabel>().Font(font), E<LineEdit>().Font(font),
            E<Label>().Prop(Label.StylePropertyFontColor, Ink),
            E<TabContainer>().Font(font)
                .Prop(TabContainer.StylePropertyPanelStyleBox, Box(Background, Border, 0))
                .Prop(TabContainer.StylePropertyTabStyleBox, Box(Surface, accent, 6))
                .Prop(TabContainer.StylePropertyTabStyleBoxInactive, Box(Background, Border, 6)),
            E<ScrollBar>().Prop(ScrollBar.StylePropertyGrabber, new StyleBoxFlat(Border)),
            E<ContainerButton>().Class("workshop-button").Prop(ContainerButton.StylePropertyStyleBox, Box(Surface, Border, 4))
                .Prop(Control.StylePropertyModulateSelf, Color.White),
            E<ContainerButton>().Class("workshop-button").PseudoHovered().Prop(ContainerButton.StylePropertyStyleBox, Box("#303030", accent, 4)),
            E<ContainerButton>().Class("workshop-button").PseudoPressed().Prop(ContainerButton.StylePropertyStyleBox, Box("#33251B", accent, 4)),
            E<ContainerButton>().Class("workshop-primary").Prop(ContainerButton.StylePropertyStyleBox, Box("#33251B", accent, 4)),
            E<ContainerButton>().Class("workshop-slot").Prop(ContainerButton.StylePropertyStyleBox, Box(Surface, Border, 3)),
            E<LineEdit>().Prop(LineEdit.StylePropertyStyleBox, Box("#141414", Border, 6)),
        ];
        root.Stylesheet = new Stylesheet((ui.Stylesheet?.Rules ?? []).Concat(rules).ToArray());
        window.ContentsContainer.AddChild(root);
        return root;
    }

    public static StyleBoxFlat Box(Color color, Color border, float padding = 10) => new(color)
    {
        BorderColor = border, BorderThickness = new Thickness(1),
        ContentMarginLeftOverride = padding, ContentMarginRightOverride = padding,
        ContentMarginTopOverride = padding, ContentMarginBottomOverride = padding,
    };
    public static StyleBoxFlat Box(string color, Color border, float padding = 10) => Box(Color.FromHex(color), border, padding);
    public static BoxContainer Column() => new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, HorizontalExpand = true };
    public static BoxContainer Row() => new() { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 10, HorizontalExpand = true };
    public static Label Text(string text = "", bool muted = false) => new()
    {
        Text = text, ClipText = true, HorizontalExpand = true, FontColorOverride = muted ? Muted : Ink,
    };
    public static RichTextLabel Paragraph(string text, float width = 320)
    {
        var label = new RichTextLabel { HorizontalExpand = true, MaxWidth = width };
        label.SetMessage(FormattedMessage.FromUnformatted(text), Muted);
        return label;
    }
    public static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Text = text, ClipText = true, MinHeight = 34, HorizontalExpand = true, ModulateSelfOverride = Color.White };
        button.AddStyleClass("workshop-button");
        if (primary) button.AddStyleClass("workshop-primary");
        button.OnPressed += _ => action();
        return button;
    }
    public static PanelContainer Card(string title, out BoxContainer contents)
    {
        var panel = new PanelContainer { PanelOverride = Box(Surface, Border), HorizontalExpand = true };
        contents = Column(); contents.AddChild(Text(title, true)); panel.AddChild(contents);
        return panel;
    }
    public static ProgressBar Progress(Color color) => new()
    {
        MinValue = 0, MaxValue = 1, MinHeight = 8,
        BackgroundStyleBoxOverride = new StyleBoxFlat(Color.FromHex("#141414")),
        ForegroundStyleBoxOverride = new StyleBoxFlat(color),
    };
    public static TextureRect Icon(string? prototype, int size = 32)
    {
        var icon = new TextureRect { MinSize = new Vector2(size), MaxSize = new Vector2(size), Stretch = TextureRect.StretchMode.KeepAspectCentered };
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        if (prototype != null && prototypes.TryIndex<EntityPrototype>(prototype, out var entity))
        {
            var manager = IoCManager.Resolve<IEntityManager>();
            icon.Texture = manager.System<SpriteSystem>().Frame0(entity);
            // The world bee occupies only a few pixels of its 32px animation cell.
            // Frame it closely so specimens remain identifiable in inventory-sized slots.
            if (entity.TryGetComponent<BeeSpecimenComponent>(out _, manager.ComponentFactory))
            {
                var texture = icon.Texture;
                var region = new UIBox2(12, 12, 20, 20);
                while (texture is AtlasTexture atlas)
                {
                    region = new UIBox2(region.TopLeft + atlas.SubRegion.TopLeft, region.BottomRight + atlas.SubRegion.TopLeft);
                    texture = atlas.SourceTexture;
                }
                icon.Texture = new AtlasTexture(texture, region);
            }
            if (entity.TryGetComponent<SpriteComponent>(out var sprite, manager.ComponentFactory)) icon.Modulate = sprite.Color;
        }
        return icon;
    }
    public static ContainerButton Item(string? prototype, string tooltip, Action action, int count = 1)
    {
        var button = new ContainerButton
        {
            MinSize = new Vector2(46), MaxSize = new Vector2(46), ToolTip = tooltip,
            ModulateSelfOverride = Color.White, StyleClasses = { "workshop-button", "workshop-slot" },
        };
        var content = new Control { Margin = new Thickness(3), MouseFilter = Control.MouseFilterMode.Ignore };
        var icon = Icon(prototype);
        icon.HorizontalAlignment = Control.HAlignment.Center; icon.VerticalAlignment = Control.VAlignment.Center;
        icon.MouseFilter = Control.MouseFilterMode.Ignore;
        content.AddChild(icon);
        if (prototype == null) content.AddChild(new Label
        {
            Text = "+", Align = Label.AlignMode.Center, VerticalAlignment = Control.VAlignment.Center,
            FontColorOverride = Muted, MouseFilter = Control.MouseFilterMode.Ignore,
        });
        if (count > 1) content.AddChild(new Label
        {
            Text = $"×{count}", HorizontalAlignment = Control.HAlignment.Right,
            VerticalAlignment = Control.VAlignment.Bottom, MouseFilter = Control.MouseFilterMode.Ignore,
        });
        button.AddChild(content); button.OnPressed += _ => action();
        return button;
    }
}

public sealed class WorkshopGauge : BoxContainer
{
    private readonly Label _value = WorkshopUi.Text();
    private readonly ProgressBar _bar;
    public WorkshopGauge(string title, Color color)
    {
        Orientation = LayoutOrientation.Vertical; SeparationOverride = 4; HorizontalExpand = true;
        var row = WorkshopUi.Row(); row.AddChild(WorkshopUi.Text(title, true)); row.AddChild(_value); AddChild(row);
        _value.Align = Label.AlignMode.Right;
        _bar = WorkshopUi.Progress(color); AddChild(_bar);
    }
    public void Apply(float value, float maximum)
    {
        _value.Text = $"{MathF.Round(value, 1)} / {maximum:0}";
        _bar.Value = maximum > 0 ? Math.Clamp(value / maximum, 0, 1) : 0;
    }
}
