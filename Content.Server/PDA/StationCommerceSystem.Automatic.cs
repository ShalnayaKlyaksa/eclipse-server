using System.Linq;
using Content.Shared.Corvax.Documents;
using Content.Shared.GameTicking;
using Content.Shared.Lathe;
using Content.Shared.PDA;
using Content.Shared.VendingMachines;
using Robust.Shared.Prototypes;

namespace Content.Server.PDA;

public sealed partial class StationCommerceSystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    private readonly Dictionary<Guid, Receipt> _receipts = new();
    private readonly HashSet<(EntityUid, string)> _merchantKits = new();
    private sealed class Receipt(EntityUid machine, EntityUid domain, string payer, int unitPrice, int units, string note)
    {
        public EntityUid Machine = machine;
        public EntityUid Domain = domain;
        public string Payer = payer;
        public int Price = unitPrice;
        public int Remaining = units;
        public string Note = note;
    }

    private void InitializeAutomaticPayments()
    {
        SubscribeLocalEvent<VendingMachineComponent, BeforePaidVendEvent>(OnVend);
        SubscribeLocalEvent<VendingMachineComponent, ComponentShutdown>((uid, _, _) => RefundMachine(uid));
        SubscribeLocalEvent<DocumentPrinterComponent, ComponentShutdown>((uid, _, _) => RefundMachine(uid));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { _receipts.Clear(); _merchantKits.Clear(); });
    }

    public int DocumentPrice(EntityUid uid) => HasComp<DocumentPrinterComponent>(uid)
        ? Math.Clamp(_prototypes.Index(PersonalEconomyPrototype.StationDefault).DocumentPrice, 0, 1000000) : 0;

    public bool ReservePrint(EntityUid machine, EntityUid? actor, int units, int expectedPrice, out Guid receipt)
    {
        receipt = Guid.Empty;
        var price = DocumentPrice(machine);
        if (price == 0) return true;
        return actor != null && expectedPrice == price && Reserve(machine, actor.Value, price, units,
            Loc.GetString("commerce-print-entry"), out receipt);
    }

    private void OnVend(EntityUid uid, VendingMachineComponent vendor, ref BeforePaidVendEvent args)
    {
        // Physical break-in/EMP ejections are theft, not purchases by an invented payer.
        if (args.Buyer is not { } buyer) return;
        var price = _prototypes.Index(PersonalEconomyPrototype.StationDefault).VendorPrices.GetValueOrDefault(vendor.PackPrototypeId);
        if (price <= 0) return;
        args.Cancelled = !Reserve(uid, buyer, price, 1, Loc.GetString("commerce-vend-entry"), out vendor.PaymentReceipt);
        if (args.Cancelled) _popup.PopupEntity(Loc.GetString("commerce-no-funds"), uid, buyer);
    }

    private bool Reserve(EntityUid machine, EntityUid actor, int price, int units, string note, out Guid id)
    {
        id = Guid.Empty;
        if (price is <= 0 or > 1000000 || units is <= 0 or > 100 || (long) price * units > 1000000 ||
            !TryIdentity(actor, out var card) || _stations.GetOwningStation(machine) != card.Domain ||
            !_cargo.TryGetAccount((card.Domain, null), "Service", out _) ||
            !_bank.ReserveBusiness(card.Domain, card.Username, price * units)) return false;
        id = Guid.NewGuid();
        _receipts[id] = new Receipt(machine, card.Domain, card.Username, price, units, note);
        return true;
    }

    public bool Complete(Guid id)
    {
        if (id == Guid.Empty) return true;
        if (!_receipts.TryGetValue(id, out var receipt) || !_bank.SettleBusiness(receipt.Domain, receipt.Payer,
                receipt.Price, null, "Service", 0, "Service", receipt.Note)) return false;
        if (--receipt.Remaining == 0) _receipts.Remove(id);
        return true;
    }

    public void Refund(Guid id, int units)
    {
        if (!_receipts.TryGetValue(id, out var receipt)) return;
        units = Math.Clamp(units, 0, receipt.Remaining);
        _bank.ReleaseBusiness(receipt.Domain, receipt.Payer, receipt.Price * units);
        receipt.Remaining -= units;
        if (receipt.Remaining == 0) _receipts.Remove(id);
    }

    private void RefundMachine(EntityUid machine)
    {
        foreach (var (id, receipt) in _receipts.Where(pair => pair.Value.Machine == machine).ToArray())
            Refund(id, receipt.Remaining);
    }

    public void ProvisionMerchant(EntityUid domain, string username, EntityUid mob, string job)
    {
        if (job is not ("Bartender" or "Chef") || !_merchantKits.Add((domain, username))) return;
        var uid = Spawn(job == "Bartender" ? "ImperialBarPaymentTerminal" : "ImperialKitchenPaymentTerminal", Transform(mob).Coordinates);
        var terminal = Comp<PaymentTerminalComponent>(uid);
        terminal.Domain = domain; terminal.OwnerUsername = username; terminal.Recipient = username; terminal.Personal = true;
        _hands.TryPickupAnyHand(mob, uid);
    }
}
