using Content.Server.Cargo.Systems;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.PDA;
using Robust.Shared.Prototypes;

namespace Content.Server.PDA;

public sealed partial class PdaPersonalServicesSystem
{
    public int CreditCapacity(EntityUid domain, string id) => int.MaxValue - Account(domain, id).Credits - Account(domain, id).ReservedCredits;
    /// <summary>Prevalidate the entire split, combine coincident destinations, then commit on the game thread.</summary>
    public bool PayBusiness(EntityUid domain, string payer, int price, string? recipient, string department,
        int commission, string commissionDepartment, string note)
    {
        if (!HasAccount(domain, payer) || price is <= 0 or > 1000000 || commission is < 0 or > 100 ||
            (recipient != null && !HasAccount(domain, recipient))) return false;
        var cargo = EntityManager.System<CargoSystem>();
        var fee = (int) ((long) price * commission / 100);
        var personal = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [payer] = -price };
        var departments = new Dictionary<ProtoId<CargoAccountPrototype>, int>();
        if (recipient != null) personal[recipient] = personal.GetValueOrDefault(recipient) + price - fee;
        else departments[department] = price - fee;
        departments[commissionDepartment] = departments.GetValueOrDefault(commissionDepartment) + fee;
        // A self-purchase still requires the full displayed amount up front.
        if (Account(domain, payer).Credits < price) return false;
        foreach (var (id, delta) in personal)
        {
            var account = Account(domain, id);
            if ((long) account.Credits + delta < 0 || (long) account.Credits + account.ReservedCredits + delta > int.MaxValue) return false;
        }
        foreach (var (id, delta) in departments)
            if (!cargo.TryGetAccount((domain, null), id, out var balance) || (long) balance + delta > int.MaxValue) return false;
        // No yields between checking balances and applying deltas.
        foreach (var (id, delta) in personal)
            Account(domain, id).Credits += delta;
        foreach (var (id, delta) in departments)
            if (delta != 0) cargo.TryAdjustBankAccount((domain, null), id, delta);
        var service = Comp<PdaPersonalServicesComponent>(domain);
        var transaction = ++service.NextTransaction;
        foreach (var (id, delta) in personal)
        {
            var counterparty = id.Equals(payer, StringComparison.OrdinalIgnoreCase) ? recipient ?? department : payer;
            Append(Account(domain, id).Transactions, new PdaBankEntry(transaction, counterparty, delta,
                note[..Math.Min(80, note.Length)], (int) _timing.CurTime.TotalSeconds), 40);
            Push(domain, id);
        }
        if (recipient != null && !recipient.Equals(payer, StringComparison.OrdinalIgnoreCase))
            Notify(domain, recipient, "pda-os-app-bank", "pda-services-transfer-received");
        return true;
    }

    /// <summary>Refund capacity is reserved too, so later transfers cannot make an escrow refund overflow.</summary>
    public bool ReserveBusiness(EntityUid domain, string payer, int amount)
    {
        if (!HasAccount(domain, payer) || amount <= 0 || Account(domain, payer).Credits < amount) return false;
        var account = Account(domain, payer);
        if (!AdjustCredits(domain, payer, -amount, Loc.GetString("commerce-reserve"))) return false;
        account.ReservedCredits += amount;
        return true;
    }

    public void ReleaseBusiness(EntityUid domain, string payer, int amount)
    {
        if (!HasAccount(domain, payer) || amount <= 0) return;
        var account = Account(domain, payer);
        if (amount > account.ReservedCredits) return;
        account.ReservedCredits -= amount;
        AdjustCredits(domain, payer, amount, Loc.GetString("commerce-refund"));
    }

    public bool SettleBusiness(EntityUid domain, string payer, int amount, string? recipient, string department,
        int commission, string commissionDepartment, string note)
    {
        var account = Account(domain, payer);
        if (amount <= 0 || account.ReservedCredits < amount) return false;
        // Make this unit available for the same validated split routine; restore it if settlement fails.
        account.ReservedCredits -= amount;
        account.Credits += amount;
        if (PayBusiness(domain, payer, amount, recipient, department, commission, commissionDepartment, note)) return true;
        account.Credits -= amount;
        account.ReservedCredits += amount;
        return false;
    }
}
