using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared.Containers;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Stacks;
using Content.Shared.Tools.Systems;
using Content.Shared.Temperature.Components;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared._Eclipse.Industrial;

public abstract class SharedIndustrialProcessorSystem : EntitySystem
{
    [Dependency] protected readonly IPrototypeManager PrototypeManager = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedToolSystem _tools = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public static readonly Direction[] CardinalDirections =
        [Direction.North, Direction.South, Direction.East, Direction.West];

    protected readonly Dictionary<IndustrialProcessorType, List<IndustrialRecipePrototype>> RecipesByProcessor = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IndustrialProcessorComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<IndustrialProcessorComponent, AfterInteractUsingEvent>(OnAfterInteractUsing);
        SubscribeLocalEvent<IndustrialProcessorComponent, EntInsertedIntoContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<IndustrialProcessorComponent, EntRemovedFromContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<IndustrialProcessorComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<IndustrialProcessorComponent, GetVerbsEvent<InteractionVerb>>(OnGetVerbs);
        SubscribeLocalEvent<IndustrialProcessorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<IndustrialProcessorComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<IndustrialProcessorComponent, GetVerbsEvent<AlternativeVerb>>(OnGetPortVerbs);

        foreach (var recipe in PrototypeManager.EnumeratePrototypes<IndustrialRecipePrototype>())
        {
            if (!RecipesByProcessor.TryGetValue(recipe.Processor, out var list))
            {
                list = new List<IndustrialRecipePrototype>();
                RecipesByProcessor[recipe.Processor] = list;
            }

            list.Add(recipe);
        }
    }

    private void OnInteractUsing(Entity<IndustrialProcessorComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (IsPortConfigurator(args.Used))
        {
            if (TryGetClickedFace(ent, args.ClickLocation, out var direction))
                CycleFacePort(ent, args.User, direction);

            args.Handled = true;
            return;
        }

        if (!_container.TryGetContainer(ent, ent.Comp.InputContainerId, out var input))
            return;

        if (!IsValidInput(args.Used, ent.Comp.ProcessorType))
        {
            _popup.PopupClient(Loc.GetString("industrial-processor-wrong-input"), ent, args.User);
            args.Handled = true;
            return;
        }

        if (input.Count >= ent.Comp.MaxInputSlots && !CanMergeIntoContainer(input, args.Used))
        {
            args.Handled = true;
            return;
        }

        if (!_hands.TryDropIntoContainer(args.User, args.Used, input))
            return;

        _popup.PopupClient(Loc.GetString("industrial-manual-insert-success"), ent, args.User);
        args.Handled = true;
    }

    private void OnAfterInteractUsing(Entity<IndustrialProcessorComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach || !IsPortConfigurator(args.Used))
            return;

        if (!TryGetClickedFace(ent, args.ClickLocation, out var direction))
            return;

        CycleFacePort(ent, args.User, direction);
        args.Handled = true;
    }

    private void OnContainerModified<T>(Entity<IndustrialProcessorComponent> ent, ref T args) where T : notnull
    {
        if (args is EntInsertedIntoContainerMessage insert && insert.Container.ID != ent.Comp.InputContainerId)
            return;

        if (args is EntRemovedFromContainerMessage remove &&
            remove.Container.ID != ent.Comp.InputContainerId &&
            remove.Container.ID != ent.Comp.OutputContainerId)
            return;

        if (ent.Comp.AutoStart)
            TryStartProcessing(ent);

        UpdateUserInterface(ent);
    }

    private void OnInsertAttempt(Entity<IndustrialProcessorComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Container.ID != ent.Comp.InputContainerId && args.Container.ID != ent.Comp.OutputContainerId)
            return;

        var maxSlots = args.Container.ID == ent.Comp.InputContainerId
            ? ent.Comp.MaxInputSlots
            : ent.Comp.MaxOutputSlots;

        if (args.Container.Count >= maxSlots && !CanMergeIntoContainer(args.Container, args.EntityUid))
            args.Cancel();
    }

    private void OnGetVerbs(Entity<IndustrialProcessorComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (!_container.TryGetContainer(ent, ent.Comp.OutputContainerId, out var output))
            return;

        if (output.Count == 0)
        {
            args.Verbs.Add(new InteractionVerb
            {
                Text = Loc.GetString("industrial-manual-output-empty"),
                Disabled = true,
            });
            return;
        }

        foreach (var item in output.ContainedEntities)
        {
            var itemCopy = item;
            var user = args.User;
            args.Verbs.Add(new InteractionVerb
            {
                Text = Loc.GetString("industrial-processor-eject", ("item", Identity.Entity(item, EntityManager))),
                Act = () => EjectOutput(ent, itemCopy, user),
            });
        }
    }

    private void OnGetPortVerbs(Entity<IndustrialProcessorComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var held = args.Using;
        if (held == null || !IsPortConfigurator(held.Value))
            return;

        var user = args.User;

        AddPortCycleVerb(args, ent, user, Direction.North, "industrial-port-toggle-north");
        AddPortCycleVerb(args, ent, user, Direction.South, "industrial-port-toggle-south");
        AddPortCycleVerb(args, ent, user, Direction.East, "industrial-port-toggle-east");
        AddPortCycleVerb(args, ent, user, Direction.West, "industrial-port-toggle-west");
    }

    private void AddPortCycleVerb(
        GetVerbsEvent<AlternativeVerb> args,
        Entity<IndustrialProcessorComponent> ent,
        EntityUid user,
        Direction direction,
        string textKey)
    {
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(textKey),
            Act = () => CycleFacePort(ent, user, direction),
        });
    }

    protected void CycleFacePort(Entity<IndustrialProcessorComponent> ent, EntityUid user, Direction direction)
    {
        var mode = ent.Comp.CycleFacePort(direction);

        if (mode == FacePortState.ItemOutput)
            ent.Comp.ClearItemOutputsExcept(direction);

        Dirty(ent);
        _popup.PopupClient(Loc.GetString("industrial-face-port-switched",
            ("direction", GetDirectionName(direction)),
            ("mode", GetFacePortName(mode))), ent, user);
        OnPortModeChanged(ent);
        UpdateUserInterface(ent);
    }

    protected virtual void OnPortModeChanged(Entity<IndustrialProcessorComponent> ent) { }

    protected virtual void UpdateUserInterface(Entity<IndustrialProcessorComponent> ent) { }

    public bool IsPortConfigurator(EntityUid item)
    {
        return HasComp<IndustrialConfiguratorComponent>(item) ||
               _tools.HasQuality(item, IndustrialToolQualities.PortConfiguring);
    }

    public bool TryGetClickedFace(EntityUid processor, EntityCoordinates clickLocation, out Direction direction)
    {
        return IndustrialPortClickZones.TryGetClickedFace(
            processor,
            clickLocation,
            EntityManager,
            _transform,
            out direction);
    }

    private void OnExamined(Entity<IndustrialProcessorComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("industrial-examine-tier", ("tier", GetTierName(ent.Comp.Tier))));
        args.PushMarkup(Loc.GetString("industrial-examine-face-port-north",
            ("mode", GetFacePortName(ent.Comp.NorthFacePort))));
        args.PushMarkup(Loc.GetString("industrial-examine-face-port-south",
            ("mode", GetFacePortName(ent.Comp.SouthFacePort))));
        args.PushMarkup(Loc.GetString("industrial-examine-face-port-east",
            ("mode", GetFacePortName(ent.Comp.EastFacePort))));
        args.PushMarkup(Loc.GetString("industrial-examine-face-port-west",
            ("mode", GetFacePortName(ent.Comp.WestFacePort))));

        var inputCount = GetContainerCount(ent, ent.Comp.InputContainerId);
        var outputCount = GetContainerCount(ent, ent.Comp.OutputContainerId);
        args.PushMarkup(Loc.GetString("industrial-examine-input-buffer",
            ("count", inputCount), ("max", ent.Comp.MaxInputSlots)));
        args.PushMarkup(Loc.GetString("industrial-examine-output-buffer",
            ("count", outputCount), ("max", ent.Comp.MaxOutputSlots)));

        var state = GetState(ent);
        var key = state switch
        {
            IndustrialProcessorState.Working => "industrial-processor-working",
            IndustrialProcessorState.Blocked => "industrial-processor-blocked",
            IndustrialProcessorState.Unpowered => "industrial-processor-unpowered",
            IndustrialProcessorState.Unheated => "industrial-processor-unheated",
            _ => "industrial-processor-idle",
        };

        args.PushMarkup(Loc.GetString(key));

        if (ent.Comp.IsWorking && ent.Comp.ProcessingTime > 0)
        {
            var percent = (int) MathF.Min(100, ent.Comp.ProcessingAccumulator / ent.Comp.ProcessingTime * 100);
            args.PushMarkup(Loc.GetString("industrial-processor-progress", ("percent", percent)));
        }
    }

    private void OnPowerChanged(Entity<IndustrialProcessorComponent> ent, ref PowerChangedEvent args)
    {
        if (!args.Powered)
            _popup.PopupEntity(Loc.GetString("industrial-processor-no-power"), ent);

        UpdateAppearance(ent);
        UpdateUserInterface(ent);

        if (args.Powered && ent.Comp.AutoStart)
            TryStartProcessing(ent);
    }

    protected IndustrialProcessorState GetState(Entity<IndustrialProcessorComponent> ent)
    {
        if (HasComp<IndustrialHeatPoweredComponent>(ent))
        {
            if (!HasSufficientHeat(ent))
                return IndustrialProcessorState.Unheated;
        }
        else if (!IsPowered(ent))
        {
            return IndustrialProcessorState.Unpowered;
        }

        if (ent.Comp.IsWorking)
            return IndustrialProcessorState.Working;

        if (TryMatchRecipe(ent, out var recipe) && recipe != null && !CanAcceptOutputs(ent, recipe))
            return IndustrialProcessorState.Blocked;

        return IndustrialProcessorState.Idle;
    }

    protected bool HasSufficientHeat(Entity<IndustrialProcessorComponent> ent)
    {
        if (!TryComp<IndustrialHeatPoweredComponent>(ent, out var heat))
            return true;

        if (!TryComp<TemperatureComponent>(ent, out var temp))
            return false;

        return temp.CurrentTemperature >= heat.MinTemperature;
    }

    protected bool CanOperate(Entity<IndustrialProcessorComponent> ent)
    {
        if (HasComp<IndustrialHeatPoweredComponent>(ent))
            return HasSufficientHeat(ent);

        if (ent.Comp.CanWorkWithoutPower)
            return true;

        return IsPowered(ent);
    }

    protected bool IsPowered(Entity<IndustrialProcessorComponent> ent)
    {
        if (ent.Comp.CanWorkWithoutPower)
            return true;

        return _power.IsPowered(ent.Owner);
    }

    protected bool IsValidInput(EntityUid item, IndustrialProcessorType processorType)
    {
        if (!RecipesByProcessor.TryGetValue(processorType, out var recipes))
            return false;

        var proto = Prototype(item);
        if (proto == null)
            return false;

        foreach (var recipe in recipes)
        {
            if (recipe.Inputs.ContainsKey(proto.ID))
                return true;
        }

        return false;
    }

    public bool CanAcceptInputProto(Entity<IndustrialProcessorComponent> ent, string protoId)
    {
        if (!RecipesByProcessor.TryGetValue(ent.Comp.ProcessorType, out var recipes))
            return false;

        foreach (var recipe in recipes)
        {
            if (recipe.Inputs.ContainsKey(protoId))
                return true;
        }

        return false;
    }

    protected bool CanMergeIntoContainer(BaseContainer container, EntityUid item)
    {
        if (!TryComp<StackComponent>(item, out var stack))
            return false;

        var proto = Prototype(item);
        if (proto == null)
            return false;

        foreach (var existing in container.ContainedEntities)
        {
            if (Prototype(existing)?.ID != proto.ID || !TryComp<StackComponent>(existing, out var existingStack))
                continue;

            var max = existingStack.MaxCountOverride ?? PrototypeManager.Index(existingStack.StackTypeId).MaxCount;
            if (existingStack.Count + stack.Count <= max)
                return true;
        }

        return false;
    }

    public bool CanMergeProtoIntoContainer(BaseContainer container, string protoId, int amount)
    {
        foreach (var existing in container.ContainedEntities)
        {
            if (Prototype(existing)?.ID != protoId || !TryComp<StackComponent>(existing, out var stack))
                continue;

            var max = stack.MaxCountOverride ?? PrototypeManager.Index(stack.StackTypeId).MaxCount;
            if (stack.Count + amount <= max)
                return true;
        }

        return false;
    }

    protected int GetAvailableCount(EntityUid item)
    {
        if (TryComp<StackComponent>(item, out var stack))
            return stack.Count;

        return 1;
    }

    protected bool TryMatchRecipe(Entity<IndustrialProcessorComponent> ent, out IndustrialRecipePrototype? recipe)
    {
        recipe = null;

        if (!RecipesByProcessor.TryGetValue(ent.Comp.ProcessorType, out var recipes))
            return false;

        if (!_container.TryGetContainer(ent, ent.Comp.InputContainerId, out var input))
            return false;

        foreach (var candidate in recipes)
        {
            var matched = true;

            foreach (var (protoId, amount) in candidate.Inputs)
            {
                var found = false;

                foreach (var contained in input.ContainedEntities)
                {
                    if (Prototype(contained)?.ID != protoId)
                        continue;

                    if (GetAvailableCount(contained) >= amount)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                recipe = candidate;
                return true;
            }
        }

        return false;
    }

    protected bool CanAcceptOutputs(Entity<IndustrialProcessorComponent> ent, IndustrialRecipePrototype recipe)
    {
        if (!_container.TryGetContainer(ent, ent.Comp.OutputContainerId, out var output))
            return false;

        var newSlotsNeeded = 0;

        foreach (var (protoId, amount) in recipe.Outputs)
        {
            var merged = false;

            foreach (var existing in output.ContainedEntities)
            {
                if (Prototype(existing)?.ID != protoId || !TryComp<StackComponent>(existing, out var stack))
                    continue;

                var max = stack.MaxCountOverride ?? PrototypeManager.Index(stack.StackTypeId).MaxCount;
                if (stack.Count + amount <= max)
                {
                    merged = true;
                    break;
                }
            }

            if (!merged)
                newSlotsNeeded++;
        }

        return output.Count + newSlotsNeeded <= ent.Comp.MaxOutputSlots;
    }

    public bool CanAcceptInputItem(Entity<IndustrialProcessorComponent> ent, string protoId, int amount = 1)
    {
        if (!CanAcceptInputProto(ent, protoId))
            return false;

        if (!_container.TryGetContainer(ent, ent.Comp.InputContainerId, out var input))
            return false;

        if (input.Count < ent.Comp.MaxInputSlots)
            return true;

        return CanMergeProtoIntoContainer(input, protoId, amount);
    }

    protected int GetContainerCount(Entity<IndustrialProcessorComponent> ent, string containerId)
    {
        if (!_container.TryGetContainer(ent, containerId, out var container))
            return 0;

        return container.Count;
    }

    public static string GetFacePortName(FacePortState state)
    {
        return Robust.Shared.Localization.Loc.GetString(state switch
        {
            FacePortState.ItemInput => "industrial-face-port-item-input",
            FacePortState.ItemOutput => "industrial-face-port-item-output",
            FacePortState.LiquidInput => "industrial-face-port-liquid-input",
            FacePortState.LiquidOutput => "industrial-face-port-liquid-output",
            FacePortState.HeatInput => "industrial-face-port-heat-input",
            _ => "industrial-face-port-disabled",
        });
    }

    public static string GetDirectionName(Direction direction)
    {
        return Robust.Shared.Localization.Loc.GetString(direction switch
        {
            Direction.North => "industrial-direction-north",
            Direction.South => "industrial-direction-south",
            Direction.East => "industrial-direction-east",
            _ => "industrial-direction-west",
        });
    }

    protected static string GetTierName(MachineTier tier)
    {
        return Robust.Shared.Localization.Loc.GetString(tier switch
        {
            MachineTier.Basic => "industrial-machine-tier-basic",
            MachineTier.Perfect => "industrial-machine-tier-perfect",
            _ => "industrial-machine-tier-industrial",
        });
    }

    protected abstract void TryStartProcessing(Entity<IndustrialProcessorComponent> ent);
    protected abstract void EjectOutput(Entity<IndustrialProcessorComponent> ent, EntityUid item, EntityUid user);
    protected abstract void UpdateAppearance(Entity<IndustrialProcessorComponent> ent);
}
