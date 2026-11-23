using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared.Eclipse.Economy;
using Content.Shared.Eclipse.Progression;
using Content.Shared.PDA;
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
        if (merits < 0 || shards < 0)
            return false;
        var balance = GetOrCreate(user);
        if (balance.Merits < merits || balance.Shards < shards)
            return false;

        balance.Merits -= merits;
        balance.Shards -= shards;

        Save();
        SyncToClient(user);
        return true;
    }

    public PdaDesktopProfile GetDesktop(NetUserId user) => _balances.TryGetValue(user, out var balance) ? balance.Desktop : new();

    public string GetPdaPublicId(NetUserId user, int slot)
    {
        return _balances.TryGetValue(user, out var balance)
            ? balance.CharacterPdaIds.GetValueOrDefault(slot, string.Empty)
            : string.Empty;
    }

    public bool IsPdaPublicIdReserved(string id)
    {
        foreach (var wallet in _balances.Values)
        {
            if (wallet.Desktop.PublicId == id || wallet.CharacterPdaIds.ContainsValue(id))
                return true;
        }

        return false;
    }

    public bool SetPdaPublicId(NetUserId user, int slot, string id)
    {
        if (!PdaPublicId.Valid(id) || !_balances.TryGetValue(user, out var balance))
            return false;

        var previous = GetPdaPublicId(user, slot);
        if (previous == id)
            return true;
        if (IsPdaPublicIdReserved(id))
            return false;

        var ids = balance.CharacterPdaIds;
        ids[slot] = id;
        if (Save())
            return true;

        if (previous.Length == 0)
            ids.Remove(slot);
        else
            ids[slot] = previous;
        return false;
    }

    public void MigratePdaPublicId(NetUserId user, int slot)
    {
        if (!_balances.TryGetValue(user, out var balance) || !PdaPublicId.Valid(balance.Desktop.PublicId))
            return;

        var legacyId = balance.Desktop.PublicId;
        if (!balance.CharacterPdaIds.TryAdd(slot, legacyId))
            return;

        balance.Desktop.PublicId = string.Empty;
        if (Save())
            return;

        balance.Desktop.PublicId = legacyId;
        balance.CharacterPdaIds.Remove(slot);
    }

    public bool RemovePdaPublicId(NetUserId user, int slot)
    {
        if (!_balances.TryGetValue(user, out var balance) || !balance.CharacterPdaIds.Remove(slot, out var id))
            return true;
        if (Save())
            return true;

        balance.CharacterPdaIds[slot] = id;
        return false;
    }

    public bool TryBuyDesktopCosmetic(NetUserId user, string id, bool shards)
    {
        var item = PdaCosmetics.Find(id);
        if (!_balances.TryGetValue(user, out var balance) || item == null || Array.IndexOf(balance.Desktop.Owned, id) >= 0)
            return false;
        var meritsCost = shards ? 0 : item.Merits;
        var shardsCost = shards ? item.Shards : 0;
        if (balance.Merits < meritsCost || balance.Shards < shardsCost)
            return false;
        balance.Merits -= meritsCost;
        balance.Shards -= shardsCost;
        var previousOwned = balance.Desktop.Owned;
        balance.Desktop.Owned = [.. balance.Desktop.Owned, id];
        if (!Save())
        {
            balance.Merits += meritsCost;
            balance.Shards += shardsCost;
            balance.Desktop.Owned = previousOwned;
            return false;
        }
        SyncToClient(user);
        return true;
    }

    public bool SaveDesktop(NetUserId user, PdaDesktopProfile profile)
    {
        if (profile == null || profile.Positions == null || profile.CardText == null ||
            !_balances.TryGetValue(user, out var balance) || profile.Positions.Count > 64 || profile.CardText.Length > 256 ||
            PdaCosmetics.Find(profile.Wallpaper) == null ||
            Array.IndexOf(balance.Desktop.Owned, profile.Wallpaper) < 0)
            return false;
        foreach (var (key, cell) in profile.Positions)
        {
            if (key.Length > 96 || cell < -1 || cell > 63)
                return false;
        }
        var studio = Array.IndexOf(balance.Desktop.Owned, "studio") >= 0;
        var previous = balance.Desktop;
        balance.Desktop = new PdaDesktopProfile
        {
            Owned = balance.Desktop.Owned,
            Wallpaper = profile.Wallpaper,
            PublicId = balance.Desktop.PublicId,
            Positions = new Dictionary<string, int>(profile.Positions),
            Card = studio && profile.Card,
            Decor = studio && profile.Decor,
            Photo = studio && profile.Photo,
            CustomWallpaper = studio && profile.CustomWallpaper,
            CardText = studio ? profile.CardText : "",
        };
        if (Save()) return true;
        balance.Desktop = previous;
        return false;
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
            var data = JsonSerializer.Deserialize<Dictionary<string, Balance>>(json, JsonOptions);
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

    private bool Save()
    {
        try
        {
            var data = new Dictionary<string, Balance>();
            foreach (var (user, balance) in _balances)
                data[user.UserId.ToString()] = balance;

            var json = JsonSerializer.Serialize(data, JsonOptions);
            if (_res.UserData.RootDir is { } directory)
            {
                var path = Path.Combine(directory, "eclipse_currency.json");
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, json);
                File.Move(temporary, path, overwrite: true);
            }
            else
            {
                using var writer = _res.UserData.OpenWriteText(StorePath);
                writer.Write(json);
            }
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save Eclipse currency store: {e}");
            return false;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };

    private sealed class Balance
    {
        public int Merits { get; set; }
        public int Shards { get; set; }
        public PdaDesktopProfile Desktop { get; set; } = new();
        public Dictionary<int, string> CharacterPdaIds { get; set; } = new();
    }
}
