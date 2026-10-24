using System.Collections.Generic;
using Robust.Shared.Serialization;

namespace Content.Shared.Eclipse.Progression;

/// <summary>
/// Sent directly to a player at the end of a round with their personal shift results, so the
/// Eclipse-styled round-end summary can show experience/currency earned and tasks completed.
/// </summary>
[Serializable, NetSerializable]
public sealed class EclipseRoundEndStatsEvent : EntityEventArgs
{
    public int RoundId;

    /// <summary>Total experience earned this shift (tasks + round participation).</summary>
    public int ExperienceEarned;

    /// <summary>Experience earned specifically from completed tasks this shift.</summary>
    public int TaskExperience;

    /// <summary>Experience earned for taking part in the round.</summary>
    public int ParticipationExperience;

    /// <summary>Merits earned this shift (derived from <see cref="ExperienceEarned"/>).</summary>
    public int MeritsEarned;

    /// <summary>Shards earned this shift (derived from <see cref="ExperienceEarned"/>).</summary>
    public int ShardsEarned;

    /// <summary>New account level after this shift.</summary>
    public int Level;

    /// <summary>Experience into the current level.</summary>
    public int CurrentLevelExperience;

    /// <summary>Experience required to reach the next level (0 at max level).</summary>
    public int NextLevelExperience;

    /// <summary>Titles of tasks completed this shift.</summary>
    public List<string> CompletedTasks = new();

    public EclipseRoundEndStatsEvent()
    {
    }
}
