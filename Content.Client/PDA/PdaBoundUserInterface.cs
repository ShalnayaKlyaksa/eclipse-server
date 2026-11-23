using Content.Client.CartridgeLoader;
using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.PDA;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.PDA;

[UsedImplicitly]
public sealed class PdaBoundUserInterface(EntityUid owner, Enum uiKey) : CartridgeLoaderBoundUserInterface(owner, uiKey)
{
    private PdaClientSession? _session;
    private PdaMenu? _menu;
    protected override bool PreserveCartridgeControls => true;
    protected override void Open()
    {
        base.Open();
        _session = EntMan.System<PdaSystem>().GetSession(Owner, PlayerManager.LocalEntity);
        _menu = _session.Menu;
        _menu.OnClose += Close;
        _menu.OnHardwareAction += Hardware;
        _menu.OnDesktopMessage += SendMessage;
        _menu.OnServicesMessage += SendMessage;
        _menu.OnProgramItemPressed += ActivateCartridge;
        _menu.OnInstallButtonPressed += InstallCartridge;
        _menu.OnUninstallButtonPressed += UninstallCartridge;
        _menu.OnCartridgeClosed += DeactivateActiveCartridge;
        var system = EntMan.System<UserInterfaceSystem>(); system.RegisterControl(this, _menu);
        if (system.TryGetPosition(Owner, UiKey, out var position)) _menu.Open(position); else _menu.OpenCenteredLeft();
        if (EntMan.TryGetComponent<PdaBorderColorComponent>(Owner, out var border))
        { _menu.BorderColor = border.BorderColor; _menu.AccentHColor = border.AccentHColor; _menu.AccentVColor = border.AccentVColor; }
        SendMessage(new PdaDesktopRequestMessage()); SendMessage(new PdaServicesRequestMessage()); SendMessage(new PdaMessengerRequestMessage(markRead: false));
    }
    private void Hardware(string action)
    {
        switch (action)
        {
            case "light": SendMessage(new PdaToggleFlashlightMessage()); break;
            case "id": SendPredictedMessage(new ItemSlotButtonPressedEvent(PdaComponent.PdaIdSlotId)); break;
            case "pen": SendPredictedMessage(new ItemSlotButtonPressedEvent(PdaComponent.PdaPenSlotId)); break;
            case "pai": SendPredictedMessage(new ItemSlotButtonPressedEvent(PdaComponent.PdaPaiSlotId)); break;
            case "music": SendMessage(new PdaShowMusicMessage()); break;
            case "ringtone": SendMessage(new PdaShowRingtoneMessage()); break;
            case "uplink": SendMessage(new PdaShowUplinkMessage()); break;
            case "lock": SendMessage(new PdaLockUplinkMessage()); break;
        }
    }
    protected override UIFragment? RetrieveCartridgeUI(EntityUid? uid) => uid is { } program && _session != null ? EntMan.System<PdaSystem>().GetProgram(_session, program) : null;
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state); if (state is PdaUpdateState update) _menu?.UpdateState(update);
    }
    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is PdaDesktopProfileMessage profile) _menu?.ApplyDesktopProfile(profile);
        if (message is PdaServicesStateMessage services) _menu?.ApplyServices(services);
        if (message is PdaMessengerStateMessage messenger) _menu?.ApplyMessenger(messenger);
    }
    protected override void AttachCartridgeUI(Control control, string? title)
    { if (ActiveProgram is { } uid) _menu?.AttachProgram(uid, control, title ?? Loc.GetString("comp-pda-io-program-fallback-title")); }
    protected override void DetachCartridgeUI(Control control) => _menu?.HideProgram(control);
    protected override void UpdateAvailablePrograms(List<(EntityUid, CartridgeComponent)> programs) => _menu?.UpdateAvailablePrograms(programs);
    protected override void Dispose(bool disposing)
    {
        if (disposing && _menu != null)
        { _menu.OnClose -= Close; _menu.DisconnectUiHandlers(); _menu.Close(); _menu = null; }
        base.Dispose(disposing);
    }
}
