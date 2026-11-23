using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.PDA;

public sealed partial class PdaMenu
{
    private sealed record DesktopApp(string Id, string Title, string Glyph, Color Tint, Action Open, SpriteSpecifier? Icon = null, EntityUid? Cartridge = null, InstallationStatus Status = InstallationStatus.Readonly);
    private readonly List<DesktopApp> _apps = new();
    private readonly List<PdaDesktopIcon> _desktopIcons = new();
    private Dictionary<string, int> _desktopCells = new();
    private List<(EntityUid, CartridgeComponent)> _cartridges = new();
    private PdaDesktopProfile _profile = new();
    private bool _profileReady;
    private Popup? _desktopPopup;
    private LineEdit? _appSearch;
    private readonly Dictionary<string, Control> _builtins = new();
    private string? _currentBuiltin;
    private PdaBankControl? _bank;
    private PdaMessengerControl? _messenger;
    private PdaMessengerStateMessage _messengerState = new();
    private PdaServicesStateMessage _services = new();
    public event Action<BoundUserInterfaceMessage>? OnServicesMessage;

    public void ApplyServices(PdaServicesStateMessage state)
    {
        _services = state;
        Desktop.PublicId = state.Id;
        if (_bank is { Disposed: false }) _bank.Update(state);
    }
    public void ApplyMessenger(PdaMessengerStateMessage state)
    {
        _messengerState = state;
        foreach (var icon in _desktopIcons) if (icon.AppId == "messenger") icon.Unread = state.Chats.Any(chat => chat.Unread > 0);
        if (_messenger is { Disposed: false }) _messenger.Update(state);
    }
    public bool IsCartridgeView => _currentView == ProgramContentView;

    private void InitializeDesktop()
    {
        Desktop.Arranging += ArrangeDesktop;
        Desktop.ContextRequested += position => OpenDesktopMenu(position);
        UpdateAvailablePrograms(_cartridges);
    }

    public void ApplyDesktopProfile(PdaDesktopProfileMessage message)
    {
        var appearanceChanged = !_profileReady || _profile.Wallpaper != message.Profile.Wallpaper || !_profile.Owned.SequenceEqual(message.Profile.Owned) ||
            _profile.Card != message.Profile.Card || _profile.CardText != message.Profile.CardText || _profile.Photo != message.Profile.Photo || _profile.Decor != message.Profile.Decor || _profile.CustomWallpaper != message.Profile.CustomWallpaper;
        _profile = message.Profile;
        if (_profile.Positions.Remove("max", out var previousCell)) _profile.Positions.TryAdd("messenger", previousCell);
        _profileReady = true;
        Desktop.Wallpaper = _profile.Wallpaper;
        Desktop.Card = _profile.Card;
        Desktop.CardText = _profile.CardText;
        Desktop.Decor = _profile.Decor;
        Desktop.Photo = _profile.Photo;
        Desktop.CustomWallpaper = _profile.CustomWallpaper;
        RebuildDesktop();
        LoadPersonalImage();
        if (appearanceChanged && !_appearanceDirty)
        {
            _appearanceBuilt = false;
            if (_currentView == AppearanceView) OpenAppearance();
        }
        if (message.Feedback != null)
        {
            TaskLabel.Text = Loc.GetString(message.Feedback);
            Taskbar.Visible = true;
        }
    }

    public void UpdateAvailablePrograms(List<(EntityUid, CartridgeComponent)> programs)
    {
        _cartridges = programs;
        _apps.Clear();
        AddBuiltin("personal", "◎", "#8DBFDC", OpenPersonal);
        AddBuiltin("bank", "₡", "#E2C58A", OpenBank);
        AddBuiltin("messenger", "", "#8BB9C8", OpenMessenger);
        AddBuiltin("flappy", "»", "#98DCAE", () => OpenBuiltin("flappy", new PocketFlappyControl()));
        AddBuiltin("blocks", "▦", "#BFA6E5", () => OpenBuiltin("blocks", new PocketBlockinatorControl()));
        AddBuiltin("calculator", "+", "#8DAFE0", OpenCalculator);
        AddBuiltin("appearance", "◈", "#E4AAC7", OpenAppearance);
        AddBuiltin("apps", "", "#91B7D3", () => ShowPage(ProgramListView, Loc.GetString("comp-pda-io-program-list-button")));
        AddBuiltin("settings", "", "#91B7D3", OpenSettings);
        foreach (var (uid, component) in programs)
        {
            var title = Loc.GetString(component.ProgramName);
            _apps.Add(new DesktopApp("cartridge:" + component.ProgramName, title, "□", Color.FromHex("#91B7D3"),
                () => OpenCartridgeApp(uid, title), component.Icon, uid, component.InstallationStatus));
        }
        RebuildDesktop();
        BuildLauncher();
        OnServicesMessage?.Invoke(new PdaServicesRequestMessage(_services.Peer));
    }

