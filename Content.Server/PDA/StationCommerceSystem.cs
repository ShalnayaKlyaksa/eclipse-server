using System.Linq;
using Content.Server.Access.Systems;
using Content.Server.Cargo.Systems;
using Content.Server.Research.Components;
using Content.Server.Station.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;

namespace Content.Server.PDA;

/// <summary>Round-local merchant invoices. Possession of the owner's physical ID authorizes management.</summary>
public sealed partial class StationCommerceSystem : EntitySystem
{
    [Dependency] private readonly PdaPersonalServicesSystem _bank = default!;
    [Dependency] private readonly IdCardSystem _cards = default!;
    [Dependency] private readonly StationSystem _stations = default!;
    [Dependency] private readonly CargoSystem _cargo = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedToolSystem _tools = default!;
    private const string GoodsSlot = "commerce-goods";

    public override void Initialize()
    {
        SubscribeLocalEvent<PaymentTerminalComponent, PaymentTerminalMessage>(OnTerminal);
        SubscribeLocalEvent<PaymentTerminalComponent, GotEmaggedEvent>(OnEmag);
        SubscribeLocalEvent<PaymentTerminalComponent, InteractUsingEvent>(OnTool);
        SubscribeLocalEvent<PaymentTerminalComponent, PaymentTerminalHackEvent>(OnHack);
        SubscribeLocalEvent<PaymentTerminalComponent, EntInsertedIntoContainerMessage>((uid, comp, args) => GoodsChanged(comp, args.Container));
        SubscribeLocalEvent<PaymentTerminalComponent, EntRemovedFromContainerMessage>((uid, comp, args) => GoodsChanged(comp, args.Container));
        InitializeAutomaticPayments();
    }

    private static void GoodsChanged(PaymentTerminalComponent terminal, BaseContainer container)
    {
        if (container.ID != GoodsSlot) return;
        terminal.BillOpen = false;
        terminal.Version++;
    }

    public bool TryIdentity(EntityUid actor, out PdaEmployeeCardComponent account)
    {
        account = default!;
        if (!_cards.TryFindIdCard(actor, out var card) || !TryComp<PdaEmployeeCardComponent>(card.Owner, out var found) ||
            !_bank.HasAccount(found.Domain, found.Username)) return false;
        account = found;
        return true;
    }

    private EntityUid? Domain(EntityUid uid, PaymentTerminalComponent terminal)
    {
        // A claimed portable register stays with its original bank, even when carried off-station.
        terminal.Domain ??= _stations.GetOwningStation(uid);
        return terminal.Domain;
    }

    public bool CanManage(EntityUid uid, PaymentTerminalComponent terminal, EntityUid actor) =>
        TryIdentity(actor, out var card) && Domain(uid, terminal) == card.Domain && terminal.OwnerUsername == card.Username;

    public bool Claim(EntityUid uid, PaymentTerminalComponent terminal, EntityUid actor, bool hacked = false)
    {
        if (!TryIdentity(actor, out var card) || Domain(uid, terminal) != card.Domain) return false;
        if (!hacked && terminal.OwnerUsername.Length != 0) return false;
        if (!hacked && terminal.OwnerJob.Length != 0 &&
            Comp<ResearchAccountsComponent>(card.Domain).Credentials[card.Username].JobId != terminal.OwnerJob) return false;
        terminal.OwnerUsername = card.Username;
        terminal.Hacked |= hacked;
        terminal.BillOpen = false;
        terminal.Version++;
        return true;
    }

    private void OnTerminal(EntityUid uid, PaymentTerminalComponent terminal, PaymentTerminalMessage message)
    {
        if (!PaymentTerminalUiKey.Key.Equals(message.UiKey)) return;
        var success = false;
        switch (message.Action)
        {
            case PaymentTerminalAction.Refresh: SendState(uid, terminal, message.Actor); return;
            case PaymentTerminalAction.Claim: success = Claim(uid, terminal, message.Actor); break;
            case PaymentTerminalAction.Configure:
                success = Configure(uid, terminal, message.Actor, message);
                break;
            case PaymentTerminalAction.LoadItem:
                if (CanManage(uid, terminal, message.Actor) && _hands.TryGetActiveItem(message.Actor, out var item))
                {
                    var slot = _containers.EnsureContainer<ContainerSlot>(uid, GoodsSlot);
                    success = _hands.TryDropIntoContainer(message.Actor, item.Value, slot);
                    if (success) { terminal.BillOpen = false; terminal.Version++; }
                }
                break;
            case PaymentTerminalAction.ReturnItem:
                if (CanManage(uid, terminal, message.Actor) && Goods(uid) is { } goods && _containers.TryRemoveFromContainer(goods))
                {
                    _hands.PickupOrDrop(message.Actor, goods);
                    terminal.BillOpen = false; terminal.Version++; success = true;
                }
                break;
            case PaymentTerminalAction.Pay: success = PayInvoice(uid, terminal, message.Actor, message.Version); break;
        }
        _ui.ServerSendUiMessage(uid, PaymentTerminalUiKey.Key,
            new PaymentTerminalFeedback(success ? "commerce-success" : "commerce-failed"), message.Actor);
        SendState(uid, terminal, message.Actor);
    }

