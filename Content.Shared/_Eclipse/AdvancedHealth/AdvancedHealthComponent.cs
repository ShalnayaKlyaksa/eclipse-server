using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;

namespace Content.Shared._Eclipse.AdvancedHealth;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AdvancedHealthComponent : Component
{
    /// <summary>
    /// Explicit anatomy override. When null, a profile whose ID matches HumanoidProfile.Species is used.
    /// </summary>
    [DataField] public ProtoId<AdvancedHealthAnatomyPrototype>? AnatomyProfile;

    [DataField, AutoNetworkedField] public Dictionary<BodyPartSlot, BodyPartState> BodyParts = new();

    [DataField, AutoNetworkedField] public float BloodVolume = 5000f;
    [DataField, AutoNetworkedField] public float MaxBloodVolume = 5000f;
    [DataField, AutoNetworkedField] public float Oxygenation = 100f;
    [DataField, AutoNetworkedField] public float Consciousness = 100f;
    [DataField, AutoNetworkedField] public float Pain;
    [DataField, AutoNetworkedField] public float Shock;
    [DataField, AutoNetworkedField] public float TraumaLoad;
    [DataField, AutoNetworkedField] public float InfectionLoad;

    // --- Cardiovascular loop (simulated) ---
    /// <summary>Heart rate in beats per minute. Compensates (tachycardia) then decompensates (bradycardia → arrest).</summary>
    [DataField, AutoNetworkedField] public float HeartRate = 75f;
    /// <summary>Mean arterial pressure in mmHg.</summary>
    [DataField, AutoNetworkedField] public float MeanArterialPressure = 92f;
    /// <summary>Tissue perfusion 0..1 (fraction of adequate blood delivery), derived from MAP.</summary>
    [DataField, AutoNetworkedField] public float Perfusion = 1f;
    /// <summary>Vascular tone multiplier; compensatory vasoconstriction raises it, decompensation drops it.</summary>
    [DataField, AutoNetworkedField] public float VascularTone = 1f;
    /// <summary>External chest-compression support (CPR) providing partial perfusion during arrest, 0..1.</summary>
    [DataField, AutoNetworkedField] public float CprSupport;
    /// <summary>Extra sympathetic/inotropic drive from drugs (e.g. epinephrine), decays over time.</summary>
    [DataField, AutoNetworkedField] public float AdrenalineDrive;

    /// <summary>Resting heart rate (bpm) with no physiological stress.</summary>
    [DataField] public float RestingHeartRate = 75f;
    /// <summary>Bradycardic heart rate at/below which the heart arrests.</summary>
    [DataField] public float BradyArrestHeartRate = 20f;
    /// <summary>Mean arterial pressure (mmHg) that counts as fully adequate perfusion.</summary>
    [DataField] public float AdequatePressure = 60f;
    /// <summary>Baseline mean arterial pressure (mmHg) at full volume, normal tone and rate.</summary>
    [DataField] public float BaselinePressure = 92f;

    // --- Blood type & oxygen carrying ---
    [DataField, AutoNetworkedField] public AboGroup BloodGroup = AboGroup.O;
    [DataField, AutoNetworkedField] public bool RhPositive = true;
    /// <summary>Randomise the blood group on spawn (real-world distribution) when true.</summary>
    [DataField] public bool RandomizeBloodGroup = true;
    [DataField, AutoNetworkedField] public string BodyFluid = "Blood";
    [DataField, AutoNetworkedField] public string BloodType = "O+";
    [DataField, AutoNetworkedField] public Color FluidColor = Color.Red;
    [DataField] public List<string> BloodTypes = new() { "O-", "O+", "A-", "A+", "B-", "B+", "AB-", "AB+" };
    [DataField] public string IncompatibleTransfusionPopup = "advanced-health-transfusion-incompatible";

    /// <summary>
    /// Fraction of blood volume that carries oxygen (red cells), 0..1. Crystalloids dilute it,
    /// whole blood restores it, incompatible transfusions destroy it (hemolysis); marrow regrows it.
    /// </summary>
    [DataField, AutoNetworkedField] public float OxygenCarryingCapacity = 1f;
    /// <summary>Per-second regeneration of oxygen-carrying capacity.</summary>
    [DataField] public float OxygenCarryingRegen = 0.0015f;

    // --- Immune defense ---
    /// <summary>Immune strength 0..100. Higher resists wound infection and disease.</summary>
    [DataField, AutoNetworkedField] public float ImmuneDefense = 100f;
    /// <summary>Per-second recovery of immune defense back toward 100.</summary>
    [DataField] public float ImmuneRegen = 0.05f;

    /// <summary>EMA-smoothed condition score (0 = healthy, 1 = critical) used for the health HUD readout.</summary>
    [DataField, AutoNetworkedField] public float SmoothedConditionBadness;

