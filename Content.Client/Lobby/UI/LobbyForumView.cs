using Content.Client.MainMenu.UI;
using Content.Client.Stylesheets;
using Content.Shared.Eclipse.News;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

public sealed class EclipseForumClientSystem : EntitySystem
{
    private int _lastRequestId;
    public event Action<ForumResponse>? StateReceived;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ForumResponse>(response => StateReceived?.Invoke(response));
    }

    public int Request(ForumRequest request)
    {
        request.RequestId = ++_lastRequestId;
        RaiseNetworkEvent(request);
        return request.RequestId;
    }
}

public sealed class LobbyForumView : BoxContainer
{
    private static readonly Color Ink = Color.FromHex("#E4E4E4");
    private static readonly Color Muted = Color.FromHex("#B7B7B7");
    private static readonly Color PanelColor = Color.FromHex("#111111");
    private static readonly Color Border = Color.FromHex("#333333");

    private readonly BoxContainer _toolbar = new() { SeparationOverride = 8 };
    private readonly BoxContainer _posts = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 10 };
    private readonly ScrollContainer _scroll = new() { VerticalExpand = true, HScrollEnabled = false };
    private readonly BoxContainer _composer = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
    private readonly LineEdit _subject = new() { PlaceHolder = "Заголовок вопроса", MinHeight = 32 };
    private readonly TextEdit _message = new() { MinHeight = 100, Margin = new Thickness(8) };
    private readonly Label _notice = new() { StyleIdentifier = MainMenuControl.StyleIdentifierSubtle, ClipText = true };
    private EclipseForumClientSystem? _client;
    private int _requestId;
    private int _topicId;
    private int _page;
    private float _waiting;
    private bool _submitting;

    public LobbyForumView()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 10;
        _composer.Visible = false;
        _notice.Visible = false;
        _scroll.AddChild(_posts);
        AddChild(_toolbar);
        AddChild(_scroll);
        AddChild(_composer);
        AddChild(_notice);

        _composer.AddChild(_subject);
        _composer.AddChild(new Label
        {
            Text = "Сообщение",
            StyleIdentifier = MainMenuControl.StyleIdentifierSubtle,
        });
        _composer.AddChild(new PanelContainer
        {
            PanelOverride = ForgeStyle.Panel(PanelColor, Border),
            Children = { _message },
        });
        var actions = new BoxContainer { SeparationOverride = 8 };
        actions.AddChild(ActionButton("Опубликовать", Submit));
        actions.AddChild(ActionButton("Отмена", CloseComposer));
        _composer.AddChild(actions);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _client = IoCManager.Resolve<IEntityManager>().System<EclipseForumClientSystem>();
        _client.StateReceived += Receive;
    }

    protected override void ExitedTree()
    {
        if (_client != null)
            _client.StateReceived -= Receive;
        _client = null;
        base.ExitedTree();
    }

    public void Open()
    {
        if (_waiting > 0)
            return;

        if (_toolbar.ChildCount == 0)
            BuildToolbar(0, 1);
        Refresh();
    }

    private void Refresh() => Read(_topicId, _page);

    private void Read(int topicId, int page = 0) => Send(new ForumRequest
    {
        Action = topicId == 0 ? ForumAction.List : ForumAction.Read,
        TopicId = topicId,
        Page = page,
    });

    private void Send(ForumRequest request)
    {
        if (_waiting > 0 || _client == null)
            return;

        _waiting = 5;
        _submitting = request.Action is ForumAction.Ask or ForumAction.Reply;
        Notify("Загрузка…");
        _requestId = _client.Request(request);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_waiting <= 0)
            return;

        _waiting -= args.DeltaSeconds;
        if (_waiting <= 0)
        {
            _requestId = 0;
            Notify("Сервер не ответил. Попробуйте ещё раз.");
        }
    }

    private void Receive(ForumResponse response)
    {
        if (response.RequestId != _requestId)
            return;

        _waiting = 0;
        Notify(response.Error);
        if (response.Error != null)
            return;

        if (_submitting || _topicId != (response.Topic?.Id ?? 0))
            CloseComposer();

        _page = response.Page;
        _topicId = response.Topic?.Id ?? 0;
        BuildToolbar(response.Page, response.PageCount);
        _posts.RemoveAllChildren();

        if (response.Topic is { } topic && response.Question is { } question)
        {
            _posts.AddChild(BuildPost(question, topic.Title, response.CanModerate));
            _posts.AddChild(new Label
            {
                Text = $"Ответы · {topic.AnswerCount}",
                StyleIdentifier = MainMenuControl.StyleIdentifierHeaderGold,
            });
            foreach (var answer in response.Answers)
                _posts.AddChild(BuildPost(answer, null, response.CanModerate));
            if (response.Answers.Length == 0)
                _posts.AddChild(Paragraph("Ответов пока нет.", Muted));
        }
        else
        {
            foreach (var item in response.Questions)
                _posts.AddChild(BuildQuestionLink(item));
            if (response.Questions.Length == 0)
                _posts.AddChild(Paragraph("Вопросов пока нет. Нажмите «Задать вопрос», чтобы начать обсуждение.", Muted));
        }

        _scroll.VScroll = 0;
    }

    private void BuildToolbar(int page, int pageCount)
    {
        _toolbar.RemoveAllChildren();
        if (_topicId != 0)
            _toolbar.AddChild(ActionButton("← К вопросам", () => Read(0)));
        _toolbar.AddChild(ActionButton(_topicId == 0 ? "Задать вопрос" : "Ответить", OpenComposer));
        _toolbar.AddChild(ActionButton("Обновить", Refresh));
        _toolbar.AddChild(new Control { HorizontalExpand = true });
        if (pageCount <= 1)
            return;

        var previous = ActionButton("‹", () => Read(_topicId, page - 1));
        previous.Disabled = page == 0;
        var next = ActionButton("›", () => Read(_topicId, page + 1));
        next.Disabled = page + 1 >= pageCount;
        _toolbar.AddChild(previous);
        _toolbar.AddChild(new Label { Text = $"{page + 1} / {pageCount}", VAlign = Label.VAlignMode.Center });
        _toolbar.AddChild(next);
    }

    private Control BuildQuestionLink(ForumQuestion item)
    {
        var button = ActionButton(string.Empty, () => Read(item.Id));
        var column = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            Margin = new Thickness(14, 11),
            SeparationOverride = 5,
        };
        column.AddChild(Paragraph(item.Title, Ink, bold: true));
        column.AddChild(MetaLabel($"{Metadata(item.Author, item.PostedAt)}  ·  Ответов {item.AnswerCount}"));
        button.AddChild(column);
        return button;
    }

    private Control BuildPost(ForumPost post, string? title, bool canModerate)
    {
        var column = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
        if (title != null)
            column.AddChild(Paragraph(title, Ink, bold: true));
        column.AddChild(MetaLabel(Metadata(post.Author, post.PostedAt)));
        column.AddChild(Paragraph(post.Body, Ink));
        if (canModerate)
        {
            var confirm = false;
            Button? delete = null;
            delete = ActionButton("Удалить", () =>
            {
                if (!confirm)
                {
                    confirm = true;
                    delete!.Text = "Подтвердить удаление";
                    return;
                }

                Send(new ForumRequest { Action = ForumAction.Delete, TopicId = _topicId, PostId = post.Id, Page = _page });
            });
            delete.HorizontalAlignment = HAlignment.Right;
            column.AddChild(delete);
        }
        return new PanelContainer { PanelOverride = ForgeStyle.Panel(PanelColor, Border, 14, 12), Children = { column } };
    }

    private void OpenComposer()
    {
        _subject.Visible = _topicId == 0;
        _composer.Visible = true;
        if (_subject.Visible)
            _subject.GrabKeyboardFocus();
        else
            _message.GrabKeyboardFocus();
    }

    private void CloseComposer()
    {
        _composer.Visible = false;
        _subject.Text = string.Empty;
        _message.TextRope = Rope.Leaf.Empty;
        _message.CursorPosition = default;
    }

    private void Submit()
    {
        var subject = _subject.Text.Trim();
        var body = Rope.Collapse(_message.TextRope).Trim();
        if (_topicId == 0 && subject.Length is 0 or > ForumRequest.TitleLimit)
        {
            Notify($"Введите заголовок до {ForumRequest.TitleLimit} символов.");
            return;
        }
        if (body.Length is 0 or > ForumRequest.BodyLimit)
        {
            Notify($"Введите сообщение до {ForumRequest.BodyLimit} символов.");
            return;
        }
        Send(new ForumRequest
        {
            Action = _topicId == 0 ? ForumAction.Ask : ForumAction.Reply,
            TopicId = _topicId,
            Title = subject,
            Body = body,
        });
    }

    private void Notify(string? text)
    {
        _notice.Text = text;
        _notice.Visible = text != null;
    }

    private static string Metadata(string author, DateTime date) => $"{author}  ·  {date.ToLocalTime():dd.MM.yyyy HH:mm}";

    private static Label MetaLabel(string text) => new()
    {
        Text = text,
        StyleIdentifier = MainMenuControl.StyleIdentifierTinySubtle,
        ClipText = true,
    };

    private static RichTextLabel Paragraph(string text, Color color, bool bold = false)
    {
        var label = new RichTextLabel();
        var message = new FormattedMessage();
        if (bold)
            message.PushTag(new MarkupNode("bold", null, null));
        message.AddText(text);
        if (bold)
            message.Pop();
        label.SetMessage(message, color);
        return label;
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            MinHeight = 34,
            StyleIdentifier = MainMenuControl.StyleIdentifierSecondary,
        };
        button.Label.Margin = string.IsNullOrEmpty(text) ? default : new Thickness(12, 0);
        button.OnPressed += _ => action();
        return button;
    }
}
