using System.Linq;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared.Construction.EntitySystems;
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

public abstract partial class SharedIndustrialProcessorSystem : EntitySystem
{
    [Dependency] protected IPrototypeManager PrototypeManager = default!;
    [Dependency] protected IndustrialResourceSystem Resources = default!;
    [Dependency] private SharedStackSystem _stacks = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private SharedToolSystem _tools = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public static Direction[] CardinalDirections =
        [Direction.North, Direction.South, Direction.East, Direction.West];

    protected Dictionary<IndustrialProcessorType, List<IndustrialRecipePrototype>> RecipesByProcessor = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IndustrialProcessorComponent, InteractUsingEvent>(OnInteractUsing, after: [typeof(AnchorableSystem)]);
        SubscribeLocalEvent<IndustrialProcessorComponent, AfterInteractUsingEvent>(OnAfterInteractUsing);
        SubscribeLocalEvent<IndustrialProcessorComponent, EntInsertedIntoContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<IndustrialProcessorComponent, EntRemovedFromContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<IndustrialProcessorComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<IndustrialProcessorComponent, GetVerbsEvent<InteractionVerb>>(OnGetVerbs);
        SubscribeLocalEvent<IndustrialProcessorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<IndustrialProcessorComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<IndustrialProcessorComponent, GetVerbsEvent<AlternativeVerb>>(OnGetPortVerbs);