    /// <summary>Last HumanHealth alert severity — used for hysteresis so the icon does not flicker.</summary>
    [DataField, AutoNetworkedField] public short CachedAlertSeverity;

    [DataField, AutoNetworkedField] public bool IsUnconscious;
    [DataField, AutoNetworkedField] public bool IsHeartStopped;
    [DataField, AutoNetworkedField] public float TimeSinceHeartStopped;
    [DataField, AutoNetworkedField] public float TimeBelowLethalOxygen;
    [DataField, AutoNetworkedField] public bool IsInAgonalState;
    [DataField, AutoNetworkedField] public bool HasBlood = true;
    [DataField, AutoNetworkedField] public bool HasPain = true;
    [DataField, AutoNetworkedField] public bool NeedsOxygen = true;
    [DataField, AutoNetworkedField] public bool CanFracture = true;

    [DataField] public float BleedingModifier = 1f;
    [DataField] public float PainModifier = 1f;
    [DataField] public float TraumaModifier = 1f;
    [DataField] public float OxygenationModifier = 1f;

    [DataField] public float CriticalBloodVolume = 2500f;
    [DataField] public float BloodDeathThreshold = 1000f;
    [DataField] public float CriticalOxygenation = 55f;
    [DataField] public float OxygenDeathThreshold = 15f;
    [DataField] public float CriticalTraumaThreshold = 90f;
    [DataField] public float LethalTraumaThreshold = 120f;
    [DataField] public float InstantDeathTraumaThreshold = 160f;
    [DataField] public float ShockUnconsciousThreshold = 70f;
    [DataField] public float ShockHeartStopThreshold = 95f;
    [DataField] public float HeartStoppedDeathTime = 120f;

    [DataField] public Dictionary<BodyPartSlot, float> BodyPartHitWeights = new();
    [DataField] public Dictionary<BodyPartSlot, float> BodyPartAccuracyPenalties = new();
    [DataField] public Dictionary<WoundType, float> WoundBleedingRates = new();
    [DataField] public Dictionary<WoundType, float> WoundPainValues = new();
    [DataField] public Dictionary<WoundType, float> WoundTraumaValues = new();

    [DataField] public float BandageEffectiveness = 0.55f;
    [DataField] public float PressureBandageEffectiveness = 0.8f;
    [DataField] public float TourniquetEffectiveness = 0.97f;
    [DataField] public float HemostaticEffectiveness = 0.7f;
}

[RegisterComponent]
public sealed partial class AdvancedHealthHitContextComponent : Component
{
    public float DominantOriginal;
    public float DominantModified;
    public string? DominantDamageType;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AimTargetComponent : Component
{
    [DataField, AutoNetworkedField] public BodyPartTarget CurrentTarget = BodyPartTarget.Auto;
    [DataField, AutoNetworkedField] public bool AdvancedTargetingEnabled;
    [DataField] public float LastTargetChangeTime;
    /// <summary>Movement slow applied while precision-aiming at head/neck (0–1).</summary>
    [DataField] public float VitalAimSlowModifier = 0.82f;
}

public enum AdvancedTreatmentType : byte
{
    Bandage,
    PressureBandage,
    Tourniquet,
    Splint,
    Hemostatic,
    Suture,
    ForeignBodyRemoval,
}

[RegisterComponent]
public sealed partial class AdvancedTreatmentComponent : Component
{
    [DataField(required: true)] public AdvancedTreatmentType Treatment;
    [DataField] public float Effectiveness = 1f;
    [DataField] public float Delay = 3f;
}

[Serializable, NetSerializable]
public sealed partial class AdvancedTreatmentDoAfterEvent : SimpleDoAfterEvent;

/// <summary>
/// Do-after used by menu-driven self-treatment, carries the explicitly chosen body zone.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class AdvancedTargetedTreatmentDoAfterEvent : SimpleDoAfterEvent
{
    [DataField] public BodyPartSlot Slot;
    [DataField] public AdvancedTreatmentType Treatment;
    [DataField] public float Effectiveness = 1f;
}

/// <summary>
/// Sent by the client health screen to request a treatment on a specific body zone using an item in hand.
/// </summary>
[Serializable, NetSerializable]
public sealed class AdvancedHealthSelfTreatmentRequestEvent : EntityEventArgs
{
    public NetEntity Target;
    public BodyPartSlot Slot;
    public AdvancedTreatmentType Treatment;

    public AdvancedHealthSelfTreatmentRequestEvent(NetEntity target, BodyPartSlot slot, AdvancedTreatmentType treatment)
    {
        Target = target;
        Slot = slot;
        Treatment = treatment;
    }
}
