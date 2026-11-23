using System.Linq;
using System.Numerics;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client.PDA;

public sealed partial class PdaMenu
{
    private sealed record RunningApp(string Id, string Title, string Icon, Action Activate, EntityUid? Program = null);
    private readonly List<RunningApp> _running = new();
    private readonly Dictionary<EntityUid, Control> _programControls = new();
    private string? _currentApp;
    private EntityUid? _shownProgram;
    public int RunningAppCount => _running.Count;

    private bool EnsureRunning(string id, string title, string icon, Action activate, EntityUid? program = null)
    {
        if (_running.Any(app => app.Id == id)) return true;
        if (_running.Count >= 6)
        {
            TaskLabel.Text = Loc.GetString("pda-task-limit"); TaskButton.Visible = false; Taskbar.Visible = true;
            return false;
        }
        _running.Add(new(id, title, icon, activate, program));
        return true;
    }

    private void RefreshTaskStrip()
    {
        RunningTasks.RemoveAllChildren();
        foreach (var app in _running)
        {
            var button = new PdaNavigationButton { SetSize = new Vector2(28, 26), ToolTip = app.Title + "\n" + Loc.GetString("pda-task-close-hint"), IsCurrent = _currentApp == app.Id && _currentView is BuiltinContentView or ProgramContentView or PersonalView };
            button.IconTexture = FieldIcon(app.Icon);
            button.OnPressed += _ => app.Activate();
            button.OnKeyBindDown += args =>
            {
                if (args.Function != EngineKeyFunctions.UIRightClick) return;
                var popup = new Popup { Stylesheet = Stylesheet };
                var close = PdaAppUi.Action(Loc.GetString("pda-task-close"));
                close.OnPressed += _ => { popup.Close(); popup.Dispose(); CloseTask(app.Id); };
                popup.AddChild(PdaAppUi.Card(close)); UserInterfaceManager.ModalRoot.AddChild(popup);
                popup.Open(UIBox2.FromDimensions(args.PointerLocation.Position / UIScale, new Vector2(160, 1)));
                args.Handle();
            };
            RunningTasks.AddChild(button);
        }
    }

    private void OpenCartridgeApp(EntityUid uid, string title)
    {
        var id = "program:" + uid;
        var program = _apps.FirstOrDefault(app => app.Cartridge == uid);
        var icon = PdaDesktopIcon.IconName(program?.Id ?? "") ?? "apps";
        if (!EnsureRunning(id, title, icon, () => OpenCartridgeApp(uid, title), uid)) return;
        _currentApp = id; _shownProgram = uid;
        foreach (var (programUid, control) in _programControls) control.Visible = programUid == uid;
        _programTitle = title; ShowPage(ProgramContentView, title);
        OnProgramItemPressed?.Invoke(uid);
    }

    public void AttachProgram(EntityUid uid, Control control, string title)
    {
        _programControls[uid] = control;
        if (control.Parent != ProgramView) { control.Orphan(); ProgramView.AddChild(control); }
        control.Visible = _shownProgram == uid;
        if (_shownProgram == uid && _currentView == ProgramContentView) ShowPage(ProgramContentView, title);
    }

    public void HideProgram(Control control) => control.Visible = false;

    private void OpenPersonal()
    {
        var title = Loc.GetString("pda-os-app-personal");
        if (!EnsureRunning("personal", title, "personal", OpenPersonal)) return;
        _currentApp = "personal"; ShowPage(PersonalView, title);
    }

    private void CloseTask(string id)
    {
        var app = _running.FirstOrDefault(app => app.Id == id);
        if (app == null) return;
        _running.Remove(app);
        if (app.Program is { } program)
        {
            if (_shownProgram == program) { _shownProgram = null; OnCartridgeClosed?.Invoke(); }
            if (_programControls.TryGetValue(program, out var control)) control.Visible = false;
        }
        else if (_builtins.Remove(id, out var content))
        {
            BuiltinView.RemoveChild(content); content.Dispose();
            if (_currentBuiltin == id) _currentBuiltin = null;
        }
        if (_currentApp == id) { _currentApp = null; ToHomeScreen(); }
        RefreshTaskStrip();
    }

    public void DisconnectUiHandlers()
    {
        OnProgramItemPressed = null; OnUninstallButtonPressed = null; OnInstallButtonPressed = null;
        OnDesktopMessage = null; OnServicesMessage = null; OnCartridgeClosed = null; OnHardwareAction = null;
    }
}
