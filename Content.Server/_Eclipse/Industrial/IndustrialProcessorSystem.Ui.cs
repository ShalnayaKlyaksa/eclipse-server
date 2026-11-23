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
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;

namespace Content.Server._Eclipse.Industrial;

public sealed partial class IndustrialProcessorSystem
{
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    private void InitializeUi()
    {
        SubscribeLocalEvent<IndustrialProcessorComponent, IndustrialProcessorSlotMessage>(OnSlotMessage);
        SubscribeLocalEvent<IndustrialProcessorComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<IndustrialProcessorComponent, BeforeActivatableUIOpenEvent>(OnBeforeUiOpen);
        SubscribeLocalEvent<IndustrialProcessorComponent, IndustrialProcessorRecipeMessage>(OnRecipeSelected);
        SubscribeLocalEvent<IndustrialProcessorComponent, IndustrialProcessorToggleMessage>(OnToggle);
        SubscribeLocalEvent<IndustrialProcessorComponent, IndustrialProcessorPortMessage>(OnSetPort);
        SubscribeLocalEvent<IndustrialProcessorComponent, IndustrialProcessorDrainMessage>(OnDrain);
        SubscribeLocalEvent<IndustrialProcessorComponent, IndustrialFluidDrawDoAfterEvent>(OnFluidDrawFinished);
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

        var maxSlots = args.IsInput ? ent.Comp.MaxInputSlots : ent.Comp.MaxOutputSlots;
        if (args.SlotIndex < 0 || args.SlotIndex >= maxSlots)
            return;
        var items = container.ContainedEntities;

        if (args.SlotIndex < items.Count)
        {
            var item = items[args.SlotIndex];
            if (!args.IsInput)
            {
                EjectOutput(ent, item, actor);
                return;
            }

            _container.Remove(item, container);
            _hands.PickupOrDrop(actor, item);
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

        if (!TryInsertHeld(ent, actor, held.Value))
            return;

        _popup.PopupCursor(Loc.GetString("industrial-manual-insert-success"), actor);

        if (ent.Comp.AutoStart)
            TryStartProcessing(ent);

        UpdateUserInterface(ent);
    }

    private void OnRecipeSelected(Entity<IndustrialProcessorComponent> ent, ref IndustrialProcessorRecipeMessage args)
    {
        if (args.RecipeId != null && (!PrototypeManager.TryIndex<IndustrialRecipePrototype>(args.RecipeId, out var recipe) ||
            (ent.Comp.ProcessorType != IndustrialProcessorType.Buffer && recipe.Processor != ent.Comp.ProcessorType) || !IsRecipeAvailable(ent, recipe)))
            return;
        ent.Comp.SelectedRecipe = args.RecipeId == null ? (ProtoId<IndustrialRecipePrototype>?) null : new ProtoId<IndustrialRecipePrototype>(args.RecipeId);
        CancelCycle(ent);
        Dirty(ent);
        UpdateUserInterface(ent);
    }

    private void OnToggle(Entity<IndustrialProcessorComponent> ent, ref IndustrialProcessorToggleMessage args)
    {
        ent.Comp.Enabled = !ent.Comp.Enabled;
        Dirty(ent);
        if (ent.Comp.AutoStart)
            TryStartProcessing(ent);
        UpdateAppearance(ent);
        UpdateUserInterface(ent);
    }

    private void OnSetPort(Entity<IndustrialProcessorComponent> ent, ref IndustrialProcessorPortMessage args)
    {
        if (!CardinalDirections.Contains(args.Face) || !Enum.IsDefined(args.Mode) ||
            ent.Comp.ProcessorType == IndustrialProcessorType.FluidTank && args.Mode is FacePortState.ItemInput or FacePortState.ItemOutput ||
            args.Mode is FacePortState.LiquidInput or FacePortState.LiquidOutput && !HasComp<IndustrialFluidProcessorComponent>(ent) ||
            args.Mode == FacePortState.HeatInput && !HasComp<IndustrialHeatPoweredComponent>(ent) ||
            !_hands.EnumerateHeld(args.Actor).Any(IsPortConfigurator))
            return;
        ent.Comp.SetFacePort(args.Face, args.Mode);
        Dirty(ent);
        OnPortModeChanged(ent);
        UpdateUserInterface(ent);
    }

    private void OnDrain(Entity<IndustrialProcessorComponent> ent, ref IndustrialProcessorDrainMessage args)
    {
        _ui.CloseUi(ent.Owner, IndustrialProcessorUiKey.Fluid, args.Actor);
        if (TryGetEntity(args.Container, out var container))
            DrawFluid(ent, args.Actor, container.Value, args.Input, args.Reagent);
    }

    protected override void UpdateUserInterface(Entity<IndustrialProcessorComponent> ent)
    {
        if (!_ui.IsUiOpen(ent.Owner, IndustrialProcessorUiKey.Key))
            return;

        var state = GetState(ent);
        var stateKey = state switch
        {
            IndustrialProcessorState.Working => "industrial-processor-working",
            IndustrialProcessorState.Blocked => "industrial-processor-blocked",
            IndustrialProcessorState.Unpowered => "industrial-processor-unpowered",
            IndustrialProcessorState.Unheated => "industrial-processor-unheated",
            IndustrialProcessorState.Paused => "industrial-processor-paused",
            IndustrialProcessorState.Unresearched => "industrial-processor-unresearched",
            _ => "industrial-processor-idle",
        };

        var progress = ent.Comp.IsWorking && ent.Comp.ProcessingTime > 0
            ? MathF.Min(1f, ent.Comp.ProcessingAccumulator / ent.Comp.ProcessingTime)
            : 0f;
        var secondsRemaining = MathF.Max(0, ent.Comp.ProcessingTime - ent.Comp.ProcessingAccumulator) /
                               MathF.Max(0.01f, ent.Comp.ProcessingSpeedMultiplier);
        if (IsEnvironmentBlocked(ent))
            stateKey = "industrial-cleanroom-required";

        string? recipeName = null;
        if (ent.Comp.CurrentRecipe != null &&
            PrototypeManager.TryIndex(ent.Comp.CurrentRecipe, out IndustrialRecipePrototype? recipe))
        {
            recipeName = GetRecipeDisplayName(recipe);
        }

        var usesHeat = HasComp<IndustrialHeatPoweredComponent>(ent);
        var hasHeat = HasSufficientHeat(ent);
        var isExtractor = TryComp<IndustrialExtractorComponent>(ent, out var extractor);
        var deposit = isExtractor ? EntityManager.System<IndustrialExtractionSystem>().GetDepositStatus(ent) : (Resource: (string?) null, Remaining: 0);
        if (extractor != null)
        {
            stateKey = state switch
            {
                IndustrialProcessorState.Paused => "industrial-extraction-stopped",
                IndustrialProcessorState.Unresearched => "industrial-extraction-needs-data",
                IndustrialProcessorState.Idle when !Transform(ent).Anchored => "industrial-extraction-unanchored",
                IndustrialProcessorState.Idle when deposit.Resource != null => "industrial-extraction-depleted",
                IndustrialProcessorState.Idle => "industrial-extraction-waiting-vein",
                _ => stateKey,
            };
            if (state == IndustrialProcessorState.Working)
            {
                recipeName = Loc.GetString("industrial-extraction-running");
                var duration = MathF.Max(0.01f, extractor.Interval);
                progress = Math.Clamp(extractor.Elapsed / duration, 0f, 1f);
                secondsRemaining = duration * (1f - progress);
            }
        }

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
            ent.Comp.WestFacePort)
        {
            IsBuffer = ent.Comp.ProcessorType is IndustrialProcessorType.Buffer or IndustrialProcessorType.FluidTank,
            IsManual = TryComp<IndustrialManualProcessorComponent>(ent, out var manual),
            ManualBusy = manual?.Busy ?? false,
            IsExtractor = isExtractor,
            ExtractionResource = deposit.Resource,
            ExtractionReserve = deposit.Remaining,
            Enabled = ent.Comp.Enabled,
            SelectedRecipeId = ent.Comp.SelectedRecipe?.Id,
            CurrentRecipeId = ent.Comp.CurrentRecipe?.Id,
            SecondsRemaining = secondsRemaining,
            SpeedMultiplier = ent.Comp.ProcessingSpeedMultiplier,
            Temperature = TryComp<Content.Shared.Temperature.Components.TemperatureComponent>(ent, out var temp)
                ? temp.CurrentTemperature : 0,
            RequiredTemperature = TryComp<IndustrialHeatPoweredComponent>(ent, out var heat) ? heat.MinTemperature : 0,
            PowerWatts = TryComp<ApcPowerReceiverComponent>(ent, out var receiver) ? receiver.Load : 0,
            SuppliedPowerWatts = receiver?.PowerReceived,
            Recipes = GetRecipeCatalog(ent),
            UsesFluids = HasComp<IndustrialFluidProcessorComponent>(ent),
            FluidInput = TankState(ent, true, out var inputCapacity),
            FluidInputCapacity = inputCapacity,
            FluidOutput = TankState(ent, false, out var outputCapacity),
            FluidOutputCapacity = outputCapacity,
        });
    }

    private IndustrialRecipeUiState[] GetRecipeCatalog(Entity<IndustrialProcessorComponent> ent)
    {
        if (ent.Comp.RecipeCatalog != null)
            return ent.Comp.RecipeCatalog;
        IEnumerable<IndustrialRecipePrototype> recipes;
        if (ent.Comp.ProcessorType == IndustrialProcessorType.Buffer)
            recipes = PrototypeManager.EnumeratePrototypes<IndustrialRecipePrototype>()
                .Where(r => r.Outputs.Count > 0)
                .GroupBy(r => string.Join(",", r.Outputs.Keys.OrderBy(k => k)))
                .Select(group => group.First()).OrderBy(GetRecipeProductName);
        else if (RecipesByProcessor.TryGetValue(ent.Comp.ProcessorType, out var typedRecipes))
            recipes = typedRecipes;
        else
            return [];
        return ent.Comp.RecipeCatalog = recipes.Select(recipe => new IndustrialRecipeUiState(
            recipe.ID, ent.Comp.ProcessorType == IndustrialProcessorType.Buffer ? GetRecipeProductName(recipe) : GetRecipeDisplayName(recipe),
            recipe.Time / ent.Comp.ProcessingSpeedMultiplier,
            IsRecipeAvailable(ent, recipe), RecipeSlots(recipe.Inputs), RecipeSlots(recipe.Outputs))
            {
                HeatKilojoules = recipe.Heat / 1000f,
                FluidInputs = FluidSlots(recipe.FluidInputs), FluidOutputs = FluidSlots(recipe.FluidOutputs),
                Bath = FluidSlots(recipe.Bath),
            }).ToArray();
    }

    private IndustrialFluidSlotState[] FluidSlots(Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> entries)
        => entries.Select(pair => FluidSlot(pair.Key, pair.Value.Float())).ToArray();

    private IndustrialFluidSlotState FluidSlot(ProtoId<ReagentPrototype> id, float amount)
    {
        var reagent = PrototypeManager.Index(id);
        return new IndustrialFluidSlotState(id.Id, reagent.LocalizedName, amount, reagent.SubstanceColor.WithAlpha(1f));
    }

    private IndustrialFluidSlotState[] TankState(EntityUid uid, bool input, out float capacity)
    {
        capacity = 0;
        if (!TryComp<IndustrialFluidProcessorComponent>(uid, out var fluid) ||
            !Solutions.TryGetSolution(uid, input ? fluid.InputSolution : fluid.OutputSolution, out _, out var solution))
            return [];
        capacity = solution.MaxVolume.Float();
        return solution.Contents.GroupBy(r => r.Reagent.Prototype)
            .Select(group => FluidSlot(group.Key, group.Sum(r => r.Quantity.Float()))).ToArray();
    }

    private IndustrialProcessorSlotState[] RecipeSlots(Dictionary<string, int> entries)
        => entries.Select(pair => new IndustrialProcessorSlotState(pair.Key, pair.Value,
            PrototypeManager.Index<EntityPrototype>(pair.Key).Name)).ToArray();

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
        if (recipe.Name is { } name) return Loc.GetString(name);
        return GetRecipeProductName(recipe);
    }

    private string GetRecipeProductName(IndustrialRecipePrototype recipe)
    {
        foreach (var protoId in recipe.Outputs.Keys)
        {
            if (PrototypeManager.TryIndex<EntityPrototype>(protoId, out var proto))
                return proto.Name;
        }

        foreach (var id in recipe.FluidOutputs.Keys)
            return PrototypeManager.Index(id).LocalizedName;
        return recipe.ID;
    }

    private IndustrialProcessorSlotState[] BuildSlotStates(
        Entity<IndustrialProcessorComponent> ent,
        string containerId,
        int maxSlots)
    {
        var slots = new IndustrialProcessorSlotState[maxSlots];
        Array.Fill(slots, new IndustrialProcessorSlotState(null, 0, string.Empty));

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

        return slots;
    }
}
