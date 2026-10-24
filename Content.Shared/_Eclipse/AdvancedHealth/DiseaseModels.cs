using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>
/// A disease definition. Diseases progress while the immune system is too weak to clear them and
/// apply medical effects and debuffs (weakness, clumsiness, etc.). This is the foundation — more
/// effects (melee/accuracy hooks) can be layered on later.
/// </summary>
[Prototype]
public sealed partial class AdvancedDiseasePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    [DataField] public LocId Name = "advanced-disease-unknown";
    /// <summary>Short status word shown for the debuff it causes, e.g. "Слабость".</summary>
    [DataField] public LocId? StatusLabel;

    /// <summary>Movement speed multiplier while sick (0.8 = −20%).</summary>
    [DataField] public float SpeedModifier = 1f;
    /// <summary>Melee damage multiplier while sick (0.5 = −50%).</summary>
    [DataField] public float MeleeDamageModifier = 1f;
    /// <summary>Two-handed melee weapons cannot be used while sick.</summary>
    [DataField] public bool BlockTwoHandedMelee;
    /// <summary>Extra weapon spread (accuracy penalty) in degrees.</summary>
    [DataField] public float RangedSpread;

    /// <summary>Pain added per second while active.</summary>
    [DataField] public float PainPerSecond;
    /// <summary>Shock added per second while active.</summary>
    [DataField] public float ShockPerSecond;
    /// <summary>Immune defense drained per second while fighting the disease.</summary>
    [DataField] public float ImmuneDrain = 0.05f;

    /// <summary>Seconds of sustained strong immunity needed to clear the disease.</summary>
    [DataField] public float ClearTime = 120f;
    /// <summary>Immune defense at/above which the immune system makes progress clearing it.</summary>
    [DataField] public float ClearImmuneThreshold = 55f;

    /// <summary>Base infectivity 0..1 — chance to catch it, before immune resistance.</summary>
    [DataField] public float Infectivity = 0.5f;
}

/// <summary>Diseases currently affecting a mob, mapped to how long they've been active (seconds).</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AdvancedDiseaseComponent : Component
{
    [DataField, AutoNetworkedField] public Dictionary<string, float> Active = new();
    /// <summary>Progress (seconds of strong immunity) toward clearing each active disease.</summary>
    [DataField] public Dictionary<string, float> ClearProgress = new();
}
