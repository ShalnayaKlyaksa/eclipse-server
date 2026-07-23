using System;
using System.Collections.Generic;
using Content.Server.GameTicking;
using Content.Server.Players.PlayTimeTracking;
using Content.Shared.Eclipse.Progression;
using Content.Shared.GameTicking;
using Content.Shared.Players.PlayTimeTracking;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Eclipse.Progression;

/// <summary>
/// Accumulates per-player results over a shift (task experience, completed tasks) and, at round end,
/// awards participation experience and sends each player their personal <see cref="EclipseRoundEndStatsEvent"/>.
/// </summary>
public sealed class EclipseShiftStatsSystem : EntitySystem
{
    [Dependency] private readonly PlayTimeTrackingManager _playTime = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly Economy.EclipseCurrencyManager _currency = default!;

    private readonly Dictionary<NetUserId, ShiftStats> _stats = new();

    public override void Initialize()
    {
        base.Initialize();

        // Fires inside ShowRoundEndScoreboard *before* the networked RoundEndMessageEvent, so the
        // player receives their stats before the summary window opens.
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndTextAppend);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    /// <summary>
    /// Records a completed task's reward for the shift. Called when a personal task is finished.
    /// </summary>
    public void RecordTaskReward(ICommonSession session, string title, int experience, int credits)
    {
        var stats = GetStats(session.UserId);
        stats.TaskExperience += Math.Max(0, experience);

        if (!string.IsNullOrWhiteSpace(title))
            stats.CompletedTasks.Add(title);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _stats.Clear();
    }

    private void OnRoundEndTextAppend(RoundEndTextAppendEvent ev)
    {
        var roundId = _ticker.RoundId;
        var participation = EclipseProgression.CalculateRoundParticipationExperience(_ticker.RoundDuration().TotalMinutes);

        foreach (var session in _players.Sessions)
        {
            if (session.Status != SessionStatus.InGame)
                continue;

            var stats = GetStats(session.UserId);

            // Award participation experience into the bonus tracker (task experience was already added
            // to the tracker as tasks were completed during the round).
            if (participation > 0)
            {
                var bonusTime = TimeSpan.FromMinutes((double) participation / EclipseProgression.BonusExperiencePerMinute);
                _playTime.AddTimeToTracker(session, EclipseProgression.BonusExperienceTracker, bonusTime);
            }

            var earned = stats.TaskExperience + participation;
            var total = GetTotalExperience(session);
            var progress = EclipseProgression.CalculateProgress(total);

            var meritsEarned = EclipseProgression.CalculateMerits(earned);
            var shardsEarned = EclipseProgression.CalculateShards(earned);

            // Credit the earned currency into the player's real, spendable balance.
            _currency.AddCurrency(session.UserId, meritsEarned, shardsEarned);

            var message = new EclipseRoundEndStatsEvent
            {
                RoundId = roundId,
                ExperienceEarned = earned,
                TaskExperience = stats.TaskExperience,
                ParticipationExperience = participation,
                MeritsEarned = meritsEarned,
                ShardsEarned = shardsEarned,
                Level = progress.Level,
                CurrentLevelExperience = progress.CurrentExperience,
                NextLevelExperience = progress.NextLevelExperience,
                CompletedTasks = new List<string>(stats.CompletedTasks),
            };

            RaiseNetworkEvent(message, session.Channel);
        }
    }

    private int GetTotalExperience(ICommonSession session)
    {
        // Guard against play time not being loaded yet (throws otherwise).
        if (!_playTime.TryGetTrackerTimes(session, out var times))
            return 0;

        var playtimeMinutes = times.TryGetValue(PlayTimeTrackingShared.TrackerOverall, out var overall)
            ? overall.TotalMinutes
            : 0d;
        var bonusMinutes = times.TryGetValue(EclipseProgression.BonusExperienceTracker, out var bonusSpan)
            ? bonusSpan.TotalMinutes
            : 0d;

        return EclipseProgression.CalculateTotalExperience(playtimeMinutes, bonusMinutes);
    }

    private ShiftStats GetStats(NetUserId userId)
    {
        if (!_stats.TryGetValue(userId, out var stats))
        {
            stats = new ShiftStats();
            _stats[userId] = stats;
        }

        return stats;
    }

    private sealed class ShiftStats
    {
        public int TaskExperience;
        public readonly List<string> CompletedTasks = new();
    }
}
