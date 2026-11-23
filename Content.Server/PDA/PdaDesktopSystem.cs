using Content.Server.Eclipse.Economy;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared.GameTicking;
using Content.Shared.PDA;
using Robust.Server.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.PDA;

/// <summary>Only the UI actor's account can pay for or change their desktop.</summary>
public sealed partial class PdaDesktopSystem : EntitySystem
{
    [Dependency] private EclipseCurrencyManager _currency = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PdaComponent, PdaDesktopRequestMessage>((uid, _, msg) => SendProfile(uid, msg));
        SubscribeLocalEvent<PdaComponent, PdaDesktopSaveMessage>(OnSave);
        SubscribeNetworkEvent<PdaCosmeticsRequestEvent>(OnRequestCatalog);
        SubscribeNetworkEvent<PdaCosmeticBuyEvent>(OnBuy);
        SubscribeNetworkEvent<PdaRequestPublicIdEvent>(OnRequestPublicId);
        SubscribeNetworkEvent<PdaSetPublicIdEvent>(OnSetPublicId);
    }

    private void OnRequestPublicId(PdaRequestPublicIdEvent message, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        SendPublicId(session, message.Slot, CanEditId(session.UserId, message.Slot));
    }

    private void OnSetPublicId(PdaSetPublicIdEvent message, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        var canEdit = CanEditId(session.UserId, message.Slot);
        var id = message.Id?.Trim().ToLowerInvariant() ?? string.Empty;
        var saved = canEdit && _currency.SetPdaPublicId(session.UserId, message.Slot, id);
        SendPublicId(session, message.Slot, canEdit, saved ? "pda-services-id-saved" : "pda-services-id-failed");
    }

    private bool CanEditId(NetUserId user, int slot) =>
        _ticker.PlayerGameStatuses.TryGetValue(user, out var status) && status != PlayerGameStatus.JoinedGame &&
        _preferences.GetPreferencesOrNull(user)?.Characters.ContainsKey(slot) == true;

    private void SendPublicId(ICommonSession session, int slot, bool canEdit, string? feedback = null)
    {
        var id = canEdit ? _currency.GetPdaPublicId(session.UserId, slot) : string.Empty;
        RaiseNetworkEvent(new PdaPublicIdStateEvent(slot, id, canEdit, feedback), session.Channel);
    }
    private void OnSave(EntityUid uid, PdaComponent component, PdaDesktopSaveMessage msg)
    {
        if (!PdaUiKey.Key.Equals(msg.UiKey) || !TryComp<ActorComponent>(msg.Actor, out var actor))
            return;
        var success = _currency.SaveDesktop(actor.PlayerSession.UserId, msg.Profile);
        SendProfile(uid, msg, success ? null : "pda-os-invalid-profile");
    }

    private void OnRequestCatalog(PdaCosmeticsRequestEvent msg, EntitySessionEventArgs args) => SendCatalog(args.SenderSession);

    private void OnBuy(PdaCosmeticBuyEvent msg, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        // Cosmetics are sold in the lobby only; a forged in-round request cannot spend funds.
        var inLobby = _ticker.PlayerGameStatuses.TryGetValue(session.UserId, out var status) &&
                      status != Content.Shared.GameTicking.PlayerGameStatus.JoinedGame;
        var success = inLobby && _currency.TryBuyDesktopCosmetic(session.UserId, msg.Id, msg.Shards);
        SendCatalog(session, success ? "pda-os-purchased" : "pda-os-purchase-failed");
    }

    private void SendCatalog(ICommonSession session, string? feedback = null)
    {
        var (merits, shards) = _currency.GetBalance(session.UserId);
        RaiseNetworkEvent(new PdaCosmeticsStateEvent(_currency.GetDesktop(session.UserId), merits, shards, feedback), session.Channel);
    }

    private void SendProfile(EntityUid uid, BoundUserInterfaceMessage msg, string? feedback = null)
    {
        if (!PdaUiKey.Key.Equals(msg.UiKey) || !TryComp<ActorComponent>(msg.Actor, out var actor))
            return;
        var user = actor.PlayerSession.UserId;
        var (merits, shards) = _currency.GetBalance(user);
        _ui.ServerSendUiMessage(uid, PdaUiKey.Key,
            new PdaDesktopProfileMessage(_currency.GetDesktop(user), merits, shards, feedback), msg.Actor);
    }
}
