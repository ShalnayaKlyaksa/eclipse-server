using System.Numerics;
using Content.Client.MainMenu.UI;
using Content.Shared.CrewManifest;
using Content.Shared.Roles;
using Content.Shared.StatusIcon;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.CrewManifest.UI;

public sealed class CrewManifestSection : BoxContainer
{
    public CrewManifestSection(
        IPrototypeManager prototypeManager,
        SpriteSystem spriteSystem,
        DepartmentPrototype section,
        List<CrewManifestEntry> entries)
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        Margin = new Thickness(0, 0, 0, 12);

        AddChild(new PanelContainer
        {
            Margin = new Thickness(0, 0, 0, 6),
            PanelOverride = EclipsePanel("#251600D8", "#A85E1270", 5f, 8f, 4f),
            Children =
            {
                new Label
                {
                    StyleIdentifier = MainMenuControl.StyleIdentifierHeaderGold,
                    Text = Loc.GetString(section.Name),
                }
            }
        });

        var gridContainer = new GridContainer
        {
            HorizontalExpand = true,
            Columns = 2
        };

        AddChild(gridContainer);

        foreach (var entry in entries)
        {
            var name = new RichTextLabel
            {
                HorizontalExpand = true,
                Margin = new Thickness(6, 2, 8, 2),
            };
            name.SetMessage(entry.Name);

            var titleContainer = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = true
            };

            var title = new RichTextLabel
            {
                Margin = new Thickness(0, 2, 6, 2),
            };
            title.SetMessage(entry.JobTitle);

            if (prototypeManager.TryIndex<JobIconPrototype>(entry.JobIcon, out var jobIcon))
            {
                var icon = new TextureRect
                {
                    TextureScale = new Vector2(2, 2),
                    VerticalAlignment = VAlignment.Center,
                    Texture = spriteSystem.Frame0(jobIcon.Icon),
                    Margin = new Thickness(0, 0, 4, 0)
                };

                titleContainer.AddChild(icon);
                titleContainer.AddChild(title);
            }
            else
            {
                titleContainer.AddChild(title);
            }

            gridContainer.AddChild(name);
            gridContainer.AddChild(titleContainer);
        }
    }

    private static EclipseStyleBoxRounded EclipsePanel(
        string background,
        string border,
        float radius,
        float horizontalPadding,
        float verticalPadding)
    {
        var style = new EclipseStyleBoxRounded
        {
            BackgroundColor = Color.FromHex(background),
            BorderColor = Color.FromHex(border),
            BorderThickness = new Thickness(1),
            Radius = radius,
        };

        style.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontalPadding);
        style.SetContentMarginOverride(StyleBox.Margin.Vertical, verticalPadding);
        return style;
    }
}
