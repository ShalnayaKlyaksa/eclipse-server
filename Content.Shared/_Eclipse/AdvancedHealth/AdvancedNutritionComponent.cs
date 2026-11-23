using Robust.Shared.GameStates;

namespace Content.Shared._Eclipse.AdvancedHealth;

/// <summary>Long-term deficits are separate from the current stomach/thirst reading.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AdvancedNutritionComponent : Component
{
    [DataField, AutoNetworkedField] public float Malnutrition;
    [DataField, AutoNetworkedField] public float DehydrationStrain;
    [DataField] public float StarvationExposure;
    [DataField] public float DehydrationExposure;
    [DataField] public float StarvationGrace = 300;
    [DataField] public float DehydrationGrace = 180;
    [DataField] public float StarvationProgressTime = 900;
    [DataField] public float DehydrationProgressTime = 480;
    [DataField] public float NutritionRecoveryTime = 600;
    [DataField] public float HydrationRecoveryTime = 300;

    [DataField, AutoNetworkedField] public bool FoodDepleted;
    [DataField, AutoNetworkedField] public bool WaterDepleted;
    public bool RequiresCrawling => FoodDepleted && WaterDepleted || Malnutrition >= 0.95f || DehydrationStrain >= 0.95f;
    public float ReserveMultiplier => RequiresCrawling ? 0.05f
        : Math.Clamp(1 - Malnutrition * 0.55f - DehydrationStrain * 0.7f
            - (FoodDepleted ? 0.15f : 0) - (WaterDepleted ? 0.25f : 0), 0.1f, 1);
    public float RecoveryMultiplier => RequiresCrawling || WaterDepleted ? 0
        : Math.Max(0, 1f - Malnutrition * 0.8f - DehydrationStrain * 0.9f) * (FoodDepleted ? 0.2f : 1);
    public float MovementMultiplier => RequiresCrawling ? 0.15f : 1f - Malnutrition * 0.35f - DehydrationStrain * 0.45f;
}
