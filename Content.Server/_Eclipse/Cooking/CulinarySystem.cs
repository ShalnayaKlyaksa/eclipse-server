using System.Linq;
using Content.Server.Power.Components;
using Content.Shared._Eclipse.Cooking;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition;
using Content.Shared.Construction;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.UserInterface;
using Content.Server.Nutrition.Components;
using Content.Shared.DoAfter;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.Cooking;

/// <summary>Free-form cooking. Existing solution transfers preserve nutrition, chemicals and property quantities.</summary>
public sealed partial class CulinarySystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IngestionSystem _ingestion = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;

    private readonly Dictionary<string, CulinaryIngredientPrototype> _foods = new();
    private readonly Dictionary<string, CulinaryIngredientPrototype> _reagents = new();
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<CulinaryStationComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<CulinaryStationComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<CulinaryStationComponent, InteractUsingEvent>(OnInteract);
        SubscribeLocalEvent<CulinaryStationComponent, CulinaryMethodMessage>(OnMethod);
        SubscribeLocalEvent<CulinaryStationComponent, CulinaryActionMessage>(OnAction);
        SubscribeLocalEvent<CulinaryStationComponent, CulinarySlotMessage>(OnSlot);
        SubscribeLocalEvent<CulinaryStationComponent, EntInsertedIntoContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<CulinaryStationComponent, EntRemovedFromContainerMessage>(OnContainerChanged);
        SubscribeLocalEvent<CulinaryStationComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
        SubscribeLocalEvent<CulinaryStationComponent, BreakageEventArgs>(OnBreak);
        SubscribeLocalEvent<CulinaryStationComponent, DestructionEventArgs>(OnDestroy);
        SubscribeLocalEvent<CulinaryStationComponent, MachineDeconstructedEvent>(OnDeconstructed);
        SubscribeLocalEvent<CulinaryStationComponent, IngestibleEvent>(OnIngestible);
        SubscribeLocalEvent<EdibleComponent, ExaminedEvent>(OnFoodExamined);
        SubscribeLocalEvent<EdibleComponent, InteractUsingEvent>(OnPrepareFood);
        SubscribeLocalEvent<EdibleComponent, CulinaryPrepareDoAfterEvent>(OnPreparedFood);
        SubscribeLocalEvent<EdibleComponent, BeforeFoodSlicedEvent>(OnSlicedFood);
        SubscribeLocalEvent<CulinaryStationComponent, CulinaryStirDoAfterEvent>(OnStirred);
    }

    private void EnsureProfiles()
    {
        if (_foods.Count != 0) return;
        foreach (var profile in _prototypes.EnumeratePrototypes<CulinaryIngredientPrototype>())
        {
            foreach (var food in profile.Foods) _foods.Add(food.Id, profile);
            foreach (var reagent in profile.Reagents) _reagents.Add(reagent.Id, profile);
        }
    }

    private void OnInit(Entity<CulinaryStationComponent> ent, ref ComponentInit args)
    {
        ent.Comp.Ingredients = _containers.EnsureContainer<Container>(ent, "culinary-ingredients");
        if (!ent.Comp.AllowedMethods.Contains(ent.Comp.Method) && ent.Comp.AllowedMethods.Length > 0)
            ent.Comp.Method = ent.Comp.AllowedMethods[0];
    }

    private bool FoodSolution(EntityUid uid, out Entity<SolutionComponent> solution)
    {
        solution = default;
        if (!TryComp<EdibleComponent>(uid, out var food)
            || !_solutions.TryGetSolution(uid, food.Solution, out var found)) return false;
        solution = found.Value;
        return true;
    }

    private bool Batch(EntityUid uid, out Entity<SolutionComponent> solution)
    {
        solution = default;
        if (!_solutions.TryGetSolution(uid, "food", out var found)) return false;
        solution = found.Value;
        return true;
    }

    private bool Powered(EntityUid uid) => TryComp<ApcPowerReceiverComponent>(uid, out var power) && power.Powered;

    /// <summary>Attach properties to existing food reagents without changing volume, nutrition or chemistry.</summary>
    public void Stamp(EntityUid uid, Entity<SolutionComponent> solution)
    {
        EnsureProfiles();
        var original = solution.Comp.Solution;
        if (original.Volume <= 0 || ReadData(original) != null) return;
        _foods.TryGetValue(MetaData(uid).EntityPrototype?.ID ?? string.Empty, out var profile);
        var donorIds = new HashSet<string>
        {
            "Nutriment", "Vitamin", "Protein", "UncookedAnimalProteins", "UncookedVegetableProteins",
            "Sugar", "Milk", "Cream", "MilkSoy", "MilkOat", "OilOlive", "Fat", "Flour", "Cornmeal",
            "Rice", "Oats", "TableSalt", "Blackpepper", "Honey", "EclipseHoney", "Egg",
        };
        var changed = false;
        foreach (var reagent in original.Contents.ToArray())
        {
            if (!donorIds.Contains(reagent.Reagent.Prototype)) continue;
            var ingredient = profile;
            if (ingredient == null && !_reagents.TryGetValue(reagent.Reagent.Prototype, out ingredient)) continue;
            var data = new CulinaryData { Traits = ingredient.Traits.ToDictionary(p => p.Key, p => p.Value * CulinaryMath.PropertyScale) };
            var metadata = reagent.Reagent.Data?.Select(value => value.Clone()).ToList() ?? new();
            metadata.Add(data);
            original.RemoveReagent(reagent.Reagent, reagent.Quantity);
            original.AddReagent(new ReagentId(reagent.Reagent.Prototype, metadata), reagent.Quantity);
            changed = true;
        }
        if (changed) _solutions.UpdateChemicals(solution);
    }

    private void OnInteract(Entity<CulinaryStationComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled) return;
        args.Handled = true;
        if (ent.Comp.Working)
        {
            Notify(ent, args.User, "culinary-finish-first");
            return;
        }
        if (!Batch(ent, out var batch)) return;
        if (batch.Comp.Solution.Volume <= 0 && ent.Comp.Ingredients.ContainedEntities.Count == 0)
        {
            ent.Comp.Composition.Clear();
            ent.Comp.Flavors.Clear();
        }
        var source = args.Used;
        if (TryComp<CulinaryVesselComponent>(source, out var vessel))
        {
            TransferVessel(ent, source, vessel, args.User);
            Publish(ent);
            return;
        }
        if (ent.Comp.Ready && MetaData(source).EntityPrototype?.ID == "FoodBowlBig")
        {
            Serve(ent, args.User);
            return;
        }
        if (TryComp<UtensilComponent>(source, out var utensil) && (utensil.Types & UtensilType.Spoon) != 0)
        {
            if (ent.Comp.Ready) _ingestion.TryIngest(args.User, ent);
            else if (ent.Comp.AllowedMethods.All(m => !CulinaryMath.IsHot(m)) && TotalVolume(ent) > 0)
                _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, 3,
                    new CulinaryStirDoAfterEvent(), ent, ent, source)
                { NeedHand = true, BreakOnMove = true, BreakOnDamage = true, BreakOnHandChange = true });
            return;
        }
        // Containers are poured ten units at a time; solids stay intact until processing starts.
        if (TryComp<DrainableSolutionComponent>(source, out var drain)
            && _solutions.TryGetSolution(source, drain.Solution, out var liquid))
        {
            Stamp(source, liquid.Value);
            var amount = FixedPoint2.Min(FixedPoint2.New(10), FixedPoint2.Min(liquid.Value.Comp.Solution.Volume,
                FixedPoint2.New(ent.Comp.Capacity) - TotalVolume(ent)));
            if (amount <= 0) { Notify(ent, args.User, "culinary-full"); return; }
            var split = _solutions.SplitSolution(liquid.Value, amount);
            ent.Comp.Composition.Add(MetaData(source).EntityName);
            ent.Comp.Ready = false;
            ent.Comp.Progress = 0;
            ent.Comp.Status = "culinary-status-idle";
            _solutions.AddSolution(batch, split);
            Publish(ent);
            return;
        }
        if (!FoodSolution(source, out var foodSolution) || foodSolution.Comp.Solution.Volume <= 0
            || HasComp<StorageComponent>(source) || HasComp<StackComponent>(source)
            || HasComp<ItemSlotsComponent>(source))
        {
            Notify(ent, args.User, "culinary-unsupported");
            return;
        }
        // Never delete items hidden inside edible toys or other nested containers.
        if (TryComp<ContainerManagerComponent>(source, out var manager)
            && manager.Containers.Values.Any(container => container.ID != "solution@food" && container.ContainedEntities.Count > 0))
        {
            Notify(ent, args.User, "culinary-unsupported");
            return;
        }
        if (ent.Comp.Ingredients.ContainedEntities.Count >= ent.Comp.MaxIngredients
            || TotalVolume(ent) + foodSolution.Comp.Solution.Volume > FixedPoint2.New(ent.Comp.Capacity))
        {
            Notify(ent, args.User, "culinary-full");
            return;
        }
        Stamp(source, foodSolution);
        if (_containers.Insert(source, ent.Comp.Ingredients))
        {
            ent.Comp.Ready = false;
            ent.Comp.Progress = 0;
            ent.Comp.Status = "culinary-status-idle";
        }
        Publish(ent);
    }

    private FixedPoint2 TotalVolume(Entity<CulinaryStationComponent> ent)
    {
        var volume = Batch(ent, out var batch) ? batch.Comp.Solution.Volume : FixedPoint2.Zero;
        foreach (var uid in ent.Comp.Ingredients.ContainedEntities)
            if (FoodSolution(uid, out var food)) volume += food.Comp.Solution.Volume;
        return volume;
    }

    private void OnMethod(Entity<CulinaryStationComponent> ent, ref CulinaryMethodMessage args)
    {
        if (ent.Comp.Working || !ent.Comp.AllowedMethods.Contains(args.Method)) return;
        ent.Comp.Method = args.Method;
        Publish(ent);
    }

    private void OnAction(Entity<CulinaryStationComponent> ent, ref CulinaryActionMessage args)
    {
        switch (args.Action)
        {
            case CulinaryAction.InsertHeld:
                if (_hands.GetActiveItem(args.Actor) is {} item)
                {
                    var use = new InteractUsingEvent(args.Actor, item, ent, Transform(ent).Coordinates);
                    OnInteract(ent, ref use);
                }
                break;
            case CulinaryAction.Start: Start(ent, args.Actor); break;
            case CulinaryAction.Stop:
                ent.Comp.Working = false;
                ent.Comp.Status = "culinary-status-stopped";
                Publish(ent);
                break;
            case CulinaryAction.Eject:
                if (ent.Comp.Working) return;
                foreach (var uid in ent.Comp.Ingredients.ContainedEntities.ToArray()) _containers.Remove(uid, ent.Comp.Ingredients);
                Publish(ent);
                break;
            case CulinaryAction.Serve:
                if (ent.Comp.Ready) Serve(ent, args.Actor);
                break;
            case CulinaryAction.Taste:
                if (!ent.Comp.Working && ent.Comp.Ready)
                {
                    _popup.PopupEntity(Describe(ent), ent, args.Actor);
                    _ingestion.TryIngest(args.Actor, ent);
                }
                break;
        }
    }

    private void OnSlot(Entity<CulinaryStationComponent> ent, ref CulinarySlotMessage args)
    {
        if (ent.Comp.Working || args.Index < 0 || args.Index >= ent.Comp.Ingredients.ContainedEntities.Count) return;
        var item = ent.Comp.Ingredients.ContainedEntities[args.Index];
        _containers.Remove(item, ent.Comp.Ingredients);
        _hands.TryPickupAnyHand(args.Actor, item);
        Publish(ent);
    }

    public bool Start(Entity<CulinaryStationComponent> ent, EntityUid? user = null)
    {
        if (ent.Comp.Working || !Batch(ent, out var batch)) return false;
        if (!ent.Comp.AllowedMethods.Contains(ent.Comp.Method)) return Reject("culinary-wrong-appliance");
        var preview = Preview(ent);
        var volume = preview.Volume;
        if (volume <= 0) return Reject("culinary-empty");
        if (CulinaryMath.IsHot(ent.Comp.Method) && !Powered(ent)) return Reject("culinary-no-power");
        var data = ReadData(preview);
        if (data == null) return Reject("culinary-no-properties");
        if (preview.Contents.SelectMany(r => r.Reagent.Data?.OfType<CulinaryData>() ?? Enumerable.Empty<CulinaryData>())
            .All(d => (d.Methods & (1u << (int) ent.Comp.Method)) != 0)) return Reject("culinary-already-processed");
        if (ent.Comp.Method is CulinaryMethod.Boil or CulinaryMethod.Simmer or CulinaryMethod.Steam or CulinaryMethod.Soak
            && preview.GetTotalPrototypeQuantity("Water") < FixedPoint2.New(volume.Float() * 0.15f))
            return Reject("culinary-needs-water");
        if (ent.Comp.Method == CulinaryMethod.Marinate && data.Get(CulinaryProperty.Acid) < 0.02f)
            return Reject("culinary-needs-acid");
        if (ent.Comp.Method == CulinaryMethod.Fry && data.Get(CulinaryProperty.Fat) < 0.02f)
            return Reject("culinary-needs-fat");

        CommitIngredients(ent, batch);
        ent.Comp.Progress = 0;
        ent.Comp.Working = true;
        ent.Comp.Ready = false;
        ent.Comp.Status = "culinary-status-working";
        Publish(ent);
        return true;

        bool Reject(string key)
        {
            if (user != null) Notify(ent, user.Value, key);
            return false;
        }
    }

    private void CommitIngredients(Entity<CulinaryStationComponent> ent, Entity<SolutionComponent> batch)
    {
        foreach (var uid in ent.Comp.Ingredients.ContainedEntities.ToArray())
        {
            if (!FoodSolution(uid, out var food)) continue;
            ent.Comp.Composition.Add(MetaData(uid).EntityName);
            if (TryComp<FlavorProfileComponent>(uid, out var flavor)) ent.Comp.Flavors.UnionWith(flavor.Flavors);
            var contents = _solutions.SplitSolution(food, food.Comp.Solution.Volume);
            _solutions.AddSolution(batch, contents);
            _containers.Remove(uid, ent.Comp.Ingredients);
            QueueDel(uid);
        }
    }

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < 0.5f) return;
        var seconds = _accumulator;
        _accumulator = 0;
        var query = EntityQueryEnumerator<CulinaryStationComponent>();
        while (query.MoveNext(out var uid, out var station))
        {
            if (!station.Working || Paused(uid)) continue;
            if (CulinaryMath.IsHot(station.Method) && !Powered(uid))
            {
                station.Status = "culinary-status-no-power";
                Publish((uid, station));
                continue;
            }
            station.Status = "culinary-status-working";
            station.Progress += seconds / CulinaryMath.Seconds(station.Method);
            if (station.Progress >= 1) Complete((uid, station));
            Publish((uid, station));
        }
    }

    public void Complete(Entity<CulinaryStationComponent> ent)
    {
        if (!ent.Comp.Working || !Batch(ent, out var batch)) return;
        var solution = batch.Comp.Solution;
        var data = ReadData(solution);
        if (data != null)
        {
            foreach (var reagent in solution.Contents.ToArray())
            {
                var ingredient = reagent.Reagent.Data?.OfType<CulinaryData>().FirstOrDefault();
                if (ingredient != null) ReplaceReagentData(solution, reagent,
                    CulinaryMath.Process(ingredient, ent.Comp.Method, data));
            }
        }
        if (CulinaryMath.IsHot(ent.Comp.Method))
        {
            // Preserve every other chemical, including poison. Heat only cooks the normal raw proteins.
            foreach (var reagent in solution.Contents.ToArray())
            {
                if (reagent.Reagent.Prototype is not ("UncookedAnimalProteins" or "UncookedVegetableProteins")) continue;
                solution.RemoveReagent(reagent.Reagent, reagent.Quantity);
                solution.AddReagent(new ReagentId("Protein", reagent.Reagent.Data), reagent.Quantity);
            }
        }
        if (ent.Comp.Method == CulinaryMethod.Reduce)
        {
            var water = solution.GetTotalPrototypeQuantity("Water");
            solution.RemoveReagent("Water", water / 2);
        }
        _solutions.UpdateChemicals(batch);
        ent.Comp.Working = false;
        ent.Comp.Ready = true;
        ent.Comp.Progress = 1;
        ent.Comp.Status = "culinary-status-ready";
    }

    public EntityUid? Serve(Entity<CulinaryStationComponent> ent, EntityUid? user = null)
    {
        if (!Batch(ent, out var batch) || batch.Comp.Solution.Volume <= 0) return null;
        if (user != null)
        {
            var bowl = _hands.GetActiveItem(user.Value);
            if (bowl == null || MetaData(bowl.Value).EntityPrototype?.ID != "FoodBowlBig"
                || !FoodSolution(bowl.Value, out var empty) || empty.Comp.Solution.Volume > 0)
            {
                Notify(ent, user.Value, "culinary-needs-bowl");
                return null;
            }
            if (!_hands.TryDrop(user.Value, bowl.Value)) return null;
            QueueDel(bowl.Value);
        }
        var split = _solutions.SplitSolution(batch, FixedPoint2.Min(FixedPoint2.New(20), batch.Comp.Solution.Volume));
        var data = ReadData(split);
        if (data != null)
        {
            data = CulinaryMath.Resolve(data);
            data.Cooked = true;
            ReplaceData(split, data);
        }
        var prototype = ent.Comp.Method switch
        {
            CulinaryMethod.Boil or CulinaryMethod.Reduce => "EclipseCulinarySoup",
            CulinaryMethod.Simmer => "EclipseCulinaryStew",
            CulinaryMethod.Fry => "EclipseCulinaryStirFry",
            CulinaryMethod.Bake => "EclipseCulinaryBake",
            CulinaryMethod.Steam => "EclipseCulinarySteamed",
            _ => "EclipseCulinaryPrepared",
        };
        var output = SpawnNextToOrDrop(prototype, ent);
        if (FoodSolution(output, out var food))
        {
            // Bowl prototypes inherit a default meal. Replace it instead of silently losing the real batch.
            _solutions.RemoveAllSolution(food);
            _solutions.AddSolution(food, split);
        }
        if (TryComp<FlavorProfileComponent>(output, out var flavor)) flavor.Flavors.UnionWith(ent.Comp.Flavors);
        if (user != null) _hands.TryPickupAnyHand(user.Value, output);
        if (batch.Comp.Solution.Volume <= 0)
        {
            ent.Comp.Ready = false;
            ent.Comp.Progress = 0;
            ent.Comp.Status = "culinary-status-idle";
            ent.Comp.Composition.Clear();
            ent.Comp.Flavors.Clear();
        }
        Publish(ent);
        return output;
    }

    public static CulinaryData? ReadData(Solution solution)
    {
        var result = new CulinaryData();
        var volume = 0f;
        foreach (var reagent in solution.Contents)
        {
            var data = reagent.Reagent.Data?.OfType<CulinaryData>().FirstOrDefault();
            if (data == null) continue;
            var quantity = reagent.Quantity.Float();
            volume += quantity;
            result.Methods |= data.Methods;
            result.Cooked |= data.Cooked;
            foreach (var (property, value) in data.Traits)
                result.Traits[property] = result.Get(property) + value * quantity;
        }
        if (volume <= 0) return null;
        foreach (var property in result.Traits.Keys.ToArray()) result.Traits[property] /= volume;
        return result;
    }

    private static void ReplaceData(Solution solution, CulinaryData data)
    {
        foreach (var reagent in solution.Contents.ToArray())
        {
            if (reagent.Reagent.Data?.OfType<CulinaryData>().Any() != true) continue;
            ReplaceReagentData(solution, reagent, data);
        }
    }

    private static void ReplaceReagentData(Solution solution, ReagentQuantity reagent, CulinaryData data)
    {
        var metadata = reagent.Reagent.Data?.Where(value => value is not CulinaryData).Select(value => value.Clone()).ToList() ?? new();
        metadata.Add((CulinaryData) data.Clone());
        solution.RemoveReagent(reagent.Reagent, reagent.Quantity);
        solution.AddReagent(new ReagentId(reagent.Reagent.Prototype, metadata), reagent.Quantity);
    }

    private Solution Preview(Entity<CulinaryStationComponent> ent)
    {
        var original = Batch(ent, out var batch) ? batch.Comp.Solution : new Solution();
        var result = original.Clone();
        foreach (var uid in ent.Comp.Ingredients.ContainedEntities)
            if (FoodSolution(uid, out var food)) result.AddSolution(food.Comp.Solution, _prototypes);
        return result;
    }

    private string Describe(Entity<CulinaryStationComponent> ent) => Describe(Preview(ent));

    public string Describe(Solution solution)
    {
        var data = ReadData(solution);
        if (data == null) return Loc.GetString("culinary-no-properties");
        var concentration = solution.Volume > 0
            ? solution.Contents.Where(r => r.Reagent.Data?.OfType<CulinaryData>().Any() == true).Sum(r => r.Quantity.Float()) / solution.Volume.Float() : 0;
        var effects = string.Join(", ", CulinaryMath.Resolve(data).Traits.Where(p => p.Value > 0.005f)
            .OrderByDescending(p => p.Value).Select(p => Loc.GetString("culinary-property-value",
                ("property", Loc.GetString($"culinary-property-{p.Key.ToString().ToLowerInvariant()}")),
                ("value", MathF.Round(p.Value * concentration * 20, 1)))));
        return Loc.GetString("culinary-profile", ("effects", effects),
            ("onset", MathF.Round(CulinaryMath.Onset(data))),
            ("duration", MathF.Round(CulinaryMath.DurationScale(data) * 100)));
    }

    private void OnFoodExamined(Entity<EdibleComponent> ent, ref ExaminedEvent args)
    {
        if (!FoodSolution(ent, out var food)) return;
        var solution = food.Comp.Solution;
        if (ReadData(solution) != null)
        {
            args.PushText(Describe(solution));
            return;
        }
        EnsureProfiles();
        if (_foods.TryGetValue(MetaData(ent).EntityPrototype?.ID ?? string.Empty, out var profile))
            args.PushText(Loc.GetString("culinary-raw-profile", ("effects", string.Join(", ", profile.Traits
                .OrderByDescending(p => p.Value).Select(p => Loc.GetString($"culinary-property-{p.Key.ToString().ToLowerInvariant()}"))))));
    }

    private void Publish(Entity<CulinaryStationComponent> ent)
    {
        if (ent.Comp.Ingredients == null) return;
        var preview = Preview(ent);
        var data = ReadData(preview);
        var concentration = preview.Volume > 0 ? preview.Contents
            .Where(r => r.Reagent.Data?.OfType<CulinaryData>().Any() == true).Sum(r => r.Quantity.Float()) / preview.Volume.Float() : 0;
        _ui.SetUiState(ent.Owner, CulinaryUiKey.Key, new CulinaryState(
            ent.Comp.Composition.Concat(ent.Comp.Ingredients.ContainedEntities.Select(uid => MetaData(uid).EntityName)).Distinct().ToArray(),
            Describe(ent), TotalVolume(ent).Float(), ent.Comp.Method, ent.Comp.Progress, ent.Comp.Working,
            ent.Comp.Ready, Powered(ent), ent.Comp.Status)
        {
            InputSlots = ent.Comp.Ingredients.ContainedEntities.Select((uid, index) => new Content.Shared._Eclipse.WorkshopItemState(
                index, MetaData(uid).EntityPrototype?.ID, MetaData(uid).EntityName)).ToArray(),
            InputCount = ent.Comp.Ingredients.ContainedEntities.Count,
            Capacity = ent.Comp.Capacity,
            PropertyValues = data == null ? [] : CulinaryMath.Resolve(data).Traits.Where(p => p.Value > 0.005f)
                .OrderBy(p => p.Key).Select(p => new CulinaryPropertyState(p.Key, p.Value * concentration * 20)).ToArray(),
            History = data == null ? [] : Enum.GetValues<CulinaryMethod>().Where(m => (data.Methods & (1u << (int) m)) != 0).Select(m => m.ToString()).ToArray(),
            SecondsRemaining = Math.Max(0, 1 - ent.Comp.Progress) * CulinaryMath.Seconds(ent.Comp.Method),
            OnsetSeconds = data == null ? 0 : CulinaryMath.Onset(data),
            DurationPercent = data == null ? 0 : CulinaryMath.DurationScale(data) * 100,
            StationName = MetaData(ent).EntityName,
            AllowedMethods = ent.Comp.AllowedMethods,
            MethodDone = data != null && preview.Contents.SelectMany(r => r.Reagent.Data?.OfType<CulinaryData>() ?? Enumerable.Empty<CulinaryData>())
                .All(d => (d.Methods & (1u << (int) ent.Comp.Method)) != 0),
        });
    }

    private void OnContainerChanged(Entity<CulinaryStationComponent> ent, ref EntInsertedIntoContainerMessage args) => Publish(ent);
    private void OnContainerChanged(Entity<CulinaryStationComponent> ent, ref EntRemovedFromContainerMessage args) => Publish(ent);
    private void OnSolutionChanged(Entity<CulinaryStationComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (ent.Comp.Ready && !ent.Comp.Working && Batch(ent, out var batch) && batch.Comp.Solution.Volume <= 0)
        {
            ent.Comp.Ready = false;
            ent.Comp.Progress = 0;
            ent.Comp.Status = "culinary-status-idle";
        }
        Publish(ent);
    }
    private void Notify(EntityUid uid, EntityUid user, string key) => _popup.PopupEntity(Loc.GetString(key), uid, user);
    private void OnUiOpened(Entity<CulinaryStationComponent> ent, ref BoundUIOpenedEvent args) => Publish(ent);
    private void OnIngestible(Entity<CulinaryStationComponent> ent, ref IngestibleEvent args)
        => args.Cancelled |= ent.Comp.Working || !ent.Comp.Ready;
    private void OnDeconstructed(Entity<CulinaryStationComponent> ent, ref MachineDeconstructedEvent args) => Evacuate(ent);
    private void OnBreak(Entity<CulinaryStationComponent> ent, ref BreakageEventArgs args) => Evacuate(ent);
    private void OnDestroy(Entity<CulinaryStationComponent> ent, ref DestructionEventArgs args) => Evacuate(ent);

    private void Evacuate(Entity<CulinaryStationComponent> ent)
    {
        ent.Comp.Working = false;
        foreach (var uid in ent.Comp.Ingredients.ContainedEntities.ToArray()) _containers.Remove(uid, ent.Comp.Ingredients);
        // Return the entire remaining batch without manufacturing or activating any properties.
        if (!Batch(ent, out var batch) || batch.Comp.Solution.Volume <= 0) return;
        var output = SpawnNextToOrDrop("EclipseCulinaryPrepared", ent);
        if (!FoodSolution(output, out var food)) return;
        _solutions.RemoveAllSolution(food);
        _solutions.SetCapacity(food, batch.Comp.Solution.Volume);
        _solutions.AddSolution(food, _solutions.SplitSolution(batch, batch.Comp.Solution.Volume));
    }
}
