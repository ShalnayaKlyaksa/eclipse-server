using System;
using System.Linq;
using System.Numerics;
using Content.Client.MainMenu.UI;
using Content.Client.Message;
using Content.Shared._Eclipse.Tutorial;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Eclipse.Tutorial;

/// <summary>
/// Lobby window listing the available tutorial lessons. The whole lesson card is clickable to start it.
/// </summary>
public sealed class TutorialWindow : DefaultWindow
{
    private readonly Action<string> _onStart;

    public TutorialWindow(IPrototypeManager prototypes, Action<string> onStart)
    {
        _onStart = onStart;

        Title = "Обучение";
        MinSize = SetSize = new Vector2(600, 580);

        var scroll = new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true,
            HScrollEnabled = false, // let descriptions wrap instead of overflowing to the right.
            Margin = new Thickness(16),
        };

        var list = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 10,
        };

        var header = new RichTextLabel { HorizontalExpand = true };
        header.SetMarkup("[color=#E6A11A][bold]ВЫБЕРИТЕ УРОК[/bold][/color]");
        list.AddChild(header);

        var subtitle = new RichTextLabel { HorizontalExpand = true };
        subtitle.SetMarkup("[color=#A6A6A6]Пройдите обучение, чтобы освоить основные механики станции.[/color]");
        list.AddChild(subtitle);

        list.AddChild(new Control { MinSize = new Vector2(0, 4) });

        var lessons = prototypes.EnumeratePrototypes<TutorialLessonPrototype>()
            .OrderBy(l => l.Order)
            .ThenBy(l => l.Name);

        foreach (var lesson in lessons)
            list.AddChild(MakeLessonCard(lesson));

        scroll.AddChild(list);
        ContentsContainer.AddChild(scroll);
    }

    private Control MakeLessonCard(TutorialLessonPrototype lesson)
    {
        var card = new Button
        {
            StyleIdentifier = MainMenuControl.StyleIdentifierNav,
            HorizontalExpand = true,
            Disabled = !lesson.Enabled,
        };

        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 14,
            Margin = new Thickness(14, 12, 14, 12),
        };

        row.AddChild(new TextureRect
        {
            TexturePath = lesson.Icon,
            SetSize = new Vector2(40, 40),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            VerticalAlignment = VAlignment.Center,
            ModulateSelfOverride = lesson.Enabled ? Color.FromHex("#E6A11A") : Color.FromHex("#7A7A7A"),
        });

        var text = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            SeparationOverride = 4,
        };

        var name = new RichTextLabel { HorizontalExpand = true };
        var nameColor = lesson.Enabled ? "#E6A11A" : "#8A8A8A";
        name.SetMarkup($"[color={nameColor}][bold]{FormattedMessage.EscapeText(lesson.Name)}[/bold][/color]");
        text.AddChild(name);

        var description = new RichTextLabel { HorizontalExpand = true };
        description.SetMarkup($"[color=#A6A6A6]{FormattedMessage.EscapeText(lesson.Description)}[/color]");
        text.AddChild(description);

        if (!lesson.Enabled)
        {
            var soon = new RichTextLabel { HorizontalExpand = true };
            soon.SetMarkup("[color=#7A7A7A]Скоро[/color]");
            text.AddChild(soon);
        }

        row.AddChild(text);
        card.AddChild(row);

        if (lesson.Enabled)
        {
            var id = lesson.ID;
            card.OnPressed += _ =>
            {
                _onStart(id);
                Close();
            };
        }

        return card;
    }
}
