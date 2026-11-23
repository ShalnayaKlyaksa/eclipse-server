using System.Linq;
using Content.Server.Research.Components;
using Content.Shared.GameTicking;
using Content.Shared.PDA;

namespace Content.Server.PDA;

public sealed class PdaGroupConversation
{
    public required string Name;
    public required string Owner;
    public readonly HashSet<string> Members = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<PdaChatLine> Messages = new();
    public readonly Dictionary<string, long> Read = new(StringComparer.OrdinalIgnoreCase);
}

public sealed partial class PdaPersonalServicesSystem
{
    private readonly Dictionary<EntityUid, string> _selectedChats = new();
    private const int ChatHistoryLimit = 200;
    private const int GroupMemberLimit = 64;

    private void InitializeMessenger()
    {
        SubscribeLocalEvent<PdaComponent, PdaMessengerRequestMessage>(OnMessengerRequest);
        SubscribeLocalEvent<PdaComponent, PdaMessengerActionMessage>(OnMessengerAction);
    }

    private void OnMessengerRequest(EntityUid uid, PdaComponent component, PdaMessengerRequestMessage message)
    {
        if (!PdaUiKey.Key.Equals(message.UiKey) || message.Chat is { Length: > 64 }) return;
        var state = MessengerState(uid, message.Chat, message.MarkRead);
        _ui.ServerSendUiMessage(uid, PdaUiKey.Key, state);
        if (!message.MarkRead) return;
        if (!TryIdentity(uid, out var mailbox, out var registry)) return;
        if (TryConversation(mailbox.Domain, registry, mailbox.Username, state.Selected, out var peer, out var group))
        {
            if (group != null) foreach (var member in group.Members.Where(id => id != mailbox.Username)) Push(mailbox.Domain, member);
            else if (peer != null) Push(mailbox.Domain, peer);
        }
    }

    private void OnMessengerAction(EntityUid uid, PdaComponent component, PdaMessengerActionMessage message)
    {
        if (!PdaUiKey.Key.Equals(message.UiKey)) return;
        var result = PerformMessengerAction(uid, message);
        var state = MessengerState(uid); state.Feedback = result; state.Request = message.Request;
        _ui.ServerSendUiMessage(uid, PdaUiKey.Key, state);
    }

    private static bool ValidChatText(string? text) => text is { Length: > 0 and <= 1000 } &&
        !string.IsNullOrWhiteSpace(text) && !text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
    private static bool ValidGroupName(string name) => name.Trim() is { Length: > 0 and <= 48 } && !name.Any(char.IsControl);

    private bool TryConversation(EntityUid domain, ResearchAccountsComponent registry, string user, string key, out string? peer, out PdaGroupConversation? group)
    {
        peer = null; group = null;
        if (key.StartsWith("d:", StringComparison.Ordinal))
        {
            peer = ResolveId(registry, key[2..]);
            return peer != null && peer != user;
        }
        return key.StartsWith("g:", StringComparison.Ordinal) && Guid.TryParseExact(key[2..], "N", out var id) &&
            Comp<PdaPersonalServicesComponent>(domain).Groups.TryGetValue(id, out group) && group.Members.Contains(user);
    }