    private void AddBuiltin(string id, string glyph, string tint, Action open) =>
        _apps.Add(new DesktopApp(id, Loc.GetString("pda-os-app-" + id), glyph, Color.FromHex(tint), open));

    private void RebuildDesktop()
    {
        Desktop.RemoveAllChildren();
        _desktopIcons.Clear();
        foreach (var app in _apps)
        {
            if (_profile.Positions.GetValueOrDefault(app.Id) == -1) continue;
            var icon = new PdaDesktopIcon(app.Id, app.Title, app.Tint, app.Icon);
            if (app.Id == "messenger") icon.Unread = _messengerState.Chats.Any(chat => chat.Unread > 0);
            icon.OpenRequested += app.Open;
            icon.ContextRequested += position => OpenDesktopMenu(position, app);
            icon.MoveFinished += () => FinishIconMove(icon);
            Desktop.AddChild(icon);
            _desktopIcons.Add(icon);
        }
        ArrangeDesktop(Desktop.Size);
    }

    private (int Columns, int Rows) DesktopGrid(Vector2 size) => (Math.Clamp((int) ((size.X - 236) / PdaDesktopIcon.CellSize.X), 1, 8), Math.Clamp((int) ((size.Y - 20) / PdaDesktopIcon.CellSize.Y), 1, 8));

    private void ArrangeDesktop(Vector2 size)
    {
        if (size.X <= 0 || size.Y <= 0) return;
        var (columns, rows) = DesktopGrid(size);
        _desktopCells = PdaDesktopLayout.Arrange(_desktopIcons.Select(icon => icon.AppId), _profile.Positions, columns, rows);
        foreach (var icon in _desktopIcons)
        {
            icon.Visible = _desktopCells.TryGetValue(icon.AppId, out var cell);
            if (!icon.Visible) continue;
            _profile.Positions.TryAdd(icon.AppId, cell);
            // A layout pass may run on every pointer move. Never snap the dragged icon back.
            if (!icon.IsDragging)
                icon.Place(new Vector2(14 + cell % PdaDesktopLayout.Stride * PdaDesktopIcon.CellSize.X, 14 + cell / PdaDesktopLayout.Stride * PdaDesktopIcon.CellSize.Y));
        }
    }

    private void FinishIconMove(PdaDesktopIcon icon)
    {
        var (columns, rows) = DesktopGrid(Desktop.Size);
        var x = Math.Clamp((int) MathF.Round((icon.Position.X - 14) / PdaDesktopIcon.CellSize.X), 0, columns - 1);
        var y = Math.Clamp((int) MathF.Round((icon.Position.Y - 14) / PdaDesktopIcon.CellSize.Y), 0, rows - 1);
        var cell = y * PdaDesktopLayout.Stride + x;
        PdaDesktopLayout.Move(_profile.Positions, _desktopCells, icon.AppId, cell);
        ArrangeDesktop(Desktop.Size);
        SaveDesktop();
    }

