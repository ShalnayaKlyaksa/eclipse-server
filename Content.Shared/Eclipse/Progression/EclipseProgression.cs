using System;

namespace Content.Shared.Eclipse.Progression;

public static class EclipseProgression
{
    public const string BonusExperienceTracker = "EclipseBonusXp";
    public const int BonusExperiencePerMinute = 1;
    public const int MaxLevel = 100;

    // --- Balance knobs (tune these) ---------------------------------------------------------
    // Passive experience granted per minute of playtime. Kept purely additive so lowering task
    // weight never retroactively removes account experience/levels.
    public const int PlaytimeExperiencePerMinute = 6;

    // Experience granted at round end for participating, scaling with round length.
    public const int RoundParticipationBaseExperience = 60;
    public const int RoundParticipationPerMinute = 2;
    public const int RoundParticipationMaxExperience = 240;

    // Currency is derived from total experience (there is no separate currency backend yet).
    public const int ExperiencePerMerit = 2;
    public const int ExperiencePerShard = 250;

    private static readonly int[] ExperienceForLevel =
    [
        0,
        84,
        172,
        264,
        360,
        460,
        564,
        672,
        784,
        900,
        1020,
        1144,
        1272,
        1404,
        1540,
        1680,
        1824,
        1972,
        2124,
        2280,
        2440,
        2604,
        2772,
        2944,
        3120,
        3300,
        3484,
        3672,
        3864,
        4060,
        4260,
        4464,
        4672,
        4884,
        5100,
        5320,
        5544,
        5772,
        6004,
        6240,
        6480,
        6724,
        6972,
        7224,
        7480,
        7740,
        8004,
        8272,
        8544,
        8820,
        9100,
        9384,
        9672,
        9964,
        10260,
        10560,
        10864,
        11172,
        11484,
        11800,
        12120,
        12465,
        12835,
        13230,
        13650,
        14095,
        14565,
        15060,
        15580,
        16125,
        16695,
        17290,
        17910,
        18555,
        19225,
        19920,
        20640,
        21385,
        22155,
        22950,
        23770,
        24615,
        25485,
        26380,
        27300,
        28245,
        29215,
        30210,
        31230,
        32275,
        33345,
        34440,
        35560,
        36705,
        37875,
        39070,
        40290,
        41535,
        42805,
        44100,
    ];

    public static AccountProgress CalculateProgress(int totalExperience)
    {
        totalExperience = Math.Max(0, totalExperience);
        var level = 1;

        for (var i = 1; i < ExperienceForLevel.Length; i++)
        {
            if (totalExperience >= ExperienceForLevel[i])
                level = i + 1;
            else
                break;
        }

        level = Math.Min(level, MaxLevel);
        var current = totalExperience - ExperienceForLevel[level - 1];
        var next = level >= MaxLevel ? 0 : ExperienceForLevel[level] - ExperienceForLevel[level - 1];

        return new AccountProgress(level, current, next);
    }

    public static int GetExperienceForLevel(int level)
    {
        return ExperienceForLevel[Math.Clamp(level, 1, MaxLevel) - 1];
    }

    public static string GetRankName(int level)
    {
        return level switch
        {
            <= 5 => "Стажёр",
            <= 10 => "Новобранец",
            <= 15 => "Служащий",
            <= 20 => "Работник",
            <= 25 => "Специалист",
            <= 30 => "Офицер",
            <= 35 => "Старшина",
            <= 40 => "Инспектор",
            <= 45 => "Куратор",
            <= 50 => "Координатор",
            <= 55 => "Смотритель",
            <= 60 => "Надзиратель",
            <= 65 => "Управляющий",
            <= 70 => "Комиссар",
            <= 75 => "Наместник",
            <= 80 => "Префект",
            <= 85 => "Советник",
            <= 90 => "Архонт",
            <= 95 => "Регент",
            _ => "Глас",
        };
    }

    /// <summary>
    /// Single source of truth for total account experience, shared by client (display) and server
    /// (authoritative round-end stats) so the numbers always match.
    /// </summary>
    public static int CalculateTotalExperience(double playtimeMinutes, double bonusTrackerMinutes)
    {
        var playtime = Math.Max(0, (int) Math.Floor(playtimeMinutes * PlaytimeExperiencePerMinute));
        var bonus = Math.Max(0, (int) Math.Floor(bonusTrackerMinutes * BonusExperiencePerMinute));
        return playtime + bonus;
    }

    public static int CalculateMerits(int totalExperience)
    {
        return Math.Max(0, totalExperience) / ExperiencePerMerit;
    }

    public static int CalculateShards(int totalExperience)
    {
        return Math.Max(0, totalExperience) / ExperiencePerShard;
    }

    /// <summary>
    /// Experience awarded at the end of a round for taking part, scaled by round duration and capped.
    /// </summary>
    public static int CalculateRoundParticipationExperience(double roundMinutes)
    {
        var minutes = Math.Max(0, (int) Math.Floor(roundMinutes));
        var xp = RoundParticipationBaseExperience + minutes * RoundParticipationPerMinute;
        return Math.Clamp(xp, RoundParticipationBaseExperience, RoundParticipationMaxExperience);
    }

    public static bool TryGetAttestationLevel(int level, out int attestationLevel)
    {
        if (level < 10 || level % 10 != 0)
        {
            attestationLevel = 0;
            return false;
        }

        attestationLevel = level / 10;
        return true;
    }
}

public readonly record struct AccountProgress(int Level, int CurrentExperience, int NextLevelExperience);
