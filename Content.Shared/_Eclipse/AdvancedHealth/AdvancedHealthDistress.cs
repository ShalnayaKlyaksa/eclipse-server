namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>
/// Shared distress metrics for advanced-health visual and audio feedback.
/// </summary>
public static class AdvancedHealthDistress
{
    /// <summary>0 = healthy, 1 = critical distress.</summary>
    public static float ComputeDistressLevel(AdvancedHealthComponent health)
    {
        if (health.IsHeartStopped)
            return 1f;

        var shockFactor = Math.Clamp((health.Shock - 25f) / 75f, 0f, 1f);
        var consciousnessFactor = Math.Clamp((25f - health.Consciousness) / 25f, 0f, 1f);
        var painFactor = health.HasPain ? Math.Clamp((health.Pain - 20f) / 80f, 0f, 1f) : 0f;
        var oxygenFactor = health.NeedsOxygen ? Math.Clamp((70f - health.Oxygenation) / 70f, 0f, 1f) : 0f;

        var distress = Math.Max(shockFactor, consciousnessFactor);
        distress = Math.Max(distress, painFactor * 0.6f);
        distress = Math.Max(distress, oxygenFactor * 0.5f);

        if (health.IsUnconscious)
            distress = Math.Max(distress, 0.75f);

        return Math.Clamp(distress, 0f, 1f);
    }

    /// <summary>1 = normal hearing, lower values duck master volume.</summary>
    public static float ComputeMuffleFactor(AdvancedHealthComponent health)
        => 1f - ComputeDistressLevel(health) * 0.55f;

    /// <summary>0 = off, 1 = loud/fast heartbeat.</summary>
    public static float ComputeHeartbeatIntensity(AdvancedHealthComponent health)
    {
        if (health.IsHeartStopped)
            return 0.9f;

        var shock = Math.Clamp((health.Shock - 30f) / 70f, 0f, 1f);
        var unconsciousBoost = health.IsUnconscious ? 0.3f : 0f;
        var lowConsciousness = Math.Clamp((40f - health.Consciousness) / 40f, 0f, 0.25f);

        return Math.Clamp(shock + unconsciousBoost + lowConsciousness, 0f, 1f);
    }

    /// <summary>0 = off, 1 = loud ear ringing. Only kicks in near death (severe hypoxia / fading mind).</summary>
    public static float ComputeRingingIntensity(AdvancedHealthComponent health)
    {
        if (health.IsHeartStopped)
            return 1f;

        var oxygen = health.NeedsOxygen ? Math.Clamp((35f - health.Oxygenation) / 35f, 0f, 1f) : 0f;
        var consciousness = Math.Clamp((20f - health.Consciousness) / 20f, 0f, 1f);

        return Math.Clamp(Math.Max(oxygen, consciousness), 0f, 1f);
    }
}
