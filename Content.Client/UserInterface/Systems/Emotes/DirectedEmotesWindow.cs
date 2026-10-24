using System.Numerics;
using Content.Client.MainMenu.UI;
using Content.Client.Stylesheets;
using Content.Shared._EinsteinEngines.DirectedEmotes;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Emotes;

public sealed class DirectedEmotesWindow : DefaultWindow
{
    private static readonly Color EclipseSurface = Color.FromHex("#080100F2");
    private static readonly Color EclipseInput = Color.FromHex("#070100F4");
    private static readonly Color EclipseButton = Color.FromHex("#090200F4");
    private static readonly Color EclipseBorder = Color.FromHex("#8A2F12AA");
    private static readonly Color EclipseAccent = Color.FromHex("#A84B00CC");
    private static readonly Color EclipseAccentHover = Color.FromHex("#D47D1BAA");
    private static readonly Color EclipseText = Color.FromHex("#FFF1D6");
    private static readonly Color EclipseTextMuted = Color.FromHex("#BFA88A");

    private readonly BoxContainer _participants;
    private readonly OutputPanel _messages;
    private readonly LineEdit _input;
    private readonly Button _sendButton;
    private readonly Button _addParticipantButton;
    private readonly Dictionary<DirectedEmotesMessageType, Button> _typeButtons = new();
    private DirectedEmotesMessageType _messageType = DirectedEmotesMessageType.Voice;

    public event Action<string, DirectedEmotesMessageType>? OnSend;
    public event Action? OnAddParticipant;

    public DirectedEmotesWindow()
    {
        Title = Loc.GetString("directed-emotes-dialog-title");
        MinSize = SetSize = new Vector2(560, 360);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true
        };

