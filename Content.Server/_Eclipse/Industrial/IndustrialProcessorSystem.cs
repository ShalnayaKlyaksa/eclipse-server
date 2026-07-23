using System.Linq;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Stack;
using Content.Shared._Eclipse.Industrial;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.Map.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;

namespace Content.Server._Eclipse.Industrial;

public sealed partial class IndustrialProcessorSystem : SharedIndustrialProcessorSystem
{
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private ItemPipeNetworkSystem _pipeNetwork = default!;
    [Dependency] private LiquidPipeNetworkSystem _liquidPipeNetwork = default!;
    [Dependency] private ItemPipeSystem _itemPipes = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IndustrialProcessorComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<IndustrialProcessorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<IndustrialProcessorComponent, IndustrialProcessorSlotMessage>(OnSlotMessage);
        SubscribeLocalEvent<IndustrialProcessorComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<IndustrialProcessorComponent, BeforeActivatableUIOpenEvent>(OnBeforeUiOpen);
    }

    private void OnComponentInit(Entity<IndustrialProcessorComponent> ent, ref ComponentInit args)
    {
        ApplyTierSettings(ent);
        UpdateAppearance(ent);
        var procConnect = EntityManager.System<SharedIndustrialProcessorPipeConnectSystem>();
        if (procConnect.TryAutoBindPortsFromAdjacentPipes(ent))
            UpdateAdjacentPipeConnections(ent);

        EntityManager.System<SharedIndustrialHeatConnectSystem>().TryAutoBindPortsFromAdjacentBuffers(ent);
    }

    private void OnMapInit(Entity<IndustrialProcessorComponent> ent, ref MapInitEvent args)
    {
        ApplyTierSettings(ent);
        UpdateAppearance(ent);
        var procConnect = EntityManager.System<SharedIndustrialProcessorPipeConnectSystem>();
        if (procConnect.TryAutoBindPortsFromAdjacentPipes(ent))
            UpdateAdjacentPipeConnections(ent);

        EntityManager.System<SharedIndustrialHeatConnectSystem>().TryAutoBindPortsFromAdjacentBuffers(ent);
    }

    private void OnBeforeUiOpen(Entity<IndustrialProcessorComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        UpdateUserInterface(ent);
    }

    private void OnUiOpened(Entity<IndustrialProcessorComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUserInterface(ent);
    }

    private void OnSlotMessage(Entity<IndustrialProcessorComponent> ent, ref IndustrialProcessorSlotMessage args)
    {
        var actor = args.Actor;

        var containerId = args.IsInput ? ent.Comp.InputContainerId : ent.Comp.OutputContainerId;
        if (!_container.TryGetContainer(ent, containerId, out var container))
            return;

        var items = container.ContainedEntities.ToList();

        if (args.SlotIndex < items.Count)
        {
            var item = items[args.SlotIndex];
            if (!args.IsInput)
            {
                EjectOutput(ent, item, actor);
                return;
            }

            _container.Remove(item, container);
            _hands.PickupOrDrop(actor, item, checkActionBlocker: false);
            UpdateUserInterface(ent);
            return;
        }

        if (!args.IsInput)
            return;

        if (!_hands.TryGetActiveItem(actor, out var held) || held == null)
            return;

        if (!IsValidInput(held.Value, ent.Comp.ProcessorType))
        {
            _popup.PopupCursor(Loc.GetString("industrial-processor-wrong-input"), actor);
            return;
        }

        if (container.Count >= ent.Comp.MaxInputSlots && !CanMergeIntoContainer(container, held.Value))
            return;

        if (!_hands.TryDropIntoContainer(actor, held.Value, container))
            return;

        _popup.PopupCursor(Loc.GetString("industrial-manual-insert-success"), actor);

        if (ent.Comp.AutoStart)
            TryStartProcessing(ent);

        UpdateUserInterface(ent);
    }

    private void ApplyTierSettings(Entity<IndustrialProcessorComponent> ent)
    {
        var specs = MachineTierHelper.GetSpecs(ent.Comp.Tier);

        ent.Comp.ProcessingSpeedMultiplier = specs.ProcessingSpeedMultiplier;
        ent.Comp.MaxInputSlots = specs.MaxInputSlots;
        ent.Comp.MaxOutputSlots = specs.MaxOutputSlots;
        ent.Comp.MaxAutoTransferPerSecond = specs.MaxAutoTransferPerSecond;

        if (TryComp<ApcPowerReceiverComponent>(ent, out var receiver))
        {
            var baseLoad = ent.Comp.BasePowerLoad > 0 ? ent.Comp.BasePowerLoad : receiver.Load;
            ent.Comp.BasePowerLoad = baseLoad;
            receiver.Load = baseLoad * specs.PowerMultiplier;
        }

        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<IndustrialProcessorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var ent = (uid, comp);

            if (!comp.IsWorking)
            {
                UpdateAppearance(ent);
                continue;
            }

            if (!CanOperate(ent))
            {
                UpdateAppearance(ent);
                continue;
            }

            comp.ProcessingAccumulator += frameTime * comp.ProcessingSpeedMultiplier;
            Dirty(uid, comp);

            if (comp.ProcessingAccumulator < comp.ProcessingTime)
            {
                UpdateUserInterface(ent);
                continue;
            }

            if (!TryCompleteRecipe(ent))
            {
                comp.IsWorking = false;
                comp.ProcessingAccumulator = 0;
                comp.CurrentRecipe = null;
                Dirty(uid, comp);
            }

            UpdateAppearance(ent);
            UpdateUserInterface(ent);
        }
    }

    protected override void TryStartProcessing(Entity<IndustrialProcessorComponent> ent)
    {
        if (ent.Comp.IsWorking)
            return;

        if (!CanOperate(ent))
            return;

        if (!TryMatchRecipe(ent, out var recipe) || recipe == null)
            return;

        if (!CanAcceptOutputs(ent, recipe))
        {
            _popup.PopupEntity(Loc.GetString("industrial-processor-output-full"), ent);
            UpdateAppearance(ent);
            return;
        }

        ent.Comp.CurrentRecipe = recipe.ID;
        ent.Comp.ProcessingTime = recipe.Time;
        ent.Comp.ProcessingAccumulator = 0;
        ent.Comp.IsWorking = true;
        Dirty(ent);

        _popup.PopupEntity(Loc.GetString("industrial-processor-started"), ent);
        UpdateAppearance(ent);
        UpdateUserInterface(ent);
    }

    private bool TryCompleteRecipe(Entity<IndustrialProcessorComponent> ent)
    {
        if (ent.Comp.CurrentRecipe is not { } recipeId ||
            !PrototypeManager.TryIndex(recipeId, out IndustrialRecipePrototype? recipe))
        {
            return false;
        }

        if (!CanOperate(ent))
            return false;

        if (!TryMatchRecipe(ent, out var matched) || matched?.ID != recipe.ID)
            return false;

        if (!CanAcceptOutputs(ent, recipe))
        {
            _popup.PopupEntity(Loc.GetString("industrial-processor-output-full"), ent);
            return false;
        }

        if (!ConsumeInputs(ent, recipe))
            return false;

        if (!ProduceOutputs(ent, recipe))
            return false;

        ent.Comp.IsWorking = false;
        ent.Comp.ProcessingAccumulator = 0;
        ent.Comp.CurrentRecipe = null;
        Dirty(ent);

        _popup.PopupEntity(Loc.GetString("industrial-processor-finished"), ent);

        if (ent.Comp.AutoStart)
            TryStartProcessing(ent);

        return true;
    }

    private bool ConsumeInputs(Entity<IndustrialProcessorComponent> ent, IndustrialRecipePrototype recipe)
    {
        if (!_container.TryGetContainer(ent, ent.Comp.InputContainerId, out var input))
            return false;

        foreach (var (protoId, amount) in recipe.Inputs)
        {
            EntityUid? target = null;

            foreach (var contained in input.ContainedEntities)
            {
                if (Prototype(contained)?.ID != protoId)
                    continue;

                if (GetAvailableCount(contained) >= amount)
                {
                    target = contained;
                    break;
                }
            }

            if (target == null)
                return false;

            if (TryComp<StackComponent>(target, out var stack))
            {
                if (stack.Count > amount)
                {
                    _stack.SetCount((target.Value, stack), stack.Count - amount);
                    continue;
                }
            }

            _container.Remove(target.Value, input);
            Del(target.Value);
        }

        return true;
    }

    private bool ProduceOutputs(Entity<IndustrialProcessorComponent> ent, IndustrialRecipePrototype recipe)
    {
        if (!_container.TryGetContainer(ent, ent.Comp.OutputContainerId, out var output))
            return false;

        foreach (var (protoId, amount) in recipe.Outputs)
        {
            var merged = false;

            foreach (var existing in output.ContainedEntities)
            {
                if (Prototype(existing)?.ID != protoId || !TryComp<StackComponent>(existing, out var stack))
                    continue;

                var max = stack.MaxCountOverride ?? PrototypeManager.Index(stack.StackTypeId).MaxCount;
                if (stack.Count + amount > max)
                    continue;

                _stack.SetCount((existing, stack), stack.Count + amount);
                merged = true;
                break;
            }

            if (merged)
                continue;

            var spawned = Spawn(protoId, Transform(ent).Coordinates);

            if (TryComp<StackComponent>(spawned, out var spawnedStack))
                _stack.SetCount((spawned, spawnedStack), amount);

            if (!_container.Insert(spawned, output))
            {
                Del(spawned);
                return false;
            }
        }

        return true;
    }

    public bool TryPipeTransfer(Entity<IndustrialProcessorComponent> source, Entity<IndustrialProcessorComponent> sink, string protoId)
    {
        if (!CanAcceptInputItem(sink, protoId))
            return false;

        if (!_container.TryGetContainer(source, source.Comp.OutputContainerId, out var output))
            return false;

        if (!_container.TryGetContainer(sink, sink.Comp.InputContainerId, out var input))
            return false;

        EntityUid? outputItem = null;

        foreach (var contained in output.ContainedEntities)
        {
            if (Prototype(contained)?.ID == protoId && GetAvailableCount(contained) > 0)
            {
                outputItem = contained;
                break;
            }
        }

        if (outputItem == null)
            return false;

        if (!TryRemoveOneUnit(outputItem.Value, output))
            return false;

        if (!TryInsertOneUnit(sink, input, protoId))
        {
            RestoreOneUnitToOutput(output, protoId, source);
            return false;
        }

        if (sink.Comp.AutoStart)
            TryStartProcessing(sink);

        return true;
    }

    public bool TryGetFirstOutputProto(Entity<IndustrialProcessorComponent> ent, out string protoId)
    {
        protoId = string.Empty;

        if (!_container.TryGetContainer(ent, ent.Comp.OutputContainerId, out var output))
            return false;

        foreach (var contained in output.ContainedEntities)
        {
            var proto = Prototype(contained);
            if (proto == null || GetAvailableCount(contained) <= 0)
                continue;

            protoId = proto.ID;
            return true;
        }

        return false;
    }

    private bool TryInsertOneUnit(Entity<IndustrialProcessorComponent> ent, BaseContainer input, string protoId)
    {
        foreach (var existing in input.ContainedEntities)
        {
            if (Prototype(existing)?.ID != protoId || !TryComp<StackComponent>(existing, out var stack))
                continue;

            var max = stack.MaxCountOverride ?? PrototypeManager.Index(stack.StackTypeId).MaxCount;
            if (stack.Count + 1 > max)
                continue;

            _stack.SetCount((existing, stack), stack.Count + 1);
            return true;
        }

        if (input.Count >= ent.Comp.MaxInputSlots)
            return false;

        var spawned = Spawn(protoId, Transform(ent).Coordinates);

        if (TryComp<StackComponent>(spawned, out var spawnedStack))
            _stack.SetCount((spawned, spawnedStack), 1);

        if (!_container.Insert(spawned, input))
        {
            Del(spawned);
            return false;
        }

        return true;
    }

    private bool TryRemoveOneUnit(EntityUid item, BaseContainer container)
    {
        if (TryComp<StackComponent>(item, out var stack))
        {
            if (stack.Count > 1)
            {
                _stack.SetCount((item, stack), stack.Count - 1);
                return true;
            }
        }

        _container.Remove(item, container);
        Del(item);
        return true;
    }

    private void RestoreOneUnitToOutput(BaseContainer output, string protoId, EntityUid coordOwner)
    {
        foreach (var existing in output.ContainedEntities)
        {
            if (Prototype(existing)?.ID != protoId || !TryComp<StackComponent>(existing, out var stack))
                continue;

            var max = stack.MaxCountOverride ?? PrototypeManager.Index(stack.StackTypeId).MaxCount;
            if (stack.Count + 1 <= max)
            {
                _stack.SetCount((existing, stack), stack.Count + 1);
                return;
            }
        }

        var spawned = Spawn(protoId, Transform(coordOwner).Coordinates);
        if (TryComp<StackComponent>(spawned, out var spawnedStack))
            _stack.SetCount((spawned, spawnedStack), 1);

        if (!_container.Insert(spawned, output))
            Del(spawned);
    }

    protected override void EjectOutput(Entity<IndustrialProcessorComponent> ent, EntityUid item, EntityUid user)
    {
        if (!_container.TryGetContainer(ent, ent.Comp.OutputContainerId, out var output))
            return;

        if (!output.Contains(item))
            return;

        _container.Remove(item, output);
        _hands.PickupOrDrop(user, item, checkActionBlocker: false);

        if (ent.Comp.AutoStart)
            TryStartProcessing(ent);

        UpdateAppearance(ent);
        UpdateUserInterface(ent);
    }

    protected override void UpdateAppearance(Entity<IndustrialProcessorComponent> ent)
    {
        _appearance.SetData(ent, IndustrialProcessorVisuals.State, GetState(ent));
    }

    protected override void OnPortModeChanged(Entity<IndustrialProcessorComponent> ent)
    {
        _pipeNetwork.RebuildNetworksNearProcessor(ent);
        _liquidPipeNetwork.RebuildNetworksNearProcessor(ent);
        UpdateAdjacentPipeConnections(ent);
    }

    private void UpdateAdjacentPipeConnections(Entity<IndustrialProcessorComponent> ent)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not EntityUid gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var pos = EntityManager.System<SharedMapSystem>().TileIndicesFor(gridUid, grid, xform.Coordinates);
        var map = EntityManager.System<SharedMapSystem>();

        foreach (var direction in ItemPipeConnectionHelper.CardinalDirections)
        {
            var enumerator = map.GetAnchoredEntitiesEnumerator(gridUid, grid, pos.Offset(direction));
            while (enumerator.MoveNext(out var entity) && entity != null)
            {
                if (!TryComp<ItemPipeComponent>(entity, out var pipe))
                    continue;

                _itemPipes.UpdateConnections((entity.Value, pipe));
            }
        }
    }

    protected override void UpdateUserInterface(Entity<IndustrialProcessorComponent> ent)
    {
        var state = GetState(ent);
        var stateKey = state switch
        {
            IndustrialProcessorState.Working => "industrial-processor-working",
            IndustrialProcessorState.Blocked => "industrial-processor-blocked",
            IndustrialProcessorState.Unpowered => "industrial-processor-unpowered",
            IndustrialProcessorState.Unheated => "industrial-processor-unheated",
            _ => "industrial-processor-idle",
        };

        var progress = ent.Comp.IsWorking && ent.Comp.ProcessingTime > 0
            ? MathF.Min(1f, ent.Comp.ProcessingAccumulator / ent.Comp.ProcessingTime)
            : 0f;

        string? recipeName = null;
        if (ent.Comp.CurrentRecipe != null &&
            PrototypeManager.TryIndex(ent.Comp.CurrentRecipe, out IndustrialRecipePrototype? recipe))
        {
            recipeName = GetRecipeDisplayName(recipe);
        }

        var usesHeat = HasComp<IndustrialHeatPoweredComponent>(ent);
        var hasHeat = HasSufficientHeat(ent);

        _ui.SetUiState(ent.Owner, IndustrialProcessorUiKey.Key, new IndustrialProcessorBoundUserInterfaceState(
            Name(ent),
            GetTierName(ent.Comp.Tier),
            stateKey,
            progress,
            recipeName,
            IsPowered(ent),
            usesHeat,
            hasHeat,
            ent.Comp.MaxInputSlots,
            ent.Comp.MaxOutputSlots,
            BuildSlotStates(ent, ent.Comp.InputContainerId, ent.Comp.MaxInputSlots),
            BuildSlotStates(ent, ent.Comp.OutputContainerId, ent.Comp.MaxOutputSlots),
            BuildProcessingSlot(ent),
            ent.Comp.NorthFacePort,
            ent.Comp.SouthFacePort,
            ent.Comp.EastFacePort,
            ent.Comp.WestFacePort));
    }

    private IndustrialProcessorSlotState BuildProcessingSlot(Entity<IndustrialProcessorComponent> ent)
    {
        if (!ent.Comp.IsWorking || ent.Comp.CurrentRecipe is not { } recipeId ||
            !PrototypeManager.TryIndex(recipeId, out IndustrialRecipePrototype? recipe))
        {
            return new IndustrialProcessorSlotState(null, 0, string.Empty);
        }

        foreach (var (protoId, amount) in recipe.Inputs)
        {
            if (!PrototypeManager.TryIndex<EntityPrototype>(protoId, out var proto))
                continue;

            return new IndustrialProcessorSlotState(protoId, amount, proto.Name);
        }

        return new IndustrialProcessorSlotState(null, 0, string.Empty);
    }

    private string GetRecipeDisplayName(IndustrialRecipePrototype recipe)
    {
        foreach (var protoId in recipe.Outputs.Keys)
        {
            if (PrototypeManager.TryIndex<EntityPrototype>(protoId, out var proto))
                return proto.Name;
        }

        return recipe.ID;
    }

    private IndustrialProcessorSlotState[] BuildSlotStates(
        Entity<IndustrialProcessorComponent> ent,
        string containerId,
        int maxSlots)
    {
        var slots = new IndustrialProcessorSlotState[maxSlots];

        if (!_container.TryGetContainer(ent, containerId, out var container))
            return slots;

        var index = 0;
        foreach (var item in container.ContainedEntities)
        {
            if (index >= maxSlots)
                break;

            var proto = Prototype(item);
            var count = GetAvailableCount(item);
            slots[index] = new IndustrialProcessorSlotState(
                proto?.ID,
                count,
                Identity.Name(item, EntityManager));
            index++;
        }

        for (; index < maxSlots; index++)
            slots[index] = new IndustrialProcessorSlotState(null, 0, string.Empty);

        return slots;
    }
}
