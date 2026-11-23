using System.Linq;
using Content.Server.Administration.Logs;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Content.Shared.Database;

namespace Content.Server.CartridgeLoader.Cartridges;

public sealed class NotekeeperCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridges = default!;
    [Dependency] private readonly IAdminLogManager _logs = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<NotekeeperCartridgeComponent, CartridgeMessageEvent>(OnMessage);
        SubscribeLocalEvent<NotekeeperCartridgeComponent, CartridgeUiReadyEvent>((uid, component, args) => UpdateState(uid, args.Loader, component));
    }
    private void Migrate(NotekeeperCartridgeComponent component)
    {
        if (component.Entries.Count != 0 || component.Notes.Count == 0) return;
        foreach (var note in component.Notes.Take(32)) component.Entries.Add(new() { Title = note.Split('\n')[0][..Math.Min(80, note.Split('\n')[0].Length)], Body = note[..Math.Min(4000, note.Length)] });
    }
    public string Apply(NotekeeperCartridgeComponent component, NotekeeperUiMessageEvent message, out Guid selected)
    {
        Migrate(component); selected = message.Id;
        if (message.Note == null || message.Title == null || message.Note.Length > 4000 || message.Title.Length > 80 ||
            message.Title.Any(char.IsControl) || message.Note.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) return "pda-services-invalid";
        var entry = component.Entries.FirstOrDefault(entry => entry.Id == message.Id);
        if (message.Action == NotekeeperUiAction.Add)
        {
            if (component.Entries.Count >= 32) return "pda-notes-limit";
            entry = new() { Title = string.IsNullOrWhiteSpace(message.Title) ? Loc.GetString("pda-notes-untitled") : message.Title.Trim(), Body = message.Note };
            component.Entries.Add(entry); selected = entry.Id;
        }
        else
        {
            if (message.Id == Guid.Empty && message.Action == NotekeeperUiAction.Remove) entry = component.Entries.FirstOrDefault(entry => entry.Body == message.Note);
            if (entry == null || entry.Revision != message.Revision) return "pda-notes-conflict";
            if (message.Action == NotekeeperUiAction.Remove) { component.Entries.Remove(entry); selected = Guid.Empty; }
            else if (message.Action == NotekeeperUiAction.Edit)
            {
                entry.Title = string.IsNullOrWhiteSpace(message.Title) ? Loc.GetString("pda-notes-untitled") : message.Title.Trim();
                entry.Body = message.Note; entry.Revision++;
            }
            else return "pda-services-invalid";
        }
        component.Notes = component.Entries.Select(entry => entry.Body).ToList();
        return "pda-notes-saved";
    }
    private void OnMessage(EntityUid uid, NotekeeperCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not NotekeeperUiMessageEvent message) return;
        var feedback = Apply(component, message, out var selected);
        if (feedback == "pda-notes-saved") _logs.Add(LogType.PdaInteract, LogImpact.Low, $"{ToPrettyString(args.Actor)} {message.Action} note {selected} on {ToPrettyString(uid)}");
        UpdateState(uid, GetEntity(args.LoaderUid), component, feedback, message.Request, selected);
    }
    private void UpdateState(EntityUid uid, EntityUid loader, NotekeeperCartridgeComponent component, string? feedback = null, Guid request = default, Guid selected = default)
    {
        Migrate(component);
        _cartridges.UpdateCartridgeUiState(loader, new NotekeeperUiState(component.Notes) { Entries = component.Entries.ToArray(), Feedback = feedback, Request = request, Selected = selected });
    }
}