        var side = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            MinWidth = 160,
            Margin = new Thickness(0, 0, 8, 0)
        };

        side.AddChild(new Label
        {
            Text = Loc.GetString("directed-emotes-dialog-participants"),
            StyleClasses = { StyleClass.LabelHeading },
            ModulateSelfOverride = EclipseText
        });

        _participants = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true
        };
        side.AddChild(WrapPanel(_participants, EclipseSurface, EclipseBorder, 4f));

        _addParticipantButton = new Button
        {
            Text = Loc.GetString("directed-emotes-dialog-add-participant"),
            Margin = new Thickness(0, 6, 0, 0)
        };
        StyleActionButton(_addParticipantButton);
        _addParticipantButton.OnPressed += _ => OnAddParticipant?.Invoke();
        side.AddChild(_addParticipantButton);

        var main = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true
        };

        _messages = new OutputPanel
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            ShowScrollDownButton = true,
            StyleBoxOverride = Rounded(EclipseSurface, Color.FromHex("#8A2F1233"), 4f, 6f, 4f)
        };
        StyleScrollDownButton(_messages);
        main.AddChild(_messages);

        var typeRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 8, 0, 0),
            SeparationOverride = 4
        };

        foreach (var type in new[]
                 {
                     DirectedEmotesMessageType.Whisper,
                     DirectedEmotesMessageType.Voice,
                     DirectedEmotesMessageType.Emotion
                 })
        {
            var button = CreateTypeButton(type);
            _typeButtons[type] = button;
            typeRow.AddChild(button);
        }

        SelectMessageType(DirectedEmotesMessageType.Voice);
        main.AddChild(typeRow);

        var inputRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Margin = new Thickness(0, 6, 0, 0),
            SeparationOverride = 6
        };

        _input = new LineEdit
        {
            HorizontalExpand = true,
            PlaceHolder = Loc.GetString("directed-emotes-dialog-input-placeholder"),
            MinHeight = 28f,
            StyleBoxOverride = Rounded(EclipseInput, EclipseAccent, 3f, 7f, 4f)
        };
        _input.OnTextEntered += args => Send(args.Text);
        inputRow.AddChild(_input);

        _sendButton = new Button
        {
            Text = Loc.GetString("directed-emotes-dialog-send"),
            MinHeight = 28f
        };
        StyleActionButton(_sendButton);
        _sendButton.OnPressed += _ => Send(_input.Text);
        inputRow.AddChild(_sendButton);

        main.AddChild(inputRow);

        root.AddChild(side);
        root.AddChild(main);
        ContentsContainer.AddChild(root);

        ContentsContainer.Margin = new Thickness(8);
    }

    public void SetParticipants(DirectedEmotesParticipantState[] participants, bool canAddParticipant)
    {
        _participants.RemoveAllChildren();
        foreach (var participant in participants)
        {
            _participants.AddChild(new Label
            {
                Text = participant.InRange
                    ? participant.Name
                    : Loc.GetString("directed-emotes-dialog-participant-out-of-range", ("player", participant.Name)),
                StyleClasses = { participant.InRange ? StyleClass.StatusGood : StyleClass.StatusOkay },
                Margin = new Thickness(0, 0, 0, 2)
            });
        }

        _addParticipantButton.Disabled = !canAddParticipant;
    }

    public void AddLine(string sender, string message, bool systemMessage, DirectedEmotesMessageType messageType)
    {
        FormattedMessage formatted;

        if (systemMessage)
        {
            formatted = FormattedMessage.FromMarkupPermissive(
                $"[color=#BFA88A][italic]{FormattedMessage.EscapeText(message)}[/italic][/color]");
        }
        else
        {
            var player = FormattedMessage.EscapeText(sender);
            var body = FormattedMessage.EscapeText(message);
            var text = messageType switch
            {
                DirectedEmotesMessageType.Whisper => Loc.GetString(
                    "directed-emotes-dialog-message-whisper",
                    ("player", player),
                    ("message", body)),
                DirectedEmotesMessageType.Emotion => Loc.GetString(
                    "directed-emotes-dialog-message-emotion",
                    ("player", player),
                    ("message", body)),
                _ => Loc.GetString(
                    "directed-emotes-dialog-message-voice",
                    ("player", player),
                    ("message", body))
            };

            formatted = FormattedMessage.FromMarkupPermissive(text);
        }

        _messages.AddMessage(formatted);
    }

    private void Send(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return;

        OnSend?.Invoke(text, _messageType);
        _input.Clear();
    }

    private Button CreateTypeButton(DirectedEmotesMessageType type)
    {
        var button = new Button
        {
            Text = Loc.GetString(GetTypeLocaleKey(type)),
            ToggleMode = true,
            HorizontalExpand = true,
            MinHeight = 26f
        };

        button.OnPressed += _ => SelectMessageType(type);
        return button;
    }

    private void SelectMessageType(DirectedEmotesMessageType type)
    {
        _messageType = type;

        foreach (var (buttonType, button) in _typeButtons)
        {
            var selected = buttonType == type;
            button.Pressed = selected;
            StyleTypeButton(button, selected);
        }

        _input.PlaceHolder = Loc.GetString(GetPlaceholderLocaleKey(type));
    }

    private static string GetTypeLocaleKey(DirectedEmotesMessageType type)
    {
        return type switch
        {
            DirectedEmotesMessageType.Whisper => "directed-emotes-dialog-type-whisper",
            DirectedEmotesMessageType.Emotion => "directed-emotes-dialog-type-emotion",
            _ => "directed-emotes-dialog-type-voice"
        };
    }

    private static string GetPlaceholderLocaleKey(DirectedEmotesMessageType type)
    {
        return type switch
        {
            DirectedEmotesMessageType.Whisper => "directed-emotes-dialog-input-placeholder-whisper",
            DirectedEmotesMessageType.Emotion => "directed-emotes-dialog-input-placeholder-emotion",
            _ => "directed-emotes-dialog-input-placeholder-voice"
        };
    }

    private static void StyleScrollDownButton(OutputPanel panel)
    {
        foreach (var child in panel.Children)
        {
            if (child is not Button { Name: "scrollLiveBtn" } scrollButton)
                continue;

            scrollButton.Text = Loc.GetString("directed-emotes-dialog-scroll-down");
            scrollButton.MaxWidth = 120;
            scrollButton.ModulateSelfOverride = EclipseText;
            scrollButton.StyleBoxOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#3A1600E8"),
                BorderColor = Color.FromHex("#D47D1B55"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 2,
                ContentMarginBottomOverride = 2,
            };
        }
    }

    private static PanelContainer WrapPanel(Control content, Color background, Color border, float radius)
    {
        var panel = new PanelContainer
        {
            VerticalExpand = true,
            PanelOverride = Rounded(background, border, radius, 6f, 4f)
        };
        panel.AddChild(content);
        return panel;
    }

    private static void StyleActionButton(Button button)
    {
        StyleActionButton(button, hovered: false);

        button.OnMouseEntered += _ =>
        {
            if (button.Disabled)
                return;

            StyleActionButton(button, hovered: true);
        };

        button.OnMouseExited += _ => StyleActionButton(button, hovered: false);
    }

    private static void StyleActionButton(Button button, bool hovered)
    {
        button.StyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = hovered ? Color.FromHex("#5A2800F0") : Color.FromHex("#3A1600E8"),
            BorderColor = hovered ? EclipseAccentHover : Color.FromHex("#D47D1B55"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 10,
            ContentMarginRightOverride = 10,
            ContentMarginTopOverride = 4,
            ContentMarginBottomOverride = 4,
        };
        button.ModulateSelfOverride = hovered ? Color.FromHex("#FFFAEE") : EclipseText;
    }

    private static void StyleTypeButton(Button button, bool selected)
    {
        button.StyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = selected ? Color.FromHex("#5A2800F0") : Color.FromHex("#090200F4"),
            BorderColor = selected ? EclipseAccentHover : Color.FromHex("#A84B0066"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 8,
            ContentMarginRightOverride = 8,
            ContentMarginTopOverride = 3,
            ContentMarginBottomOverride = 3,
        };
        button.ModulateSelfOverride = selected ? Color.FromHex("#FFFAEE") : EclipseTextMuted;
    }

    private static EclipseStyleBoxRounded Rounded(
        Color background,
        Color border,
        float radius,
        float horizontalPadding = 0f,
        float verticalPadding = 0f)
    {
        var style = new EclipseStyleBoxRounded
        {
            BackgroundColor = background,
            BorderColor = border,
            BorderThickness = border.A > 0 ? new Thickness(1) : new Thickness(0),
            Radius = radius,
        };

        style.SetContentMarginOverride(StyleBox.Margin.Horizontal, horizontalPadding);
        style.SetContentMarginOverride(StyleBox.Margin.Vertical, verticalPadding);
        return style;
    }
}
