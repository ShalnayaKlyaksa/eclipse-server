using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Shared._EinsteinEngines.DirectedEmotes;
using Content.Shared.Database;
using Content.Shared.IdentityManagement;
using Content.Shared.Players.RateLimiting;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._EinsteinEngines.DirectedEmotes;

public sealed class DirectedEmotesSystem : EntitySystem
{
    private const float InteractionRange = 3f;
    private const int MaxParticipants = 6;
    private static readonly TimeSpan RangeCheckInterval = TimeSpan.FromSeconds(0.5);

    [Dependency] private readonly IServerNetManager _net = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly Dictionary<int, DirectedConversation> _conversations = new();
    private readonly Dictionary<NetUserId, int> _participantConversations = new();
    private int _nextConversationId = 1;
    private TimeSpan _nextRangeCheck;

    public override void Initialize()
    {
        base.Initialize();

        _net.RegisterNetMessage<DirectedEmotesStartMessage>(OnStart);
        _net.RegisterNetMessage<DirectedEmotesSendMessage>(OnSend);
        _net.RegisterNetMessage<DirectedEmotesAddParticipantMessage>(OnAddParticipant);
        _net.RegisterNetMessage<DirectedEmotesLeaveMessage>(OnLeave);
        _net.RegisterNetMessage<DirectedEmotesStateMessage>();
        _net.RegisterNetMessage<DirectedEmotesChatMessage>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextRangeCheck)
            return;

        _nextRangeCheck = _timing.CurTime + RangeCheckInterval;

