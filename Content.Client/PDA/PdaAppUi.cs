using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.PDA;

/// <summary>Shared spacing and surfaces for the handheld's applications.</summary>
public static class PdaAppUi
{
    public static BoxContainer Header(string icon, string title, string subtitle) => new()
    {
        SeparationOverride = 6,
        Children =
        {
            new TextureRect { TexturePath = "/Textures/_Eclipse/Interface/PDA/Icons/Field/" + icon + ".png", SetSize = new Vector2(24), CanShrink = true, Stretch = TextureRect.StretchMode.KeepAspectCentered },
            new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 2, Children =
            {
                new Label { Text = title, StyleClasses = { "pda-title" } },
                new Label { Text = subtitle, ClipText = true, StyleClasses = { "terminal-muted" } },
            } },
        },
    };

    public static PanelContainer Card(Control content, string fill = "#192930", string border = "#354951", int padding = 4) => new()
    {
        PanelOverride = PdaTheme.Box(fill, border, padding, 5),
        Children = { content },
    };

    public static BoxContainer Field(string label, Control input) => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 5,
        HorizontalExpand = true,
        Children = { new Label { Text = label, StyleClasses = { "terminal-muted" } }, input },
    };

    public static Button Action(string text, bool primary = false)
    {
        var button = new Button { Text = text, MinHeight = 26 };
        if (primary) button.StyleClasses.Add("pda-primary");
        return button;
    }

    public static Control Empty(string title, string hint)
    {
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 9, VerticalAlignment = Control.VAlignment.Center, HorizontalExpand = true, Margin = new Thickness(6) };
        column.AddChild(new Label { Text = title, StyleClasses = { "pda-caption" } });
        var text = new RichTextLabel { HorizontalExpand = true }; text.SetMessage(hint); column.AddChild(text);
        return column;
    }
}

