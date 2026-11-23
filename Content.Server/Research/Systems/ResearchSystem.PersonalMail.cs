using System.Linq;
using Content.Server.PDA;
using Content.Server.Research.Components;
using Content.Shared.Research.Components;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    public string HandlePersonalMail(EntityUid pda, ResearchMailActionMessage message)
    {
        if (!TryComp<ResearchPdaMailboxComponent>(pda, out var mailbox) || !TryComp<ResearchAccountsComponent>(mailbox.Domain, out var registry) ||
            !registry.Mailboxes.TryGetValue(mailbox.Username, out var inbox)) return "pda-services-no-account";
        if (!registry.SentMail.TryGetValue(mailbox.Username, out var sent)) registry.SentMail[mailbox.Username] = sent = new();
        if (message.Action != ResearchMailAction.Send)
        {
            var folder = message.SentFolder ? sent : inbox;
            var mail = folder.FirstOrDefault(item => item.Id == message.Id);
            if (mail == null) return "pda-services-invalid";
            if (message.Action == ResearchMailAction.Read) mail.Read = true;
            else if (message.Action == ResearchMailAction.Delete) folder.Remove(mail);
            else return "pda-services-invalid";
            return "pda-mail-updated";
        }
        if (message.Recipient == null || message.Subject == null || message.Body == null || message.Recipient.Length > 24 || message.Subject.Length > 80 ||
            message.Body.Length > 4000 || string.IsNullOrWhiteSpace(message.Subject) || string.IsNullOrWhiteSpace(message.Body)) return "pda-services-invalid";
        var recipient = PdaPersonalServicesSystem.ResolveId(registry, message.Recipient);
        if (recipient == null) return "pda-services-recipient-not-found";
        if (_timing.CurTime < registry.NextMail.GetValueOrDefault(mailbox.Username)) return "pda-services-slow-down";
        var own = registry.Credentials[mailbox.Username];
        var recipientId = registry.Credentials[recipient].PublicId;
        if (!SendResearchMail(mailbox.Domain, recipient, message.Subject.Trim(), message.Body.Trim(), own.Name + " / " + own.PublicId, own.PublicId, recipientId)) return "pda-services-recipient-not-found";
        var incoming = registry.Mailboxes[recipient][^1];
        incoming.SenderId = own.PublicId;
        incoming.RecipientId = recipientId;
        sent.Add(new ResearchMailMessage(incoming.Sender, incoming.Subject, incoming.Body) { SenderId = own.PublicId, RecipientId = recipientId, Read = true });
        if (sent.Count > 32) sent.RemoveAt(0);
        registry.NextMail[mailbox.Username] = _timing.CurTime + TimeSpan.FromSeconds(2);
        return "pda-mail-sent";
    }
}
