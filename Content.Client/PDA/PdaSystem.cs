using Content.Client.UserInterface.Fragments;
using System.Linq;
using Content.Shared.GameTicking;
using Content.Shared.PDA;
using Robust.Shared.Serialization.Manager;

namespace Content.Client.PDA;

public sealed class PdaClientSession(EntityUid owner)
{
    public readonly PdaMenu Menu = new();
    public readonly Dictionary<EntityUid, UIFragment> Programs = new();
    public readonly BoundUserInterface Context = new ProgramContext(owner);
    private sealed class ProgramContext(EntityUid device) : BoundUserInterface(device, PdaUiKey.Key);
    public void Dispose()
    {
        Menu.Dispose();
        foreach (var program in Programs.Values) if (!program.GetUIFragmentRoot().Disposed) program.GetUIFragmentRoot().Dispose();
        Programs.Clear(); Context.Dispose();
    }
}

public sealed partial class PdaSystem : SharedPdaSystem
{
    private readonly Dictionary<(EntityUid Device, EntityUid? Actor), PdaClientSession> _sessions = new();
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PdaComponent, ComponentShutdown>((uid, _, _) =>
        {
            foreach (var key in _sessions.Keys.Where(key => key.Device == uid).ToArray()) { _sessions[key].Dispose(); _sessions.Remove(key); }
        });
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ClearSessions());
    }
    public PdaClientSession GetSession(EntityUid device, EntityUid? actor)
    {
        var key = (device, actor);
        if (_sessions.TryGetValue(key, out var existing)) return existing;
        if (_sessions.Count >= 32)
        {
            var inactive = _sessions.FirstOrDefault(entry => entry.Value.Menu.Parent == null);
            if (inactive.Value != null) { inactive.Value.Dispose(); _sessions.Remove(inactive.Key); }
        }
        return _sessions[key] = new(device);
    }
    public UIFragment? GetProgram(PdaClientSession session, EntityUid program)
    {
        if (session.Programs.TryGetValue(program, out var existing)) return existing;
        if (!TryComp<UIFragmentComponent>(program, out var component) || component.Ui == null) return null;
        var ui = IoCManager.Resolve<ISerializationManager>().CreateCopy(component.Ui, notNullableOverride: true);
        ui.Setup(session.Context, program); session.Programs[program] = ui;
        return ui;
    }
    private void ClearSessions() { foreach (var session in _sessions.Values) session.Dispose(); _sessions.Clear(); }
    public override void Shutdown() { ClearSessions(); base.Shutdown(); }
}