        RebuildRecipeIndex();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => RebuildRecipeIndex());
    }

    private void RebuildRecipeIndex()
    {
        RecipesByProcessor.Clear();
        // A supplied extra ingredient should select its specialised process rather than be ignored by a simpler recipe.
        foreach (var recipe in PrototypeManager.EnumeratePrototypes<IndustrialRecipePrototype>()
                     .OrderByDescending(r => r.Inputs.Count + r.FluidInputs.Count + r.Bath.Count).ThenBy(r => r.ID))
        {
            if (!RecipesByProcessor.TryGetValue(recipe.Processor, out var list))
            {
                list = new List<IndustrialRecipePrototype>();
                RecipesByProcessor[recipe.Processor] = list;
            }

            list.Add(recipe);
        }
        var query = EntityQueryEnumerator<IndustrialProcessorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            comp.RecipeCatalog = null;
            UpdateUserInterface((uid, comp));
        }
    }

    private void OnInteractUsing(Entity<IndustrialProcessorComponent> ent, ref InteractUsingEvent args)
    {
        if (HasComp<IndustrialDataDiskComponent>(args.Used))
            return;
        if (args.Handled)
            return;
        if (TryComp<IndustrialUpgradeModuleComponent>(args.Used, out var module))
        {
            TryUpgradeMachine(ent, args.User, args.Used, module);
            args.Handled = true;
            return;
        }

        if (IsPortConfigurator(args.Used))
        {
            if (TryGetClickedFace(ent, args.ClickLocation, out var direction))
                CycleFacePort(ent, args.User, direction);

            args.Handled = true;
            return;
        }

        if (HasComp<IndustrialFluidProcessorComponent>(ent) &&
            HasComp<Content.Shared.Chemistry.Components.SolutionManager.SolutionContainerManagerComponent>(args.Used))
        {
            // Keep the native filled-container pouring priority; empty containers choose a machine tank.
            if (TryComp<Content.Shared.Chemistry.Components.SolutionTransferComponent>(args.Used, out var transfer) &&
                transfer.CanReceive && Solutions.TryGetRefillableSolution(args.Used, out _, out var heldSolution) &&
                heldSolution.Volume == 0 && heldSolution.AvailableVolume > 0)
            {
                args.Handled = true;
                OpenFluidSelection(ent, args.User, args.Used);
            }
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

        if (!TryInsertHeld(ent, args.User, args.Used))
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

        if (ent.Comp.InTransaction)
            return;
        ent.Comp.WaitingForOutput = false;

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

        if (args.Container.Count >= maxSlots)
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
        var mode = ent.Comp.GetFacePort(direction) switch
        {
            FacePortState.Disabled when ent.Comp.ProcessorType == IndustrialProcessorType.FluidTank => FacePortState.LiquidInput,
            FacePortState.Disabled => FacePortState.ItemInput,
            FacePortState.ItemInput => FacePortState.ItemOutput,
            FacePortState.ItemOutput when HasComp<IndustrialFluidProcessorComponent>(ent) => FacePortState.LiquidInput,
            FacePortState.LiquidInput => FacePortState.LiquidOutput,
            FacePortState.LiquidOutput when HasComp<IndustrialHeatPoweredComponent>(ent) => FacePortState.HeatInput,
            FacePortState.ItemOutput when HasComp<IndustrialHeatPoweredComponent>(ent) => FacePortState.HeatInput,
            _ => FacePortState.Disabled,
        };
        ent.Comp.SetFacePort(direction, mode);


        Dirty(ent);
        _popup.PopupClient(Loc.GetString("industrial-face-port-switched",
            ("direction", GetDirectionName(direction)),
            ("mode", GetFacePortName(mode))), ent, user);
        OnPortModeChanged(ent);
        UpdateUserInterface(ent);
    }

    protected virtual void TryUpgradeMachine(Entity<IndustrialProcessorComponent> ent, EntityUid user,
        EntityUid moduleUid, IndustrialUpgradeModuleComponent module) { }

    protected virtual void OnPortModeChanged(Entity<IndustrialProcessorComponent> ent) { }

    protected virtual void OpenFluidSelection(Entity<IndustrialProcessorComponent> ent, EntityUid user, EntityUid container) { }

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
        if (ent.Comp.ProcessorType == IndustrialProcessorType.Buffer)
        {
            args.PushMarkup(Loc.GetString("industrial-examine-storage", ("count", inputCount), ("max", ent.Comp.MaxInputSlots)));
            return;
        }

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
            IndustrialProcessorState.Paused => "industrial-processor-paused",
            IndustrialProcessorState.Unresearched => "industrial-processor-unresearched",
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
        if (!args.Powered && ent.Comp.ProcessorType != IndustrialProcessorType.Buffer)
            _popup.PopupEntity(Loc.GetString("industrial-processor-no-power"), ent);

        UpdateAppearance(ent);
        UpdateUserInterface(ent);

        if (args.Powered && ent.Comp.AutoStart)
            TryStartProcessing(ent);
    }

    protected IndustrialProcessorState GetState(Entity<IndustrialProcessorComponent> ent)
    {
        if (IsEnvironmentBlocked(ent))
            return IndustrialProcessorState.Blocked;
        if (!ent.Comp.Enabled)
            return IndustrialProcessorState.Paused;
        if (TryComp<IndustrialExtractorComponent>(ent, out var extractor))
            return extractor.State;
        if (!HasAvailableRecipe(ent))
            return IndustrialProcessorState.Unresearched;

        if (HasComp<IndustrialHeatPoweredComponent>(ent))
        {
            if (!HasSufficientHeat(ent))
                return IndustrialProcessorState.Unheated;
        }
        else if (!IsPowered(ent))
        {
            return IndustrialProcessorState.Unpowered;
        }

        if (ent.Comp.WaitingForOutput)
            return IndustrialProcessorState.Blocked;
        if (TryComp<IndustrialManualProcessorComponent>(ent, out var manual) && manual.Busy)
            return IndustrialProcessorState.Working;
        if (ent.Comp.IsWorking)
        {
            if (ent.Comp.ProcessingAccumulator >= ent.Comp.ProcessingTime &&
                ent.Comp.CurrentRecipe is { } current && PrototypeManager.TryIndex(current, out var active) &&
                !CanAcceptOutputs(ent, active))
                return IndustrialProcessorState.Blocked;
            return IndustrialProcessorState.Working;
        }

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

    private bool HasAvailableRecipe(Entity<IndustrialProcessorComponent> ent)
    {
        var id = ent.Comp.CurrentRecipe ?? ent.Comp.SelectedRecipe;
        return id == null || PrototypeManager.TryIndex(id.Value, out IndustrialRecipePrototype? recipe) &&
            IsRecipeAvailable(ent, recipe);
    }

    protected bool CanOperate(Entity<IndustrialProcessorComponent> ent)
    {
        if (!ent.Comp.Enabled || !HasAvailableRecipe(ent))
            return false;

        if ((ent.Comp.CurrentRecipe ?? ent.Comp.SelectedRecipe) is { } id &&
            PrototypeManager.TryIndex(id, out IndustrialRecipePrototype? recipe) && !HasProcessEnvironment(ent, recipe))
            return false;

        if (HasComp<IndustrialHeatPoweredComponent>(ent))
            return HasSufficientHeat(ent);

        if (ent.Comp.CanWorkWithoutPower)
            return true;

        return IsPowered(ent);
    }

    protected bool HasProcessEnvironment(Entity<IndustrialProcessorComponent> ent, IndustrialRecipePrototype recipe)
        => recipe.CleanroomGrade <= 0 || TryComp<RoomSanitationComponent>(ent, out var room) && room.Grade >= recipe.CleanroomGrade;

    protected bool IsEnvironmentBlocked(Entity<IndustrialProcessorComponent> ent)
    {
        if (ent.Comp.ProcessorType == IndustrialProcessorType.Buffer)
            return false;
        if ((ent.Comp.CurrentRecipe ?? ent.Comp.SelectedRecipe) is { } id && PrototypeManager.TryIndex(id, out IndustrialRecipePrototype? recipe))
            return !HasProcessEnvironment(ent, recipe);
        return TryMatchRecipe(ent, out var matched) && matched != null && !HasProcessEnvironment(ent, matched);
    }

    protected bool IsPowered(Entity<IndustrialProcessorComponent> ent)
    {
        if (ent.Comp.CanWorkWithoutPower)
            return true;

        return _power.IsPowered(ent.Owner);
    }

    protected bool IsValidInput(EntityUid item, IndustrialProcessorType processorType)
    {
        if (processorType == IndustrialProcessorType.Buffer)
            return HasComp<Content.Shared.Item.ItemComponent>(item);

        if (!RecipesByProcessor.TryGetValue(processorType, out var recipes))
            return false;

        var proto = Prototype(item);
        if (proto == null)
            return false;

        foreach (var recipe in recipes)
        {
            if (recipe.Inputs.Keys.Any(id => Resources.Key(id) == Resources.Key(proto.ID)))
                return true;
        }

        return false;
    }

    public bool CanAcceptInputProto(Entity<IndustrialProcessorComponent> ent, string protoId)
    {
        if (ent.Comp.ProcessorType == IndustrialProcessorType.Buffer)
            return PrototypeManager.TryIndex<EntityPrototype>(protoId, out var proto) &&
                   proto.TryGetComponent<Content.Shared.Item.ItemComponent>(out _, EntityManager.ComponentFactory);
        if (!RecipesByProcessor.TryGetValue(ent.Comp.ProcessorType, out var recipes))
            return false;
        foreach (var recipe in recipes)
        {
            if (ent.Comp.SelectedRecipe is { } selected && selected.Id != recipe.ID)
                continue;
            if (recipe.Inputs.Keys.Any(id => Resources.Key(id) == Resources.Key(protoId)))
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

        long free = 0;
        foreach (var existing in container.ContainedEntities)
        {
            if (!Resources.Matches(existing, proto.ID) || !TryComp<StackComponent>(existing, out var other))
                continue;
            free += _stacks.GetAvailableSpace(other);
        }
        return free >= stack.Count;
    }

    public bool CanMergeProtoIntoContainer(BaseContainer container, string protoId, int amount)
    {
        foreach (var existing in container.ContainedEntities)
        {
            if (!Resources.Matches(existing, protoId) || !TryComp<StackComponent>(existing, out var stack))
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

        if (ent.Comp.ProcessorType == IndustrialProcessorType.Buffer ||
            !RecipesByProcessor.TryGetValue(ent.Comp.ProcessorType, out var recipes))
            return false;

        if (!_container.TryGetContainer(ent, ent.Comp.InputContainerId, out var input))
            return false;

        foreach (var candidate in recipes)
        {
            if (ent.Comp.SelectedRecipe is { } selected ? candidate.ID != selected.Id : candidate.RequireSelection)
                continue;
            if (!IsRecipeAvailable(ent, candidate) || !Resources.CanAfford(input.ContainedEntities, candidate.Inputs) ||
                !HasFluidIngredients(ent, candidate))
                continue;
            recipe = candidate;
            return true;
        }
        return false;
    }

    protected bool IsRecipeAvailable(Entity<IndustrialProcessorComponent> ent, IndustrialRecipePrototype recipe)
    {
        if (recipe.MinimumTier > ent.Comp.Tier)
            return false;
        if (recipe.RequiredTechnology is not { } technology)
            return true;
        if (TryComp<IndustrialMachineKnowledgeComponent>(ent, out var knowledge) && knowledge.Technologies.Contains(technology))
            return true;
        // New factory branches require a physical hand-off from science.
        return !recipe.RequireDataDisk && !technology.Id.StartsWith("Industrial", StringComparison.Ordinal) &&
            EntityManager.System<Content.Shared.Research.Systems.SharedResearchSystem>().HasTechnology(ent, technology);
    }

    protected bool CanAcceptOutputs(Entity<IndustrialProcessorComponent> ent, IndustrialRecipePrototype recipe)
    {
        if (!CanAcceptFluidOutputs(ent, recipe))
            return false;
        if (!_container.TryGetContainer(ent, ent.Comp.OutputContainerId, out var output))
            return false;

        var available = new Dictionary<string, long>();
        foreach (var item in output.ContainedEntities)
        {
            if (!TryComp<StackComponent>(item, out var stack) || Prototype(item) is not { } proto)
                continue;
            var key = Resources.Key(proto.ID);
            available[key] = available.GetValueOrDefault(key) + _stacks.GetMaxCount(stack) - stack.Count;
        }
        var slotsNeeded = 0L;
        foreach (var (prototype, amount) in recipe.Outputs)
        {
            if (amount <= 0)
                return false;
            if (!Resources.TryGetStack(prototype, out var stack))
            {
                slotsNeeded += amount;
                continue;
            }
            var key = Resources.Key(prototype);
            var free = available.GetValueOrDefault(key);
            var merged = Math.Min(free, amount);
            available[key] = free - merged;
            var remaining = amount - merged;
            var max = _stacks.GetMaxCount(stack);
            var slots = (remaining + max - 1L) / max;
            slotsNeeded += slots;
            available[key] += slots * max - remaining;
        }
        return output.Count + slotsNeeded <= ent.Comp.MaxOutputSlots;
    }

    protected bool TryInsertHeld(Entity<IndustrialProcessorComponent> ent, EntityUid user, EntityUid item)
    {
        if (Prototype(item) is not { } prototype || !CanAcceptInputProto(ent, prototype.ID) ||
            !_container.TryGetContainer(ent, ent.Comp.InputContainerId, out var input))
            return false;
        if (input.Count < ent.Comp.MaxInputSlots)
            return _hands.TryDropIntoContainer(user, item, input);
        if (!CanMergeIntoContainer(input, item) || !_hands.TryDrop(user, item))
            return false;
        foreach (var existing in input.ContainedEntities)
        {
            if (!TryComp<StackComponent>(existing, out var recipient) || !TryComp<StackComponent>(item, out var donor))
                continue;
            _stacks.TryMergeStacks((item, donor), (existing, recipient), out _);
            if (donor.Count == 0)
            {
                TryStartProcessing(ent);
                UpdateUserInterface(ent);
                return true;
            }
        }
        _hands.PickupOrDrop(user, item);
        return false;
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
