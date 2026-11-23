using System.IO;
using System.Linq;
using System.Text.Json;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Eclipse.News;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Eclipse.News;

public sealed class EclipseForumSystem : EntitySystem
{
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly IAdminManager _admins = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const int PageSize = 12;
    private static readonly ResPath StorePath = new("/eclipse_forum.json");
    private readonly Dictionary<NetUserId, TimeSpan> _nextRequest = new();
    private readonly Dictionary<NetUserId, TimeSpan> _nextWrite = new();
    private ForumStore _store = new(1, []);
    private bool _available;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<ForumRequest>(OnRequest);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;

        try
        {
            if (_resources.UserData.Exists(StorePath))
            {
                using var input = _resources.UserData.OpenRead(StorePath);
                _store = JsonSerializer.Deserialize<ForumStore>(input)
                    ?? throw new InvalidDataException("Forum store is empty.");
            }
            _available = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error($"Failed to load the Eclipse forum: {e}");
        }
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        base.Shutdown();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        _nextRequest.Remove(args.Session.UserId);
        _nextWrite.Remove(args.Session.UserId);
    }

    private void OnRequest(ForumRequest request, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        var now = _timing.RealTime;
        if (now < _nextRequest.GetValueOrDefault(session.UserId))
            return;

        _nextRequest[session.UserId] = now + TimeSpan.FromMilliseconds(200);
        var response = new ForumResponse
        {
            RequestId = request.RequestId,
            CanModerate = _admins.GetAdminData(session)?.HasFlag(AdminFlags.Admin) == true,
        };
        if (!_available)
        {
            response.Error = "Форум временно недоступен.";
        }
        else
        {
            var topic = _store.Topics.Find(item => item.Question.Id == request.TopicId);
            switch (request.Action)
            {
                case ForumAction.List:
                    WriteList(response, request.Page);
                    break;
                case ForumAction.Read:
                    if (topic == null)
                        response.Error = "Вопрос больше недоступен.";
                    else
                        WriteTopic(response, topic, request.Page);
                    break;
                case ForumAction.Ask:
                case ForumAction.Reply:
                    Publish(request, response, session, topic, now);
                    break;
                case ForumAction.Delete:
                    Delete(request, response, topic, session);
                    break;
                default:
                    response.Error = "Неизвестное действие.";
                    break;
            }
        }
        RaiseNetworkEvent(response, session.Channel);
    }

    private void Publish(ForumRequest request, ForumResponse response, ICommonSession session, ForumTopic? topic, TimeSpan now)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var body = request.Body?.Trim() ?? string.Empty;
        var ask = request.Action == ForumAction.Ask;
        if (body.Length is 0 or > ForumRequest.BodyLimit || body.Any(ch => char.IsControl(ch) && ch is not ('\n' or '\r' or '\t'))
            || ask && (title.Length is 0 or > ForumRequest.TitleLimit || title.Any(char.IsControl)))
        {
            response.Error = $"Проверьте текст. Заголовок до {ForumRequest.TitleLimit} символов, сообщение до {ForumRequest.BodyLimit}.";
            return;
        }
        if (!ask && topic == null)
        {
            response.Error = "Вопрос больше недоступен.";
            return;
        }
        if (now < _nextWrite.GetValueOrDefault(session.UserId))
        {
            response.Error = "Подождите несколько секунд перед следующей публикацией.";
            return;
        }

        _nextWrite[session.UserId] = now + TimeSpan.FromSeconds(5);
        var post = new ForumPost(_store.NextId, session.Name, body, DateTime.UtcNow);
        var next = new ForumStore(_store.NextId + 1, [.._store.Topics]);
        ForumTopic updated;
        if (ask)
        {
            updated = new ForumTopic(title, post, []);
            next.Topics.Add(updated);
        }
        else
        {
            updated = topic! with { Answers = [..topic!.Answers, post] };
            next.Topics[next.Topics.IndexOf(topic)] = updated;
        }

        if (Commit(next, response))
            WriteTopic(response, updated, int.MaxValue);
    }

    private void Delete(ForumRequest request, ForumResponse response, ForumTopic? topic, ICommonSession session)
    {
        if (!response.CanModerate || topic == null)
        {
            response.Error = "Публикация недоступна для удаления.";
            return;
        }

        var next = new ForumStore(_store.NextId, [.._store.Topics]);
        var answers = topic.Answers.Where(post => post.Id != request.PostId).ToList();
        var deleteTopic = request.PostId == topic.Question.Id;
        if (!deleteTopic && answers.Count == topic.Answers.Count)
        {
            response.Error = "Ответ уже удалён.";
            return;
        }
        var updated = topic with { Answers = answers };
        if (deleteTopic)
            next.Topics.Remove(topic);
        else
            next.Topics[next.Topics.IndexOf(topic)] = updated;

        if (!Commit(next, response))
            return;

        Log.Info($"{session.UserId} removed forum publication {request.PostId} in topic {request.TopicId}");
        if (deleteTopic)
            WriteList(response, 0);
        else
            WriteTopic(response, updated, request.Page);
    }

    private void WriteList(ForumResponse response, int page)
    {
        SetPage(response, page, _store.Topics.Count);
        response.Questions = _store.Topics.AsEnumerable().Reverse()
            .Skip(response.Page * PageSize).Take(PageSize).Select(Summarize).ToArray();
    }

    private static void WriteTopic(ForumResponse response, ForumTopic topic, int page)
    {
        SetPage(response, page, topic.Answers.Count);
        response.Topic = Summarize(topic);
        response.Question = topic.Question;
        response.Answers = topic.Answers.Skip(response.Page * PageSize).Take(PageSize).ToArray();
    }

    private static ForumQuestion Summarize(ForumTopic topic) =>
        new(topic.Question.Id, topic.Title, topic.Question.Author, topic.Question.PostedAt, topic.Answers.Count);

    private static void SetPage(ForumResponse response, int page, int itemCount)
    {
        response.PageCount = Math.Max(1, (itemCount + PageSize - 1) / PageSize);
        response.Page = Math.Clamp(page, 0, response.PageCount - 1);
    }

    private bool Commit(ForumStore next, ForumResponse response)
    {
        try
        {
            // ponytail: rewrite the forum file on publication; use the server database if the archive grows too large.
            var json = JsonSerializer.Serialize(next);
            if (_resources.UserData.RootDir is { } directory)
            {
                var path = Path.Combine(directory, "eclipse_forum.json");
                File.WriteAllText(path + ".tmp", json);
                File.Move(path + ".tmp", path, overwrite: true);
            }
            else
            {
                using var stream = _resources.UserData.OpenWriteText(StorePath);
                stream.Write(json);
            }
            _store = next;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Failed to save the Eclipse forum: {e}");
            response.Error = "Не удалось сохранить публикацию. Попробуйте ещё раз.";
            return false;
        }
    }

    private sealed record ForumStore(int NextId, List<ForumTopic> Topics);
    private sealed record ForumTopic(string Title, ForumPost Question, List<ForumPost> Answers);
}
