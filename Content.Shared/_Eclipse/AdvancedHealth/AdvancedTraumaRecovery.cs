using System.Linq;

namespace Content.Shared._Eclipse.AdvancedHealth;

public static class AdvancedTraumaRecovery
{
    /// <summary>Game pacing: severe fractures need fixation, simple ones can unite in a splint.</summary>
    public static bool Advance(BodyPartState part, float elapsed, float perfusion, float healingTime)
    {
        if (!float.IsFinite(elapsed) || elapsed <= 0f)
            return false;
        var changed = part.LocalAnesthesia > 0f;
        part.LocalAnesthesia = Math.Max(0f, part.LocalAnesthesia - elapsed);
        if (!float.IsFinite(healingTime) || healingTime <= 0f
            || part.IsDestroyed || part.IsSurgicalOpen || part.HasTourniquet || perfusion < 0.5f
            || part.Wounds.Any(wound => wound.InternalBleedingRate > 0.001f))
            return changed;
        var fractures = part.Wounds.Where(wound => wound.Type == WoundType.Fracture).ToArray();
        if (fractures.Length == 0 || fractures.Any(wound => !wound.IsBoneFixed
                && (!part.IsSplinted || wound.Severity >= WoundSeverity.Severe)))
            return changed;
        var recovery = elapsed / healingTime;
        part.BoneIntegrity = Math.Min(part.MaxBoneIntegrity, part.BoneIntegrity + part.MaxBoneIntegrity * recovery);
        foreach (var wound in fractures)
        {
            wound.HealingProgress = Math.Min(1f, wound.HealingProgress + recovery);
            wound.Pain = Math.Max(0f, wound.Pain * (1f - Math.Min(1f, recovery)));
        }
        part.Wounds.RemoveAll(wound => wound.Type == WoundType.Fracture && wound.HealingProgress >= 1f);
        if (!part.Wounds.Any(wound => wound.Type == WoundType.Fracture))
            part.IsSplinted = false;
        return true;
    }
}
