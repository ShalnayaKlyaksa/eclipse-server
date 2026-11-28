using Content.Server.Research.Components;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using System.Linq;
using Content.Shared._Erida.Research;
// ReSharper disable once CheckNamespace
namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    private void InitializeBkm()
    {
        SubscribeLocalEvent<ResearchServerComponent, MapInitEvent>(OnServerInit);
    }

    private void UpdateFancyConsoleInterface(EntityUid uid,
        ResearchConsoleComponent? component = null,
        ResearchClientComponent? clientComponent = null)
    {
        if (!Resolve(uid, ref component, ref clientComponent, false))
            return;

        var terminal = GetTerminalState(uid, component);
        if (terminal.Username == null)
        {
            SendConsoleState(uid, component, new ResearchConsoleBoundInterfaceState(0, new()) { Terminal = terminal });
            return;
        }
        var allTechs = PrototypeManager.EnumeratePrototypes<TechnologyPrototype>().Where(x => !x.Hidden).ToList();
        Dictionary<string, ResearchAvailability> techList;
        var points = 0;

        if (TryGetClientServer(uid, out var serverUid, out var server, clientComponent) &&
            TryComp<TechnologyDatabaseComponent>(serverUid, out var db))
        {
            var unlockedTechs = new HashSet<string>(db.UnlockedTechnologies.Select(p => p.ToString()));
            techList = allTechs.ToDictionary(
                proto => proto.ID,
                proto =>
                {
                    if (unlockedTechs.Contains(proto.ID))
                        return ResearchAvailability.Researched;

                    var prereqsMet = proto.TechnologyPrerequisites.All(p => unlockedTechs.Contains(p.ToString()));
                    var canAfford = server.Points >= proto.Cost;

                    return prereqsMet
                       ? (canAfford ? ResearchAvailability.Available : ResearchAvailability.PrereqsMet)
                       : ResearchAvailability.Unavailable;
                });

            points = clientComponent.ConnectedToServer ? server.Points : 0;
        }
        else
        {
            techList = allTechs.ToDictionary(proto => proto.ID, _ => ResearchAvailability.Unavailable);
        }

        SendConsoleState(uid, component, new ResearchConsoleBoundInterfaceState(points, techList) { Terminal = terminal });
    }

    private void SendConsoleState(EntityUid uid, ResearchConsoleComponent component, ResearchConsoleBoundInterfaceState state)
    {
        if (component.AdminSession is { } session)
        {
            // The shared UI state is replicated to nearby players even when they have not opened the console.
            if (HasTerminalAdminAccess(session.Player, session.Actor))
                _uiSystem.ServerSendUiMessage(uid, ResearchConsoleUiKey.Key, new ResearchTerminalAdminStateMessage(state), session.Actor);
            return;
        }

        _uiSystem.SetUiState(uid, ResearchConsoleUiKey.Key, state);
    }

    private void OnServerInit(Entity<ResearchServerComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<TechnologyDatabaseComponent>(ent, out var techBase))
            return;

        foreach (var tech in techBase.RoundstartTechnologies)
        {
            AddTechnology(ent, tech, techBase);
        }
    }

}
