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

    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    [DataField, AutoNetworkedField]
    public ProtoId<IndustrialRecipePrototype>? SelectedRecipe;

    // Suppresses reentrant processing while container/stack events are raised by a transaction.
    public bool InTransaction;
    public bool WaitingForOutput;
    public bool PendingStateRefresh;
    public float UiAccumulator;
    public IndustrialProcessorState? LastState;
    public IndustrialRecipeUiState[]? RecipeCatalog;


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

}
