using System;
using Content.Shared.Eclipse.Economy;

namespace Content.Client.Eclipse.Economy;

/// <summary>
/// Caches the player's authoritative Eclipse currency balance sent by the server so the lobby (and
/// future store UI) can display it. Raises <see cref="BalanceChanged"/> whenever it updates.
/// </summary>
public sealed class EclipseCurrencyClientSystem : EntitySystem
{
    public int Merits { get; private set; }
    public int Shards { get; private set; }

    /// <summary>True once the server has sent at least one balance for this session.</summary>
    public bool HasBalance { get; private set; }

    public event Action? BalanceChanged;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<EclipseCurrencyBalanceEvent>(OnBalance);
    }

    private void OnBalance(EclipseCurrencyBalanceEvent ev)
    {
        Merits = ev.Merits;
        Shards = ev.Shards;
        HasBalance = true;
        BalanceChanged?.Invoke();
    }
}