    public string PerformMessengerAction(EntityUid pda, PdaMessengerActionMessage message)
    {
        if (!TryIdentity(pda, out var mailbox, out var registry)) return "pda-services-no-account";
        if (message.Chat == null || message.Chat.Length > 64 || message.Text == null || message.Text.Length > 1000 ||
            message.Members == null || message.Members.Length > GroupMemberLimit || message.Members.Any(id => id == null || id.Length > 24)) return "pda-services-invalid";
        var domain = mailbox.Domain; var user = mailbox.Username;
        var own = Account(domain, user); var service = Comp<PdaPersonalServicesComponent>(domain);
        if (message.Request != Guid.Empty && own.ChatRequests.Contains(message.Request)) return "pda-orbit-done";
        var affected = new HashSet<string> { user };
        string? notification = null;
        var text = message.Text.Trim();
        if (message.Action == PdaMessengerAction.OpenDirect)
        {
            var target = ResolveId(registry, text);
            if (target == null || target == user) return "pda-services-recipient-not-found";
            own.Contacts.Add(target); _selectedChats[pda] = "d:" + registry.Credentials[target].PublicId;
        }
        else if (message.Action == PdaMessengerAction.CreateGroup)
        {
            if (!ValidGroupName(text) || service.Groups.Count >= 128 || service.Groups.Values.Count(g => g.Members.Contains(user)) >= 32) return "pda-orbit-group-limit";
            var members = message.Members.Select(id => ResolveId(registry, id)).ToArray();
            if (members.Any(id => id == null)) return "pda-services-recipient-not-found";
            var id = Guid.NewGuid(); var group = new PdaGroupConversation { Name = text, Owner = user };
            group.Members.Add(user); group.Members.UnionWith(members.Select(id => id!));
            if (group.Members.Count > GroupMemberLimit) return "pda-orbit-group-limit";
            service.Groups.Add(id, group); affected.UnionWith(group.Members); _selectedChats[pda] = "g:" + id.ToString("N");
        }
        else
        {
            if (!TryConversation(domain, registry, user, message.Chat, out var peer, out var group)) return "pda-orbit-unavailable";
            var history = group?.Messages ?? own.Messages.Where(line => line.From == peer || line.To == peer).ToList();
            switch (message.Action)
            {
                case PdaMessengerAction.Send:
                    if (!ValidChatText(message.Text)) return "pda-services-invalid";
                    if (_timing.CurTime < own.NextMessage) return "pda-services-slow-down";
                    if (peer != null && (own.Blocked.Contains(peer) || Account(domain, peer).Blocked.Contains(user))) return "pda-services-message-unavailable";
                    if (message.Message != 0 && !history.Any(line => line.Id == message.Message)) return "pda-orbit-message-missing";
                    own.NextMessage = _timing.CurTime + TimeSpan.FromSeconds(1);
                    var line = new PdaChatLine(user, peer ?? message.Chat, text, (int) _timing.CurTime.TotalSeconds, ++service.NextChatMessage, message.Message);
                    if (group != null)
                    {
                        Append(group.Messages, line, ChatHistoryLimit); group.Read[user] = line.Id;
                        affected.UnionWith(group.Members);
                    }
                    else
                    {
                        var recipient = Account(domain, peer!); own.Contacts.Add(peer!);
                        Append(own.Messages, line, ChatHistoryLimit); Append(recipient.Messages, line, ChatHistoryLimit);
                        own.ReadChats[peer!] = line.Id; recipient.Unread.Add(user); affected.Add(peer!);
                    }
                    notification = "pda-services-new-message";
                    break;
                case PdaMessengerAction.Edit:
                case PdaMessengerAction.Delete:
                    var original = history.FirstOrDefault(line => line.Id == message.Message);
                    if (original == null) return "pda-orbit-message-missing";
                    if (original.From != user && !(message.Action == PdaMessengerAction.Delete && group?.Owner == user)) return "pda-orbit-owner-only";
                    if (message.Action == PdaMessengerAction.Edit && !ValidChatText(message.Text)) return "pda-services-invalid";
                    void Apply(List<PdaChatLine> lines)
                    {
                        var index = lines.FindIndex(line => line.Id == message.Message);
                        if (index < 0) return;
                        if (message.Action == PdaMessengerAction.Delete) lines.RemoveAt(index);
                        else lines[index] = lines[index] with { Text = text, Edited = true };
                    }
                    if (group != null) { Apply(group.Messages); affected.UnionWith(group.Members); }
                    else { Apply(own.Messages); Apply(Account(domain, peer!).Messages); affected.Add(peer!); }
                    break;
                case PdaMessengerAction.ToggleBlock:
                    if (peer == null) return "pda-services-invalid";
                    if (!own.Blocked.Remove(peer)) own.Blocked.Add(peer);
                    break;
                case PdaMessengerAction.RenameGroup:
                    if (group == null || group.Owner != user) return "pda-orbit-owner-only";
                    if (!ValidGroupName(text)) return "pda-services-invalid";
                    group.Name = text; affected.UnionWith(group.Members);
                    break;
                case PdaMessengerAction.AddMembers:
                    if (group == null || group.Owner != user) return "pda-orbit-owner-only";
                    var invited = message.Members.Select(id => ResolveId(registry, id)).ToArray();
                    if (invited.Any(id => id == null)) return "pda-services-recipient-not-found";
                    var resulting = group.Members.Union(invited.Select(id => id!)).ToArray();
                    if (resulting.Length > GroupMemberLimit) return "pda-orbit-group-limit";
                    group.Members.UnionWith(resulting); affected.UnionWith(group.Members);
                    break;
                case PdaMessengerAction.RemoveMember:
                    if (group == null || group.Owner != user) return "pda-orbit-owner-only";
                    var removed = ResolveId(registry, text);
                    if (removed == null || removed == user || !group.Members.Contains(removed)) return "pda-services-invalid";
                    affected.UnionWith(group.Members); group.Members.Remove(removed); group.Read.Remove(removed);
                    break;
                case PdaMessengerAction.LeaveGroup:
                    if (group == null) return "pda-services-invalid";
                    affected.UnionWith(group.Members); group.Members.Remove(user); group.Read.Remove(user);
                    if (group.Members.Count == 0) service.Groups.Remove(Guid.ParseExact(message.Chat[2..], "N"));
                    else if (group.Owner == user) group.Owner = group.Members.OrderBy(id => id, StringComparer.Ordinal).First();
                    _selectedChats[pda] = "";
                    break;
                default: return "pda-services-invalid";
            }
            if (message.Action != PdaMessengerAction.LeaveGroup) _selectedChats[pda] = message.Chat;
        }
        if (message.Request != Guid.Empty)
        {
            own.ChatRequests.Enqueue(message.Request); if (own.ChatRequests.Count > 64) own.ChatRequests.Dequeue();
        }
        foreach (var member in affected)
        {
            Push(domain, member);
            if (notification != null && member != user) Notify(domain, member, "pda-orbit-title", notification);
        }
        return message.Action == PdaMessengerAction.Send ? "pda-services-message-sent" : "pda-orbit-done";
    }

