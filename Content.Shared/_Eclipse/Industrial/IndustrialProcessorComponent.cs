using Content.Shared._Eclipse.Industrial.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._Eclipse.Industrial;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedIndustrialProcessorSystem), Other = AccessPermissions.ReadWriteExecute)]
public sealed partial class IndustrialProcessorComponent : Component
{
    [DataField(required: true)]
    public IndustrialProcessorType ProcessorType;

    [DataField]
    public MachineTier Tier = MachineTier.Industrial;

    [DataField]
    public string InputContainerId = "industrial_input";

    [DataField]
    public string OutputContainerId = "industrial_output";

    [DataField, AutoNetworkedField]
    public ProtoId<IndustrialRecipePrototype>? CurrentRecipe;

    [DataField, AutoNetworkedField]
    public float ProcessingTime;

    [DataField, AutoNetworkedField]
    public float ProcessingAccumulator;

    [DataField, AutoNetworkedField]
    public bool IsWorking;

    [DataField]
    public float RequiredPower = 1000f;

    [DataField]
    public float ProcessingSpeedMultiplier = 1f;

    [DataField]
    public int MaxInputSlots = 4;

    [DataField]
    public int MaxOutputSlots = 4;

    [DataField]
    public int MaxAutoTransferPerSecond = 1;

    [DataField]
    public float BasePowerLoad = 0f;

    [DataField]
    public bool CanWorkWithoutPower;

    [DataField]
    public bool AutoStart = true;

    [DataField, AutoNetworkedField]
    public FacePortState NorthFacePort = FacePortState.Disabled;

    [DataField, AutoNetworkedField]
    public FacePortState SouthFacePort = FacePortState.Disabled;

    [DataField, AutoNetworkedField]
    public FacePortState EastFacePort = FacePortState.Disabled;

    [DataField, AutoNetworkedField]
    public FacePortState WestFacePort = FacePortState.Disabled;

    /// <summary>
    /// TODO: Add optional SolutionContainerId for washer water/reagent consumption.
    /// </summary>
    [DataField]
    public string? SolutionContainerId;

    public FacePortState GetFacePort(Direction direction)
    {
        return direction switch
        {
            Direction.North => NorthFacePort,
            Direction.South => SouthFacePort,
            Direction.East => EastFacePort,
            Direction.West => WestFacePort,
            _ => FacePortState.Disabled,
        };
    }

    public void SetFacePort(Direction direction, FacePortState state)
    {
        switch (direction)
        {
            case Direction.North:
                NorthFacePort = state;
                break;
            case Direction.South:
                SouthFacePort = state;
                break;
            case Direction.East:
                EastFacePort = state;
                break;
            case Direction.West:
                WestFacePort = state;
                break;
        }
    }

    public PortMode GetItemPortMode(Direction direction)
    {
        return GetFacePort(direction) switch
        {
            FacePortState.ItemInput => PortMode.Input,
            FacePortState.ItemOutput => PortMode.Output,
            _ => PortMode.Disabled,
        };
    }

    public LiquidPortMode GetLiquidPortMode(Direction direction)
    {
        return GetFacePort(direction) switch
        {
            FacePortState.LiquidInput => LiquidPortMode.Input,
            FacePortState.LiquidOutput => LiquidPortMode.Output,
            _ => LiquidPortMode.Disabled,
        };
    }

    public FacePortState CycleFacePort(Direction direction)
    {
        var next = GetFacePort(direction) switch
        {
            FacePortState.Disabled => FacePortState.ItemInput,
            FacePortState.ItemInput => FacePortState.ItemOutput,
            FacePortState.ItemOutput => FacePortState.LiquidInput,
            FacePortState.LiquidInput => FacePortState.LiquidOutput,
            FacePortState.LiquidOutput => FacePortState.HeatInput,
            _ => FacePortState.Disabled,
        };

        SetFacePort(direction, next);
        return next;
    }

    public void ClearItemOutputsExcept(Direction except)
    {
        foreach (var direction in SharedIndustrialProcessorSystem.CardinalDirections)
        {
            if (direction == except)
                continue;

            if (GetFacePort(direction) == FacePortState.ItemOutput)
                SetFacePort(direction, FacePortState.Disabled);
        }
    }
}
