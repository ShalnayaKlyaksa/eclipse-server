using System.Collections.Generic;
using Content.Shared.Eclipse.Progression;

namespace Content.Client.Eclipse.Progression;

/// <summary>
/// Caches the personal <see cref="EclipseRoundEndStatsEvent"/> sent by the server at round end so the
/// round-end summary window can display it. The stats message is sent before the round-end message, so
/// it is already cached by the time the summary window opens.
/// </summary>
public sealed class EclipseRoundEndStatsSystem : EntitySystem
{
    private readonly Dictionary<int, EclipseRoundEndStatsEvent> _byRound = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<EclipseRoundEndStatsEvent>(OnStats);
    }

    private void OnStats(EclipseRoundEndStatsEvent ev)
    {
        _byRound[ev.RoundId] = ev;
    }

    public EclipseRoundEndStatsEvent? GetStats(int roundId)
    {
        return _byRound.TryGetValue(roundId, out var stats) ? stats : null;
    }
}