    public bool Configure(EntityUid uid, PaymentTerminalComponent terminal, EntityUid actor, PaymentTerminalMessage message)
    {
        if (!CanManage(uid, terminal, actor) || message.Version != terminal.Version ||
            message.Price is < 1 or > 1000000 || message.Commission is < 0 or > 100 ||
            message.Recipient == null || message.Recipient.Length > 24 || message.Description == null || message.Description.Length > 80 ||
            message.Department == null || message.CommissionDepartment == null ||
            message.Department.Length > 32 || message.CommissionDepartment.Length > 32 || Domain(uid, terminal) is not { } domain) return false;
        if (!_cargo.TryGetAccount((domain, null), message.Department, out _) ||
            !_cargo.TryGetAccount((domain, null), message.CommissionDepartment, out _)) return false;
        var recipient = PdaPersonalServicesSystem.ResolveId(Comp<ResearchAccountsComponent>(domain), message.Recipient);
        if (message.Personal && recipient == null) return false;
        terminal.Personal = message.Personal;
        terminal.Recipient = recipient ?? "";
        terminal.Department = message.Department;
        terminal.Price = message.Price;
        terminal.Commission = message.Commission;
        terminal.CommissionDepartment = message.CommissionDepartment;
        terminal.Description = message.Description.Trim();
        terminal.BillOpen = true;
        terminal.Version++;
        return true;
    }

    public bool PayInvoice(EntityUid uid, PaymentTerminalComponent terminal, EntityUid actor, int version)
    {
        if (!terminal.BillOpen || version != terminal.Version || !TryIdentity(actor, out var card) ||
            Domain(uid, terminal) != card.Domain) return false;
        var goods = Goods(uid);
        // Remove into server custody before moving money; restore on failure.
        if (goods != null && !_containers.TryRemoveFromContainer(goods.Value)) return false;
        if (!_bank.PayBusiness(card.Domain, card.Username, terminal.Price, terminal.Personal ? terminal.Recipient : null,
                terminal.Department, terminal.Commission, terminal.CommissionDepartment,
                Loc.GetString("commerce-purchase-entry", ("description", terminal.Description))))
        {
            if (goods != null) _containers.Insert(goods.Value, _containers.EnsureContainer<ContainerSlot>(uid, GoodsSlot));
            return false;
        }
        terminal.BillOpen = false;
        terminal.Version++;
        if (goods != null) _hands.PickupOrDrop(actor, goods.Value);
        return true;
    }

    private EntityUid? Goods(EntityUid uid) => _containers.TryGetContainer(uid, GoodsSlot, out var container) && container.ContainedEntities.Count > 0
        ? container.ContainedEntities[0] : null;

    private void SendState(EntityUid uid, PaymentTerminalComponent terminal, EntityUid actor)
    {
        var state = new PaymentTerminalState
        {
            CanManage = CanManage(uid, terminal, actor), CanClaim = terminal.OwnerUsername.Length == 0,
            Personal = terminal.Personal, Department = terminal.Department, Price = terminal.Price,
            Commission = terminal.Commission, CommissionDepartment = terminal.CommissionDepartment,
            Description = terminal.Description, BillOpen = terminal.BillOpen, Version = terminal.Version,
            Hacked = terminal.Hacked, Item = Goods(uid) is { } goods ? Name(goods) : "",
        };
        if (Domain(uid, terminal) is { } domain)
        {
            state.Departments = _cargo.GetAccounts((domain, null)).Keys.Select(id => id.Id).Order().ToArray();
            if (TryComp<ResearchAccountsComponent>(domain, out var registry))
            {
                state.Owner = registry.Credentials.GetValueOrDefault(terminal.OwnerUsername)?.PublicId ?? "";
                state.Recipient = registry.Credentials.GetValueOrDefault(terminal.Recipient)?.PublicId ?? "";
            }
        }
        _ui.ServerSendUiMessage(uid, PaymentTerminalUiKey.Key, state, actor);
    }

    private void OnEmag(EntityUid uid, PaymentTerminalComponent terminal, ref GotEmaggedEvent args)
    {
        if ((args.Type & EmagType.Interaction) == 0) return;
        args.Handled = Claim(uid, terminal, args.UserUid, true);
        args.Repeatable = true;
    }

    private void OnTool(EntityUid uid, PaymentTerminalComponent terminal, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<ToolComponent>(args.Used, out var tool)) return;
        if (_tools.HasQuality(args.Used, "Screwing", tool))
        {
            terminal.PanelOpen = !terminal.PanelOpen;
            _popup.PopupEntity(Loc.GetString(terminal.PanelOpen ? "commerce-panel-open" : "commerce-panel-closed"), uid, args.User);
            args.Handled = true;
        }
        else if (terminal.PanelOpen && _tools.HasQuality(args.Used, "Pulsing", tool) && TryIdentity(args.User, out _))
            args.Handled = _tools.UseTool(args.Used, args.User, uid, 15f, "Pulsing", new PaymentTerminalHackEvent());
    }

    private void OnHack(EntityUid uid, PaymentTerminalComponent terminal, PaymentTerminalHackEvent args)
    {
        if (args.Cancelled || args.Handled || !terminal.PanelOpen) return;
        args.Handled = Claim(uid, terminal, args.User, true);
        _popup.PopupEntity(Loc.GetString(args.Handled ? "commerce-hacked" : "commerce-failed"), uid, args.User);
    }
}
