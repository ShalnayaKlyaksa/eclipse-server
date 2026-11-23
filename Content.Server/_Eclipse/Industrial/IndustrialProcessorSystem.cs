using Robust.Shared.Map;
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
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private ItemPipeNetworkSystem _pipeNetwork = default!;
    [Dependency] private ItemPipeSystem _itemPipes = default!;
    [Dependency] private readonly LiquidPipeNetworkSystem _liquidPipes = default!;
    [Dependency] private readonly Content.Server.Temperature.Systems.TemperatureSystem _temperature = default!;

    public override void Initialize()
    {
        base.Initialize();
        InitializeUi();
        InitializeManual();
        SubscribeLocalEvent<IndustrialProcessorComponent, Content.Shared.Chemistry.EntitySystems.SolutionContainerChangedEvent>(OnFluidContentsChanged);

        SubscribeLocalEvent<IndustrialProcessorComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<IndustrialProcessorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<IndustrialProcessorComponent, Content.Shared.Temperature.OnTemperatureChangeEvent>(OnTemperatureChanged);
        SubscribeLocalEvent<StackComponent, StackCountChangedEvent>(OnStoredStackChanged);
        SubscribeLocalEvent<IndustrialProcessorComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<IndustrialProcessorComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<IndustrialProcessorComponent, Content.Shared.Research.Components.TechnologyDatabaseModifiedEvent>(OnResearchChanged);
        SubscribeLocalEvent<IndustrialProcessorComponent, Content.Shared.Research.Components.TechnologyDatabaseSynchronizedEvent>(OnResearchSynced);
        SubscribeLocalEvent<IndustrialProcessorComponent, Content.Shared.Research.Components.ResearchRegistrationChangedEvent>(OnResearchRegistrationChanged);
    }

    private void OnComponentInit(Entity<IndustrialProcessorComponent> ent, ref ComponentInit args)
    {
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
        _pipeNetwork.InvalidateProcessorConnections(ent);
        TryStartProcessing(ent);
    }

    private void OnTemperatureChanged(Entity<IndustrialProcessorComponent> ent, ref Content.Shared.Temperature.OnTemperatureChangeEvent args)
    {
        if (ent.Comp.AutoStart)
            TryStartProcessing(ent);
        UpdateAppearance(ent);
        UpdateUserInterface(ent);
    }

    private void OnStoredStackChanged(Entity<StackComponent> ent, ref StackCountChangedEvent args)
    {
        if (!_container.TryGetContainingContainer(ent.Owner, out var container) ||
            !TryComp<IndustrialProcessorComponent>(container.Owner, out var processor) || processor.InTransaction)
            return;
        Entity<IndustrialProcessorComponent> machine = (container.Owner, processor);
        processor.WaitingForOutput = false;
        if (processor.AutoStart)
            TryStartProcessing(machine);
        UpdateAppearance(machine);
        UpdateUserInterface(machine);
    }

    private void OnAnchorChanged(Entity<IndustrialProcessorComponent> ent, ref AnchorStateChangedEvent args)
        => OnPortModeChanged(ent);

    private void OnTerminating(Entity<IndustrialProcessorComponent> ent, ref EntityTerminatingEvent args)
    {
        _pipeNetwork.InvalidateProcessorConnections(ent);
        _liquidPipes.InvalidateProcessorConnections(ent);
        UpdateAdjacentPipeConnections(ent);
    }

    private void OnResearchChanged(Entity<IndustrialProcessorComponent> ent,
        ref Content.Shared.Research.Components.TechnologyDatabaseModifiedEvent args)
    {
        ent.Comp.RecipeCatalog = null;
        if (ent.Comp.AutoStart)
            TryStartProcessing(ent);
        UpdateAppearance(ent);
        UpdateUserInterface(ent);
    }

    private void OnResearchSynced(Entity<IndustrialProcessorComponent> ent,
        ref Content.Shared.Research.Components.TechnologyDatabaseSynchronizedEvent args)
    {
        ent.Comp.RecipeCatalog = null;
        if (ent.Comp.AutoStart)
            TryStartProcessing(ent);
        UpdateAppearance(ent);
        UpdateUserInterface(ent);
    }

    private void OnResearchRegistrationChanged(Entity<IndustrialProcessorComponent> ent,
        ref Content.Shared.Research.Components.ResearchRegistrationChangedEvent args)
    {
        // The research system may clear/synchronise its database later in this event dispatch.
        // Leave rebuilding the menu to the next UI update, after registration has completed.
        ent.Comp.RecipeCatalog = null;
        ent.Comp.PendingStateRefresh = true;
    }

    protected override void TryUpgradeMachine(Entity<IndustrialProcessorComponent> ent, EntityUid user,
        EntityUid moduleUid, IndustrialUpgradeModuleComponent module)
        => EntityManager.System<IndustrialMachineAssemblySystem>().TryUpgradeMachine(ent, user, moduleUid, module);

    public void UpgradeMachine(Entity<IndustrialProcessorComponent> ent, MachineTier tier, EntProtoId prototype)
    {
        ent.Comp.Tier = tier;
        ent.Comp.RecipeCatalog = null;
        ApplyTierSettings(ent);
        EntityManager.System<MetaDataSystem>().SetEntityName(ent, PrototypeManager.Index(prototype).Name);
        ent.Comp.LastState = null;
        UpdateAppearance(ent);
        UpdateUserInterface(ent);
    }

    private void ApplyTierSettings(Entity<IndustrialProcessorComponent> ent)
    {
        var specs = MachineTierHelper.GetSpecs(ent.Comp.Tier);

        ent.Comp.ProcessingSpeedMultiplier = specs.ProcessingSpeedMultiplier;
        ent.Comp.MaxInputSlots = specs.MaxInputSlots;
        ent.Comp.MaxOutputSlots = specs.MaxOutputSlots;
        ent.Comp.MaxAutoTransferPerSecond = specs.MaxAutoTransferPerSecond;
        if (ent.Comp.ProcessorType == IndustrialProcessorType.Extractor)
            ent.Comp.MaxInputSlots = 0;
        if (ent.Comp.ProcessorType == IndustrialProcessorType.FluidTank)
            ent.Comp.MaxInputSlots = ent.Comp.MaxOutputSlots = 0;

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
            Entity<IndustrialProcessorComponent> ent = (uid, comp);
            if (comp.PendingStateRefresh)
            {
                comp.PendingStateRefresh = false;
                if (comp.AutoStart)
                    TryStartProcessing(ent);
                UpdateAppearance(ent);
                UpdateUserInterface(ent);
            }
            if (comp.IsWorking && !comp.WaitingForOutput && CanOperate(ent))
            {
                comp.ProcessingAccumulator = MathF.Min(comp.ProcessingTime,
                    comp.ProcessingAccumulator + frameTime * comp.ProcessingSpeedMultiplier);
                if (comp.ProcessingAccumulator >= comp.ProcessingTime)
                {
                    if (comp.CurrentRecipe is not { } id ||
                        !PrototypeManager.TryIndex(id, out IndustrialRecipePrototype? recipe) ||
                        !_container.TryGetContainer(ent, comp.InputContainerId, out var input) ||
                        !Resources.CanAfford(input.ContainedEntities, recipe.Inputs) || !HasFluidIngredients(ent, recipe))
                        CancelCycle(ent);
                    else if (CanAcceptOutputs(ent, recipe))
                        TryCompleteRecipe(ent, recipe);
                    else
                        comp.WaitingForOutput = true;
                }
                UpdateAppearance(ent);
            }

            comp.UiAccumulator += frameTime;
            if (comp.UiAccumulator >= 1f)
            {
                comp.UiAccumulator %= 1f;
                UpdateAppearance(ent);
                UpdateUserInterface(ent);
            }
        }
    }

    private void CancelCycle(Entity<IndustrialProcessorComponent> ent)
    {
        ent.Comp.IsWorking = false;
        ent.Comp.WaitingForOutput = false;
        ent.Comp.CurrentRecipe = null;
        ent.Comp.ProcessingAccumulator = 0;
        Dirty(ent);
        if (ent.Comp.AutoStart)
            TryStartProcessing(ent);
        UpdateAppearance(ent);
    }

    public void RefreshExternalStatus(EntityUid uid)
    {
        if (!TryComp<IndustrialProcessorComponent>(uid, out var component))
            return;
        UpdateAppearance((uid, component));
        UpdateUserInterface((uid, component));
    }

    protected override void TryStartProcessing(Entity<IndustrialProcessorComponent> ent)
    {
        if (HasComp<IndustrialManualProcessorComponent>(ent))
            return;
        if (ent.Comp.InTransaction || ent.Comp.IsWorking || !CanOperate(ent) ||
            !TryMatchRecipe(ent, out var recipe) || recipe == null || !CanAcceptOutputs(ent, recipe) || !HasProcessEnvironment(ent, recipe))
            return;
        ent.Comp.CurrentRecipe = recipe.ID;
        ent.Comp.ProcessingTime = recipe.Time;
        ent.Comp.ProcessingAccumulator = 0;
        ent.Comp.IsWorking = true;
        Dirty(ent);
        UpdateAppearance(ent);
        UpdateUserInterface(ent);
    }

    private bool TryCompleteRecipe(Entity<IndustrialProcessorComponent> ent, IndustrialRecipePrototype recipe)
    {
        if (!IsRecipeAvailable(ent, recipe) || !HasProcessEnvironment(ent, recipe))
            return false;
        if (!_container.TryGetContainer(ent, ent.Comp.InputContainerId, out var input) ||
            !_container.TryGetContainer(ent, ent.Comp.OutputContainerId, out var output) ||
            !Resources.TryPlan(input.ContainedEntities, recipe.Inputs, out var plan))
            return false;

        // Outputs are committed before inputs are consumed. Failed insertion restores every output
        // stack and removes staged entities; container events cannot start another cycle midway.
        var originalCounts = new Dictionary<EntityUid, int>();
        foreach (var item in output.ContainedEntities)
        {
            if (TryComp<StackComponent>(item, out var stack))
                originalCounts.Add(item, stack.Count);
        }
        var spawned = new List<EntityUid>();
        var fluidSnapshots = SnapshotFluids(ent);
        var committed = false;
        ent.Comp.InTransaction = true;
        try
        {
            foreach (var (prototype, count) in recipe.Outputs)
            {
                foreach (var product in _stack.SpawnMultipleAtPosition(new EntProtoId(prototype), count, Transform(ent).Coordinates))
                {
                    spawned.Add(product);
                    if (TryComp<StackComponent>(product, out var donor))
                    {
                        foreach (var existing in output.ContainedEntities)
                        {
                            if (TryComp<StackComponent>(existing, out var recipient))
                                _stack.TryMergeStacks((product, donor), (existing, recipient), out _);
                            if (donor.Count == 0)
                                break;
                        }
                        if (donor.Count == 0)
                            continue;
                    }
                    // Prefabricated pipes and machines leave production as movable inventory, not anchored map objects.
                    if (Transform(product).Anchored)
                        EntityManager.System<SharedTransformSystem>().Unanchor(product);
                    if (!_container.Insert(product, output))
                        return false;
                }
            }

            // An external insertion listener may have changed an ingredient while outputs were staged.
            if (!Resources.TryPlan(input.ContainedEntities, recipe.Inputs, out plan))
                return false;
            if (!CommitFluids(ent, recipe) || !Resources.TryPlan(input.ContainedEntities, recipe.Inputs, out plan))
                return false;
            foreach (var (item, count) in plan)
            {
                if (TryComp<StackComponent>(item, out var stack) && stack.Count > count)
                    _stack.TryUse((item, stack), count);
                else
                {
                    _container.Remove(item, input);
                    Del(item);
                }
            }
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                RestoreFluids(fluidSnapshots);
                foreach (var item in spawned)
                {
                    if (!Deleted(item))
                        Del(item);
                }
                foreach (var (item, count) in originalCounts)
                {
                    if (TryComp<StackComponent>(item, out var stack))
                        _stack.SetCount((item, stack), count);
                }
            }
            ent.Comp.InTransaction = false;
        }

        if (recipe.Heat > 0 && HasComp<IndustrialHeatPoweredComponent>(ent))
            _temperature.ChangeHeat(ent, -recipe.Heat, true);
        CancelCycle(ent);
        UpdateUserInterface(ent);
        return true;
    }

    protected override void EjectOutput(Entity<IndustrialProcessorComponent> ent, EntityUid item, EntityUid user)
    {
        if (!_container.TryGetContainer(ent, ent.Comp.OutputContainerId, out var output))
            return;

        if (!output.Contains(item))
            return;

        _container.Remove(item, output);
        _hands.PickupOrDrop(user, item);

        if (ent.Comp.AutoStart)
            TryStartProcessing(ent);

        UpdateAppearance(ent);
        UpdateUserInterface(ent);
    }

    protected override void UpdateAppearance(Entity<IndustrialProcessorComponent> ent)
    {
        var state = GetState(ent);
        // Power can change while the process remains paused or research-locked.
        if (HasComp<IndustrialCrusherVisualsComponent>(ent))
            _appearance.SetData(ent, IndustrialProcessorVisuals.Powered, IsPowered(ent));
        if (ent.Comp.BasePowerLoad > 0 && TryComp<ApcPowerReceiverComponent>(ent, out var receiver))
            receiver.Load = TryComp<IndustrialExtractorComponent>(ent, out var extractor)
                ? extractor.State is IndustrialProcessorState.Working or IndustrialProcessorState.Unpowered ? ent.Comp.BasePowerLoad : 50f
                : ent.Comp.CanWorkWithoutPower ? 0f :
                ent.Comp.IsWorking && ent.Comp.Enabled && !ent.Comp.WaitingForOutput && state != IndustrialProcessorState.Unresearched
                    ? ent.Comp.BasePowerLoad * MachineTierHelper.GetSpecs(ent.Comp.Tier).PowerMultiplier : 50f;
        if (ent.Comp.LastState == state)
            return;
        ent.Comp.LastState = state;
        _appearance.SetData(ent, IndustrialProcessorVisuals.State, state);
    }

    protected override void OnPortModeChanged(Entity<IndustrialProcessorComponent> ent)
    {
        _pipeNetwork.InvalidateProcessorConnections(ent);
        _liquidPipes.InvalidateProcessorConnections(ent);
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

}