    private void OpenDesktopMenu(Vector2 position, DesktopApp? app = null)
    {
        _desktopPopup?.Close();
        _desktopPopup?.Dispose();
        var popup = new Popup { Stylesheet = Stylesheet, MinWidth = 220 };
        _desktopPopup = popup;
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 3, Margin = new Thickness(5) };
        popup.AddChild(new PanelContainer { PanelOverride = PdaTheme.Box("#172531", "#4D6979", 4), Children = { column } });
        void Item(string key, Action action)
        {
            var button = new Button { Text = Loc.GetString(key), HorizontalExpand = true };
            button.OnPressed += _ => { popup.Close(); action(); };
            column.AddChild(button);
        }
        if (app != null)
        {
            Item("pda-os-open", app.Open);
            Item("pda-os-hide-shortcut", () => { _profile.Positions[app.Id] = -1; RebuildDesktop(); SaveDesktop(); });
            if (app.Cartridge is { } uid && app.Status != InstallationStatus.Readonly)
                Item(app.Status == InstallationStatus.Cartridge ? "cartridge-bound-user-interface-install-button" : "cartridge-bound-user-interface-uninstall-button",
                    () => { if (app.Status == InstallationStatus.Cartridge) OnInstallButtonPressed?.Invoke(uid); else OnUninstallButtonPressed?.Invoke(uid); });
        }
        Item("pda-os-arrange", () => { _profile.Positions.Clear(); RebuildDesktop(); SaveDesktop(); });
        Item("pda-os-all-programs", () => ShowPage(ProgramListView, Loc.GetString("comp-pda-io-program-list-button")));
        Item("pda-max-copy-id", () => _clipboard.SetText(_services.Id));
        Item("comp-pda-io-settings-button", OpenSettings);
        Item("pda-os-personalize", OpenAppearance);
        UserInterfaceManager.ModalRoot.AddChild(popup);
        popup.Open(UIBox2.FromDimensions(position, new Vector2(220, 1)));
    }

    private void BuildLauncher()
    {
        var filter = _appSearch?.Text ?? "";
        ProgramList.RemoveAllChildren();
        _appSearch = new LineEdit { PlaceHolder = Loc.GetString("pda-os-search"), Text = filter };
        ProgramList.AddChild(_appSearch);
        var list = new GridContainer { Columns = 2, HorizontalExpand = true };
        ProgramList.AddChild(list);
        void Refresh()
        {
            list.RemoveAllChildren();
            foreach (var app in _apps.Where(a => a.Title.Contains(_appSearch.Text, StringComparison.CurrentCultureIgnoreCase)))
            {
                var row = new BoxContainer { SeparationOverride = 4, HorizontalExpand = true };
                var button = new ContainerButton { StyleClasses = { "button" }, HorizontalExpand = true };
                var content = new BoxContainer { SeparationOverride = 10 };
                content.AddChild(new TextureRect
                {
                    TexturePath = "/Textures/_Eclipse/Interface/PDA/Icons/Field/" + (PdaDesktopIcon.IconName(app.Id) ?? "fallback") + ".png",
                    SetSize = new Vector2(24), CanShrink = true, Stretch = TextureRect.StretchMode.KeepAspectCentered,
                });
                content.AddChild(new Label { Text = app.Title, ClipText = true, HorizontalExpand = true, VerticalAlignment = VAlignment.Center });
                button.ToolTip = app.Title;
                button.AddChild(content);
                button.OnPressed += _ => app.Open();
                row.AddChild(button);
                if (_profile.Positions.GetValueOrDefault(app.Id) == -1)
                {
                    var restore = new Button { Text = Loc.GetString("pda-os-pin") };
                    restore.OnPressed += _ => { _profile.Positions.Remove(app.Id); RebuildDesktop(); SaveDesktop(); Refresh(); };
                    row.AddChild(restore);
                }
                list.AddChild(row);
            }
        }
        _appSearch.OnTextChanged += _ => Refresh();
        Refresh();
    }

    private void SaveDesktop()
    {
        if (_profileReady) OnDesktopMessage?.Invoke(new PdaDesktopSaveMessage(_profile));
    }

    private bool OpenBuiltin(string id, Control content)
    {
        var title = Loc.GetString("pda-os-app-" + id);
        if (!EnsureRunning(id, title, PdaDesktopIcon.IconName(id) ?? "apps", () => OpenBuiltin(id, content))) { content.Dispose(); return false; }
        _currentApp = id;
        if (_builtins.TryGetValue(id, out var existing)) { if (content != existing) content.Dispose(); }
        else { existing = content; _builtins.Add(id, content); BuiltinView.AddChild(content); }
        foreach (var child in BuiltinView.Children) child.Visible = child == existing;
        _currentBuiltin = id;
        ShowPage(BuiltinContentView, Loc.GetString("pda-os-app-" + id));
        return true;
    }

    private void CloseCurrentApp()
    {
        if (_currentApp != null && _currentView is BuiltinContentView or ProgramContentView or PersonalView) CloseTask(_currentApp);
        else ToHomeScreen();
    }
    private void OpenBank()
    {
        if (_bank is not { Disposed: false })
        {
            _bank = new PdaBankControl();
            _bank.Send += message => OnServicesMessage?.Invoke(message);
        }
        _bank.Update(_services);
        if (!OpenBuiltin("bank", _bank)) return;
        OnServicesMessage?.Invoke(new PdaServicesRequestMessage());
    }

    private void OpenMessenger()
    {
        if (_messenger is not { Disposed: false })
        {
            _messenger = new PdaMessengerControl();
            _messenger.Send += message => OnServicesMessage?.Invoke(message);
        }
        _messenger.Update(_messengerState);
        if (!OpenBuiltin("messenger", _messenger)) return;
        OnServicesMessage?.Invoke(new PdaMessengerRequestMessage(_messenger.SelectedChat.Length == 0 ? null : _messenger.SelectedChat));
    }
    private void OpenCalculator() => OpenBuiltin("calculator", new PdaCalculatorControl());
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _desktopPopup?.Dispose(); _personalImage?.Dispose(); }
        base.Dispose(disposing);
    }
}