        foreach (var conversation in _conversations.Values.ToArray())
        {
            if (conversation.Participants.Count == 0)
            {
                _conversations.Remove(conversation.Id);
                continue;
            }

            UpdateConversationRanges(conversation);
        }
    }

    private void OnStart(DirectedEmotesStartMessage message)
    {
        var session = _players.GetSessionByChannel(message.MsgChannel);
        if (session.AttachedEntity is not { Valid: true } source)
            return;

        var target = GetEntity(message.Target);
        if (!Exists(target)
            || target == source
            || !HasComp<ActorComponent>(target)
            || !TryGetDistance(source, target, out var distance)
            || distance > InteractionRange)
        {
            _chat.DispatchServerMessage(session, Loc.GetString("directed-emotes-menu-no-targets"), true);
            return;
        }

        if (_participantConversations.TryGetValue(session.UserId, out var existingId)
            && _conversations.TryGetValue(existingId, out var existing))
        {
            SendState(existing, session);
            return;
        }

        if (!TryComp(target, out ActorComponent? targetActor))
            return;

        var id = _nextConversationId++;
        var conversation = new DirectedConversation(id, session.UserId);
        _conversations[id] = conversation;

        AddParticipant(conversation, session);
        AddParticipant(conversation, targetActor.PlayerSession);

        UpdateConversationRanges(conversation, forceState: true);
        SendSystemLine(conversation, Loc.GetString("directed-emotes-dialog-started"));
    }

    private void OnSend(DirectedEmotesSendMessage message)
    {
        var session = _players.GetSessionByChannel(message.MsgChannel);
        if (!_conversations.TryGetValue(message.ConversationId, out var conversation)
            || !conversation.Participants.Contains(session.UserId)
            || !conversation.ActiveParticipants.Contains(session.UserId))
        {
            return;
        }

        if (_chat.HandleRateLimit(session) != RateLimitStatus.Allowed)
            return;

        var text = message.Message.Trim();
        if (text.Length == 0 || _chat.MessageCharacterLimit(session, text))
            return;

        var messageType = Enum.IsDefined(message.MessageType)
            ? message.MessageType
            : DirectedEmotesMessageType.Voice;

        text = FormattedMessage.RemoveMarkupOrThrow(text);
        var sender = GetSessionName(session);
        SendLine(conversation, sender, text, false, messageType, activeOnly: true);
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Directed emotes chat from {session:Player} ({messageType}): {text}");
    }

    private void OnAddParticipant(DirectedEmotesAddParticipantMessage message)
    {
        var session = _players.GetSessionByChannel(message.MsgChannel);
        if (!_conversations.TryGetValue(message.ConversationId, out var conversation)
            || !conversation.Participants.Contains(session.UserId)
            || conversation.Participants.Count >= MaxParticipants
            || !TryGetAnchor(conversation, out var anchor))
        {
            return;
        }

        var candidate = GetNearbyCandidate(conversation, anchor);
        if (candidate == null)
            return;

        AddParticipant(conversation, candidate);
        UpdateConversationRanges(conversation, forceState: true);
        SendSystemLine(conversation, Loc.GetString("directed-emotes-dialog-participant-added", ("player", GetSessionName(candidate))));
    }

    private void OnLeave(DirectedEmotesLeaveMessage message)
    {
        var session = _players.GetSessionByChannel(message.MsgChannel);
        if (!_conversations.TryGetValue(message.ConversationId, out var conversation)
            || !conversation.Participants.Contains(session.UserId))
        {
            return;
        }

        RemoveParticipant(conversation, session.UserId);
        if (conversation.Participants.Count == 0)
        {
            _conversations.Remove(conversation.Id);
            return;
        }

        SendState(conversation);
    }

    private void AddParticipant(DirectedConversation conversation, ICommonSession session)
    {
        conversation.Participants.Add(session.UserId);
        conversation.ActiveParticipants.Add(session.UserId);
        _participantConversations[session.UserId] = conversation.Id;
    }

    private void UpdateConversationRanges(DirectedConversation conversation, bool forceState = false)
    {
        if (!TryGetAnchor(conversation, out var anchor))
            return;

        var changed = false;
        foreach (var userId in conversation.Participants.ToArray())
        {
            if (!_players.TryGetSessionById(userId, out var session)
                || session.AttachedEntity is not { Valid: true } entity)
            {
                RemoveParticipant(conversation, userId);
                changed = true;
                continue;
            }

            var inRange = userId == conversation.Anchor
                || TryGetDistance(anchor, entity, out var distance) && distance <= InteractionRange;

            if (inRange && conversation.ActiveParticipants.Add(userId))
            {
                changed = true;
                SendSystemLine(conversation, Loc.GetString("directed-emotes-dialog-returned"));
            }
            else if (!inRange && conversation.ActiveParticipants.Remove(userId))
            {
                changed = true;
                SendSystemLine(conversation, Loc.GetString("directed-emotes-dialog-left"));
            }
        }

        if (changed || forceState)
            SendState(conversation);
    }

    private void RemoveParticipant(DirectedConversation conversation, NetUserId userId)
    {
        conversation.Participants.Remove(userId);
        conversation.ActiveParticipants.Remove(userId);
        _participantConversations.Remove(userId);

        if (conversation.Anchor == userId && conversation.Participants.Count > 0)
            conversation.Anchor = conversation.Participants.First();
    }

    private ICommonSession? GetNearbyCandidate(DirectedConversation conversation, EntityUid anchor)
    {
        ICommonSession? best = null;
        var bestDistance = float.MaxValue;

        foreach (var session in _players.Sessions)
        {
            if (conversation.Participants.Contains(session.UserId)
                || session.AttachedEntity is not { Valid: true } entity
                || !TryGetDistance(anchor, entity, out var distance)
                || distance > InteractionRange
                || distance >= bestDistance)
            {
                continue;
            }

            best = session;
            bestDistance = distance;
        }

        return best;
    }

    private bool TryGetAnchor(DirectedConversation conversation, out EntityUid anchor)
    {
        anchor = default;
        return _players.TryGetSessionById(conversation.Anchor, out var session)
            && session.AttachedEntity is { Valid: true } entity
            && (anchor = entity).Valid;
    }

    private bool TryGetDistance(EntityUid first, EntityUid second, out float distance)
    {
        distance = default;
        return TryComp(first, out TransformComponent? firstXform)
            && TryComp(second, out TransformComponent? secondXform)
            && secondXform.Coordinates.TryDistance(EntityManager, firstXform.Coordinates, out distance);
    }

    private void SendState(DirectedConversation conversation, ICommonSession? recipient = null)
    {
        var participants = conversation.Participants
            .Select(userId =>
            {
                var name = _players.TryGetSessionById(userId, out var session)
                    ? GetSessionName(session)
                    : Loc.GetString("directed-emotes-dialog-unknown-player");
                return new DirectedEmotesParticipantState(name, conversation.ActiveParticipants.Contains(userId));
            })
            .ToArray();

        var canAdd = conversation.Participants.Count < MaxParticipants
            && TryGetAnchor(conversation, out var anchor)
            && GetNearbyCandidate(conversation, anchor) != null;

        var message = new DirectedEmotesStateMessage
        {
            ConversationId = conversation.Id,
            Participants = participants,
            CanAddParticipant = canAdd
        };

        if (recipient != null)
        {
            _net.ServerSendMessage(message, recipient.Channel);
            return;
        }

        foreach (var session in GetParticipantSessions(conversation))
        {
            _net.ServerSendMessage(message, session.Channel);
        }
    }

    private void SendSystemLine(DirectedConversation conversation, string text)
    {
        SendLine(conversation, string.Empty, text, true, DirectedEmotesMessageType.Voice, activeOnly: false);
    }

    private void SendLine(
        DirectedConversation conversation,
        string sender,
        string text,
        bool system,
        DirectedEmotesMessageType messageType,
        bool activeOnly)
    {
        var message = new DirectedEmotesChatMessage
        {
            ConversationId = conversation.Id,
            Sender = sender,
            Message = text,
            SystemMessage = system,
            MessageType = messageType
        };

        foreach (var session in GetParticipantSessions(conversation, activeOnly))
        {
            _net.ServerSendMessage(message, session.Channel);
        }
    }

    private IEnumerable<ICommonSession> GetParticipantSessions(DirectedConversation conversation, bool activeOnly = false)
    {
        foreach (var userId in conversation.Participants)
        {
            if (activeOnly && !conversation.ActiveParticipants.Contains(userId))
                continue;

            if (_players.TryGetSessionById(userId, out var session))
                yield return session;
        }
    }

    private string GetSessionName(ICommonSession session)
    {
        return session.AttachedEntity is { Valid: true } entity
            ? Identity.Name(entity, EntityManager)
            : session.Name;
    }

    private sealed class DirectedConversation(int id, NetUserId anchor)
    {
        public readonly int Id = id;
        public NetUserId Anchor = anchor;
        public readonly HashSet<NetUserId> Participants = [];
        public readonly HashSet<NetUserId> ActiveParticipants = [];
    }
}
