using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared.Eclipse.Economy;
using Content.Shared.Eclipse.Progression;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.Eclipse.Economy;

/// <summary>
/// Foundation for spendable Eclipse currency (Merits and Shards).
///
/// Balances are stored per account, persisted to a JSON file, and synced to the client for display.
/// This is deliberately a self-contained scaffold: the persistence backend (currently a UserData JSON
/// file) is isolated behind <see cref="Load"/>/<see cref="Save"/> so it can later be swapped for a DB
/// table or the Eclipse site backend without touching callers.
///
/// Spending is exposed via <see cref="TrySpend"/> for a future store to call.
/// </summary>
public sealed class EclipseCurrencyManager : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTime = default!;

    private static readonly ResPath StorePath = new("/eclipse_currency.json");

    private readonly Dictionary<NetUserId, Balance> _balances = new();

    public override void Initialize()
    {
        base.Initialize();
        Load();
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
        // Play time data loads asynchronously after connect; seeding waits for this event so it never
        // touches play time before it is ready.
        _playTime.SessionPlayTimeUpdated += OnPlayTimeUpdated;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        _playTime.SessionPlayTimeUpdated -= OnPlayTimeUpdated;
    }

    public (int Merits, int Shards) GetBalance(NetUserId user)
    {
        return _balances.TryGetValue(user, out var balance) ? (balance.Merits, balance.Shards) : (0, 0);
    }

    /// <summary>
    /// Grants (or, with negatives, removes) currency. Balances never go below zero.
    /// </summary>
    public void AddCurrency(NetUserId user, int merits, int shards)
    {
        if (merits == 0 && shards == 0)
            return;

        var balance = GetOrCreate(user);
        balance.Merits = Math.Max(0, balance.Merits + merits);
        balance.Shards = Math.Max(0, balance.Shards + shards);

        Save();
        SyncToClient(user);
    }

    /// <summary>
    /// Attempts to spend currency. Returns false and changes nothing if the balance is insufficient.
    /// </summary>
    public bool TrySpend(NetUserId user, int merits, int shards)
    {
        var balance = GetOrCreate(user);
        if (balance.Merits < merits || balance.Shards < shards)
            return false;

        balance.Merits -= merits;
        balance.Shards -= shards;

        Save();
        SyncToClient(user);
        return true;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus is not (SessionStatus.Connected or SessionStatus.InGame))
            return;

        TrySeedAndSend(args.Session);
    }

    private void OnPlayTimeUpdated(ICommonSession session)
    {
        // Play time is now loaded, so a first-seen account can finally be seeded from its legacy value.
        TrySeedAndSend(session);
    }

    /// <summary>
    /// Sends the player's balance, seeding it first (from the legacy XP-derived value) the first time
    /// an account is seen — but only once play time data has loaded, so it never throws.
    /// </summary>
    private void TrySeedAndSend(ICommonSession session)
    {
        if (!_balances.ContainsKey(session.UserId))
        {
            // Not loaded yet: bail out. We'll be called again from SessionPlayTimeUpdated once it is.
            if (!_playTime.TryGetTrackerTimes(session, out _))
                return;

            var total = GetDerivedExperience(session);
            _balances[session.UserId] = new Balance
            {
                Merits = EclipseProgression.CalculateMerits(total),
                Shards = EclipseProgression.CalculateShards(total),
            };
            Save();
        }

        SendBalance(session);
    }

    private int GetDerivedExperience(ICommonSession session)
    {
        var playtimeMinutes = _playTime.GetOverallPlaytime(session).TotalMinutes;
        var bonusMinutes = 0d;

        if (_playTime.GetPlayTimes(session).TryGetValue(EclipseProgression.BonusExperienceTracker, out var span))
            bonusMinutes = span.TotalMinutes;

        return EclipseProgression.CalculateTotalExperience(playtimeMinutes, bonusMinutes);
    }

    public void SendBalance(ICommonSession session)
    {
        var (merits, shards) = GetBalance(session.UserId);
        RaiseNetworkEvent(new EclipseCurrencyBalanceEvent(merits, shards), session.Channel);
    }

    private void SyncToClient(NetUserId user)
    {
        if (_players.TryGetSessionById(user, out var session))
            SendBalance(session);
    }

    private Balance GetOrCreate(NetUserId user)
    {
        if (!_balances.TryGetValue(user, out var balance))
        {
            balance = new Balance();
            _balances[user] = balance;
        }

        return balance;
    }

    private void Load()
    {
        try
        {
            if (!_res.UserData.Exists(StorePath))
                return;

            using var reader = new StreamReader(_res.UserData.OpenRead(StorePath));
            var json = reader.ReadToEnd();
            var data = JsonSerializer.Deserialize<Dictionary<string, Balance>>(json);
            if (data == null)
                return;

            foreach (var (key, value) in data)
            {
                if (Guid.TryParse(key, out var guid))
                    _balances[new NetUserId(guid)] = value;
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to load Eclipse currency store: {e}");
        }
    }

    private void Save()
    {
        try
        {
            var data = new Dictionary<string, Balance>();
            foreach (var (user, balance) in _balances)
                data[user.UserId.ToString()] = balance;

            var json = JsonSerializer.Serialize(data);
            using var writer = _res.UserData.OpenWriteText(StorePath);
            writer.Write(json);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save Eclipse currency store: {e}");
        }
    }

    private sealed class Balance
    {
        public int Merits { get; set; }
        public int Shards { get; set; }
    }
}
