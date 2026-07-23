using Robust.Shared.Serialization;

namespace Content.Shared.Eclipse.Economy;

/// <summary>
/// Sent to a client whenever their persistent Eclipse currency balance changes (or on connect),
/// so the lobby and future store UI can show the authoritative, spendable balance.
/// </summary>
[Serializable, NetSerializable]
public sealed class EclipseCurrencyBalanceEvent : EntityEventArgs
{
    public int Merits;
    public int Shards;

    public EclipseCurrencyBalanceEvent()
    {
    }

    public EclipseCurrencyBalanceEvent(int merits, int shards)
    {
        Merits = merits;
        Shards = shards;
    }
}