    public PdaMessengerStateMessage MessengerState(EntityUid pda, string? requested = null, bool markRead = false)
    {
        var state = new PdaMessengerStateMessage();
        if (!TryIdentity(pda, out var mailbox, out var registry)) return state;
        var domain = mailbox.Domain; var user = mailbox.Username; var own = Account(domain, user);
        state.Available = true; state.OwnId = registry.Credentials[user].PublicId;
        if (requested != null) _selectedChats[pda] = requested.Length <= 64 ? requested : "";
        var selected = _selectedChats.GetValueOrDefault(pda, "");
        var valid = TryConversation(domain, registry, user, selected, out var peer, out var group);
        if (valid)
        {
            state.Selected = selected; state.Group = group != null; state.Owner = group?.Owner == user;
            state.Title = group?.Name ?? registry.Credentials[peer!].Name;
            state.Blocked = peer != null && own.Blocked.Contains(peer);
            var history = group?.Messages ?? own.Messages.Where(line => line.From == peer || line.To == peer).ToList();
            if (markRead)
            {
                var last = history.LastOrDefault()?.Id ?? 0;
                if (group != null) group.Read[user] = last;
                else { own.ReadChats[peer!] = last; own.Unread.Remove(peer!); }
            }
            var members = group?.Members.ToArray() ?? new[] { user, peer! };
            state.Members = members.Where(registry.Credentials.ContainsKey).Select(id => new PdaConversationMember(registry.Credentials[id].PublicId, registry.Credentials[id].Name, group?.Owner == id)).ToArray();
            state.Messages = history.Select(line => new PdaConversationMessage(line.Id, registry.Credentials.GetValueOrDefault(line.From)?.PublicId ?? line.From,
                registry.Credentials.GetValueOrDefault(line.From)?.Name ?? line.From, line.Text, line.Seconds, line.ReplyTo, line.Edited, line.From == user,
                group != null ? group.Members.Where(id => id != line.From).All(id => group.Read.GetValueOrDefault(id) >= line.Id) : Account(domain, line.From == user ? peer! : user).ReadChats.GetValueOrDefault(line.From) >= line.Id)).ToArray();
        }
        var chats = new List<PdaConversation>();
        foreach (var id in own.Contacts.Union(own.Messages.Select(line => line.From == user ? line.To : line.From)).Where(id => id != user && registry.Credentials.ContainsKey(id)).Distinct())
        {
            var history = own.Messages.Where(line => line.From == id || line.To == id).ToArray(); var last = history.LastOrDefault();
            chats.Add(new("d:" + registry.Credentials[id].PublicId, registry.Credentials[id].Name, last?.Text ?? "", history.Count(line => line.From == id && line.Id > own.ReadChats.GetValueOrDefault(id)), false, 2, last?.Seconds ?? 0));
        }
        foreach (var (id, conversation) in Comp<PdaPersonalServicesComponent>(domain).Groups)
        {
            if (!conversation.Members.Contains(user)) continue;
            var last = conversation.Messages.LastOrDefault();
            chats.Add(new("g:" + id.ToString("N"), conversation.Name, last?.Text ?? "", conversation.Messages.Count(line => line.From != user && line.Id > conversation.Read.GetValueOrDefault(user)), true, conversation.Members.Count, last?.Seconds ?? 0));
        }
        state.Chats = chats.OrderByDescending(chat => chat.Seconds).ThenBy(chat => chat.Title).Take(96).ToArray();
        return state;
    }

    private void SendMessengerState(EntityUid pda) => _ui.ServerSendUiMessage(pda, PdaUiKey.Key, MessengerState(pda));
}
