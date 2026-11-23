using System.Linq;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Botany.Components;
using Content.Server.Power.Components;
using Content.Shared._Eclipse.Beekeeping;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Construction;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Tag;
using Content.Shared.UserInterface;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Eclipse.Beekeeping;

/// <summary>Round-local apiculture. Breeding and rare-product rolls occur once per completed, supplied lifecycle.</summary>
public sealed partial class ApiarySystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedStackSystem _stacks = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    private float _accumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<ApiaryComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ApiaryComponent, InteractUsingEvent>(OnInteract);
        SubscribeLocalEvent<ApiaryComponent, BoundUIOpenedEvent>(OnOpen);
        SubscribeLocalEvent<ApiaryComponent, ApiaryActionMessage>(OnAction);
        SubscribeLocalEvent<ApiaryComponent, ApiarySlotMessage>(OnSlot);
        SubscribeLocalEvent<ApiaryComponent, BreakageEventArgs>(OnBreak);
        SubscribeLocalEvent<ApiaryComponent, DestructionEventArgs>(OnDestroy);
        SubscribeLocalEvent<ApiaryComponent, MachineDeconstructedEvent>(OnDeconstructed);
        SubscribeLocalEvent<BeeSpecimenComponent, MapInitEvent>(OnBeeInit);
        SubscribeLocalEvent<BeeSpecimenComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<BeeAnalyzerComponent, AfterInteractEvent>(OnAnalyse);
        SubscribeLocalEvent<BeeNetComponent, AfterInteractEvent>(OnCatch);
        SubscribeLocalEvent<BeeSmokerComponent, InteractUsingEvent>(OnRefuel);
        SubscribeLocalEvent<BeeSmokerComponent, ExaminedEvent>(OnSmokerExamined);
        SubscribeLocalEvent<DamageableComponent, BeforeDamageChangedEvent>(OnBeeDamage);
    }

    private void OnInit(Entity<ApiaryComponent> ent, ref ComponentInit args)
    {
        ent.Comp.Input = _containers.EnsureContainer<Container>(ent, "apiary-input");
        ent.Comp.Output = _containers.EnsureContainer<Container>(ent, "apiary-output");
    }

    private void OnInteract(Entity<ApiaryComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled) return;
        args.Handled = true;
        var source = args.Used;
        if (TryComp<BeeSmokerComponent>(source, out var smoker))
        {
            if (smoker.Charges <= 0) { Tell(ent, args.User, "apiary-smoker-empty"); return; }
            smoker.Charges--;
            ent.Comp.SmokeSeconds = 60;
            Tell(ent, args.User, "apiary-smoked");
        }
        else if (TryComp<ApiaryFrameComponent>(source, out var frame))
        {
            if (ent.Comp.Mother != null) { Tell(ent, args.User, "apiary-ui-frame-locked"); return; }
            if (ent.Comp.FrameDurability > 0) { Tell(ent, args.User, "apiary-frame-present"); return; }
            ent.Comp.FrameDurability = Math.Clamp(frame.Durability, 1, 64);
            ent.Comp.FrameKind = frame.Kind;
            QueueDel(source);
            Sting(ent, args.User);
        }
        else if (TryComp<BeeSpecimenComponent>(source, out var bee))
        {
            if (ent.Comp.Mother != null || ent.Comp.Input.ContainedEntities.Count >= 2
                || ent.Comp.Input.ContainedEntities.Any(uid => Comp<BeeSpecimenComponent>(uid).Caste == bee.Caste))
            { Tell(ent, args.User, "apiary-parent-slots"); return; }
            if (_containers.Insert(source, ent.Comp.Input)) Sting(ent, args.User);
        }
        else if (TryComp<DrainableSolutionComponent>(source, out var drain)
            && _solutions.TryGetSolution(source, drain.Solution, out var liquid))
        {
            var solution = liquid.Value.Comp.Solution;
            var water = solution.GetTotalPrototypeQuantity("Water");
            var sugar = solution.GetTotalPrototypeQuantity("Sugar");
            if (water > 0 && ent.Comp.Water < 200)
            {
                var amount = FixedPoint2.Min(water, FixedPoint2.New(Math.Min(20, 200 - ent.Comp.Water)));
                ent.Comp.Water += _solutions.RemoveReagent(liquid.Value, "Water", amount).Float();
            }
            else if (sugar > 0 && ent.Comp.Nectar < 200)
            {
                var amount = FixedPoint2.Min(sugar, FixedPoint2.New(Math.Min(10, (200 - ent.Comp.Nectar) / 2)));
                ent.Comp.Nectar += _solutions.RemoveReagent(liquid.Value, "Sugar", amount).Float() * 2;
            }
            else Tell(ent, args.User, "apiary-needs-forage");
        }
        else if (HasComp<ProduceComponent>(source) || MetaData(source).EntityPrototype?.ID.StartsWith("EclipseFood") == true)
        {
            if (ent.Comp.Nectar >= 200) { Tell(ent, args.User, "apiary-feed-full"); return; }
            TryComp<EdibleComponent>(source, out var edible);
            var foodName = TryComp<ProduceComponent>(source, out var produce) ? produce.SolutionName : edible?.Solution;
            if (foodName != null && _solutions.TryGetSolution(source, foodName, out var food))
            {
                var amount = FixedPoint2.Min(FixedPoint2.New(5), food.Value.Comp.Solution.Volume);
                if (amount <= 0) return;
                _solutions.SplitSolution(food.Value, amount);
                ent.Comp.Nectar = Math.Min(200, ent.Comp.Nectar + amount.Float() * 2);
                if (food.Value.Comp.Solution.Volume <= 0 && (edible?.DestroyOnEmpty ?? true)) QueueDel(source);
            }
        }
        else if (TryComp<StackComponent>(source, out var stack)
            && (stack.StackTypeId == "Steel" || stack.StackTypeId == "Gold" || stack.StackTypeId == "PlasmaOre"))
        {
            if (ent.Comp.Minerals < 50 && _stacks.TryUse((source, stack), 1)) ent.Comp.Minerals += 5;
        }
        else Tell(ent, args.User, "apiary-needs-forage");
        Publish(ent);
    }

    private void OnAction(Entity<ApiaryComponent> ent, ref ApiaryActionMessage args)
    {
        switch (args.Action)
        {
            case ApiaryAction.RemoveFrame:
                if (ent.Comp.Mother != null || ent.Comp.FrameDurability <= 0) return;
                var framePrototype = ent.Comp.FrameKind switch
                {
                    "Selection" => "EclipseApiarySelectionFrame", "Nursing" => "EclipseApiaryNursingFrame",
                    "Insulated" => "EclipseApiaryInsulatedFrame", _ => "EclipseApiaryFrame",
                };
                var frame = SpawnNextToOrDrop(framePrototype, ent);
                Comp<ApiaryFrameComponent>(frame).Durability = ent.Comp.FrameDurability;
                ent.Comp.FrameDurability = 0;
                _hands.TryPickupAnyHand(args.Actor, frame);
                Sting(ent, args.Actor);
                break;
            case ApiaryAction.InsertHeld:
                if (_hands.GetActiveItem(args.Actor) is {} held)
                {
                    var use = new InteractUsingEvent(args.Actor, held, ent, Transform(ent).Coordinates);
                    OnInteract(ent, ref use);
                }
                break;
            case ApiaryAction.Toggle:
                if (ent.Comp.Mother != null)
                {
                    ent.Comp.Enabled = !ent.Comp.Enabled;
                    if (!ent.Comp.Enabled) ent.Comp.Status = "apiary-status-paused";
                }
                else Start(ent);
                break;
            case ApiaryAction.Harvest:
                Sting(ent, args.Actor);
                foreach (var uid in ent.Comp.Output.ContainedEntities.ToArray())
                { _containers.Remove(uid, ent.Comp.Output); _hands.TryPickupAnyHand(args.Actor, uid); }
                break;
            case ApiaryAction.Eject:
                if (ent.Comp.Mother != null) return;
                Sting(ent, args.Actor);
                foreach (var uid in ent.Comp.Input.ContainedEntities.ToArray())
                { _containers.Remove(uid, ent.Comp.Input); _hands.TryPickupAnyHand(args.Actor, uid); }
                break;
            case ApiaryAction.Analyse:
                var scanner = _hands.GetActiveItem(args.Actor);
                if (scanner == null || !HasComp<BeeAnalyzerComponent>(scanner))
                { Tell(ent, args.Actor, "apiary-needs-analyzer"); return; }
                var report = ent.Comp.Mother != null ? GenomeText(ent.Comp.Mother)
                    : string.Join("\n", ent.Comp.Input.ContainedEntities.Select(uid => GenomeText(Comp<BeeSpecimenComponent>(uid).Genome)));
                _popup.PopupEntity(report, ent, args.Actor);
                break;
        }
        Publish(ent);
    }

    private void OnSlot(Entity<ApiaryComponent> ent, ref ApiarySlotMessage args)
    {
        if (args.Index < 0) return;
        var container = args.Output ? ent.Comp.Output : ent.Comp.Input;
        if (!args.Output && ent.Comp.Mother != null) return;
        var items = args.Output ? container.ContainedEntities.ToArray() : container.ContainedEntities
            .OrderBy(uid => Comp<BeeSpecimenComponent>(uid).Caste).ToArray();
        if (args.Index >= items.Length) return;
        Sting(ent, args.Actor);
        _containers.Remove(items[args.Index], container);
        _hands.TryPickupAnyHand(args.Actor, items[args.Index]);
        Publish(ent);
    }

    public bool Start(Entity<ApiaryComponent> ent)
    {
        if (ent.Comp.Mother != null) return false;
        var parents = ent.Comp.Input.ContainedEntities;
        var princess = parents.FirstOrDefault(uid => Comp<BeeSpecimenComponent>(uid).Caste == BeeCaste.Princess, EntityUid.Invalid);
        var drone = parents.FirstOrDefault(uid => Comp<BeeSpecimenComponent>(uid).Caste == BeeCaste.Drone, EntityUid.Invalid);
        if (!princess.IsValid() || !drone.IsValid()) { ent.Comp.Status = "apiary-status-parents"; Publish(ent); return false; }
        if (_prototypes.Index<BeeSpeciesPrototype>(Comp<BeeSpecimenComponent>(princess).Genome.Dominant).Tier >= 4 && !ent.Comp.ControlledClimate)
        { ent.Comp.Status = "apiary-status-advanced"; Publish(ent); return false; }
        ent.Comp.Mother = Comp<BeeSpecimenComponent>(princess).Genome.Clone();
        ent.Comp.Father = Comp<BeeSpecimenComponent>(drone).Genome.Clone();
        ent.Comp.CycleSeconds = _prototypes.Index<BeeSpeciesPrototype>(ent.Comp.Mother.Dominant).CycleSeconds;
        ent.Comp.Progress = 0;
        ent.Comp.Enabled = true;
        foreach (var uid in new[] { princess, drone }) { _containers.Remove(uid, ent.Comp.Input); QueueDel(uid); }
        Publish(ent);
        return true;
    }

    public override void Update(float frameTime)
    {
        _accumulator += frameTime;
        if (_accumulator < 2) return;
        var seconds = _accumulator; _accumulator = 0;
        var query = EntityQueryEnumerator<ApiaryComponent>();
        while (query.MoveNext(out var uid, out var hive))
        {
            if (Paused(uid)) continue;
            hive.SmokeSeconds = Math.Max(0, hive.SmokeSeconds - seconds);
            var gas = _atmos.GetContainingMixture(uid, true, true);
            hive.Temperature = gas?.Temperature ?? 0;
            hive.Pressure = gas?.Pressure ?? 0;
            if (hive.Mother == null || !hive.Enabled) { Publish((uid, hive)); continue; }
            var species = _prototypes.Index<BeeSpeciesPrototype>(hive.Mother.Dominant);
            var tolerance = hive.Mother.Hardiness * 2 + (hive.FrameDurability > 0 ? ApiaryFrameRules.TemperatureTolerance(hive.FrameKind) : 0);
            var powered = TryComp<ApcPowerReceiverComponent>(uid, out var power) && power.Powered;
            if (gas == null || gas.Pressure < 40 || gas.Pressure > 160
                || (!hive.ControlledClimate && (gas.Temperature < species.MinTemperature - tolerance || gas.Temperature > species.MaxTemperature + tolerance)))
                hive.Status = "apiary-status-climate";
            else if (hive.ControlledClimate && !powered) hive.Status = "apiary-status-power";
            else AdvanceActive((uid, hive), seconds);
            Publish((uid, hive));
        }
    }

    /// <summary>Advance supplied, climate-compatible work only. Incomplete and paused cycles never roll.</summary>
    public void AdvanceActive(Entity<ApiaryComponent> ent, float seconds)
    {
        if (ent.Comp.Mother == null || !ent.Comp.Enabled || !float.IsFinite(seconds) || seconds <= 0) return;
        var species = _prototypes.Index<BeeSpeciesPrototype>(ent.Comp.Mother.Dominant);
        if (species.Tier >= 4 && !ent.Comp.ControlledClimate) { ent.Comp.Status = "apiary-status-advanced"; return; }
        if (species.NeedsMinerals && ent.Comp.Minerals <= 0) { ent.Comp.Status = "apiary-status-minerals"; return; }
        if (ent.Comp.Output.ContainedEntities.Count > 24) { ent.Comp.Status = "apiary-status-full"; return; }
        var step = Math.Min(seconds, Math.Max(0, ent.Comp.CycleSeconds - ent.Comp.Progress));
        var kind = ent.Comp.FrameDurability > 0 ? ent.Comp.FrameKind : "None";
        var food = step * 0.04f * ApiaryFrameRules.FoodMultiplier(kind);
        var water = step * 0.06f * ApiaryFrameRules.WaterMultiplier(kind);
        if (ent.Comp.Nectar < food || ent.Comp.Water < water)
        { ent.Comp.Status = "apiary-status-feed"; return; }
        ent.Comp.Nectar -= food;
        ent.Comp.Water -= water;
        ent.Comp.Progress += step;
        ent.Comp.Status = "apiary-status-working";
        if (ent.Comp.Progress >= ent.Comp.CycleSeconds) Finish(ent);
    }

    private void Finish(Entity<ApiaryComponent> ent)
    {
        var mother = ent.Comp.Mother!;
        var father = ent.Comp.Father!;
        var seconds = ent.Comp.CycleSeconds;
        var species = _prototypes.Index<BeeSpeciesPrototype>(mother.Dominant);
        var minerals = ent.Comp.Minerals > 0;
        // Commit completion before creating output. No UI action can repeat the roll.
        ent.Comp.Mother = null; ent.Comp.Father = null; ent.Comp.Enabled = false;
        ent.Comp.Progress = 0; ent.Comp.CompletedCycles++;
        var framed = ent.Comp.FrameDurability > 0;
        var frameKind = framed ? ent.Comp.FrameKind : "None";
        if (framed) ent.Comp.FrameDurability--;
        if (species.NeedsMinerals && minerals) ent.Comp.Minerals--;
        BeeGenome Child()
        {
            var child = BeeGenetics.Cross(mother, father, seconds, () => _random.NextFloat());
            foreach (var mutation in _prototypes.EnumeratePrototypes<BeeMutationPrototype>().OrderBy(p => p.ID))
            {
                if (!BeeGenetics.Eligible(mother, father, mutation, seconds, minerals)) continue;
                var modifier = ApiaryFrameRules.MutationMultiplier(frameKind, _prototypes.Index(mutation.Result).Tier);
                if (!_random.Prob(mutation.Chance * modifier)) continue;
                child.Dominant = mutation.Result.Id;
                child.Recessive = mutation.Result.Id;
                break;
            }
            if (_random.Prob(0.1f)) child.Productivity = Math.Clamp(child.Productivity + (_random.Prob(0.5f) ? 1 : -1), 1, 5);
            if (_random.Prob(0.1f)) child.Hardiness = Math.Clamp(child.Hardiness + (_random.Prob(0.5f) ? 1 : -1), 1, 5);
            if (_random.Prob(0.1f)) child.Fertility = Math.Clamp(child.Fertility + (_random.Prob(0.5f) ? 1 : -1), 1, 5);
            return child;
        }
        AddOutput(ent, CreateBee(ent, Child(), BeeCaste.Princess));
        for (var i = 0; i < Math.Clamp(mother.Fertility, 1, 5) + ApiaryFrameRules.DroneBonus(frameKind); i++) AddOutput(ent, CreateBee(ent, Child(), BeeCaste.Drone));
        for (var i = 0; i < Math.Max(1, 1 + mother.Productivity / 2 + ApiaryFrameRules.CombBonus(frameKind)); i++) AddOutput(ent, SpawnNextToOrDrop(species.Comb, ent));
        if (species.Byproduct != null && _random.Prob(species.ByproductChance))
            AddOutput(ent, SpawnNextToOrDrop(species.Byproduct.Value, ent));
        var worked = mother.Clone(); worked.LineageSeconds += seconds;
        // Neither fertility, productivity nor frames multiply this probability or the number of rare rolls.
        if (BeeGenetics.RareEligible(worked, species) && _random.Prob(species.RareChance))
            AddOutput(ent, SpawnNextToOrDrop(species.RareProduct!.Value, ent));
        var harvest = new ApiaryHarvestEvent();
        RaiseLocalEvent(ent, ref harvest);
        ent.Comp.Status = "apiary-status-finished";
    }

    private void AddOutput(Entity<ApiaryComponent> ent, EntityUid output) => _containers.Insert(output, ent.Comp.Output);

    public EntityUid CreateBee(EntityUid near, BeeGenome genome, BeeCaste caste)
    {
        var bee = SpawnNextToOrDrop(caste == BeeCaste.Princess ? "EclipseBeePrincess" : "EclipseBeeDrone", near);
        var specimen = Comp<BeeSpecimenComponent>(bee);
        specimen.Genome = genome.Clone(); specimen.Caste = caste;
        NameBee((bee, specimen));
        return bee;
    }

    private string Species(string id) => Loc.GetString($"bee-species-{id.ToLowerInvariant()}");
    private string GenomeText(BeeGenome genome) => Loc.GetString("apiary-genome", ("active", Species(genome.Dominant)),
        ("recessive", Species(genome.Recessive)), ("generation", genome.Generation), ("minutes", MathF.Round(genome.LineageSeconds / 60, 1)),
        ("productivity", genome.Productivity), ("fertility", genome.Fertility), ("hardiness", genome.Hardiness));
    private void NameBee(Entity<BeeSpecimenComponent> bee) => _metadata.SetEntityName(bee,
        Loc.GetString(bee.Comp.Caste == BeeCaste.Princess ? "apiary-princess-name" : "apiary-drone-name", ("species", Species(bee.Comp.Genome.Dominant))));
    private void OnBeeInit(Entity<BeeSpecimenComponent> ent, ref MapInitEvent args) => NameBee(ent);
    private void OnExamined(Entity<BeeSpecimenComponent> ent, ref ExaminedEvent args)
        => args.PushText(ent.Comp.Analysed ? GenomeText(ent.Comp.Genome) : Loc.GetString("apiary-unanalyzed"));

    private void OnAnalyse(Entity<BeeAnalyzerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target == null || !TryComp<BeeSpecimenComponent>(args.Target, out var bee)) return;
        bee.Analysed = true; args.Handled = true;
        _popup.PopupEntity(GenomeText(bee.Genome), args.Target.Value, args.User);
    }

    private void OnCatch(Entity<BeeNetComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not {} target || !_tags.HasTag(target, "Bee")
            || HasComp<BeeSpecimenComponent>(target) || !TryComp<MobStateComponent>(target, out var state) || state.CurrentState != MobState.Alive) return;
        args.Handled = true;
        EnsureComp<BeeSpecimenComponent>(target); // Block duplicate catches before deferred deletion runs.
        var genome = new BeeGenome { Dominant = _random.Prob(0.5f) ? "Meadow" : "Forest" };
        genome.Recessive = genome.Dominant;
        var bee = CreateBee(target, genome, _random.Prob(0.3f) ? BeeCaste.Princess : BeeCaste.Drone);
        _hands.TryPickupAnyHand(args.User, bee); QueueDel(target);
    }

    private void OnRefuel(Entity<BeeSmokerComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.Charges >= 30 || !TryComp<StackComponent>(args.Used, out var stack) || stack.StackTypeId != "WoodPlank") return;
        if (!_stacks.TryUse((args.Used, stack), 1)) return;
        args.Handled = true; ent.Comp.Charges = Math.Min(30, ent.Comp.Charges + 10);
    }

    private void OnSmokerExamined(Entity<BeeSmokerComponent> ent, ref ExaminedEvent args)
        => args.PushText(Loc.GetString("apiary-smoker-charges", ("charges", ent.Comp.Charges)));

    public bool Protected(EntityUid actor) => new[] { "head", "outerClothing", "gloves" }
        .All(slot => _inventory.TryGetSlotEntity(actor, slot, out var item) && HasComp<BeekeeperProtectionComponent>(item));

    private void Sting(Entity<ApiaryComponent> hive, EntityUid actor)
    {
        if (hive.Comp.Mother == null && !hive.Comp.Input.ContainedEntities.Any(uid => HasComp<BeeSpecimenComponent>(uid))
            && !hive.Comp.Output.ContainedEntities.Any(uid => HasComp<BeeSpecimenComponent>(uid))) return;
        if (hive.Comp.SmokeSeconds > 0 || Protected(actor) || !_random.Prob(0.35f)) return;
        _damage.TryChangeDamage(actor, new DamageSpecifier
        { DamageDict = new() { ["Piercing"] = FixedPoint2.New(1), ["Poison"] = FixedPoint2.New(1) } }, origin: hive.Owner);
        Tell(hive, actor, "apiary-sting");
    }

    private void OnBeeDamage(Entity<DamageableComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Origin != null && _tags.HasTag(args.Origin.Value, "Bee") && Protected(ent)) args.Cancelled = true;
    }

    private void Publish(Entity<ApiaryComponent> ent)
    {
        if (ent.Comp.Input == null) return;
        var mutations = string.Join("\n", _prototypes.EnumeratePrototypes<BeeMutationPrototype>().OrderBy(p => p.Generation).Select(p =>
            Loc.GetString("apiary-mutation", ("first", Species(p.First.Id)), ("second", Species(p.Second.Id)), ("result", Species(p.Result.Id)),
                ("chance", MathF.Round(p.Chance * 100, 1)), ("generation", p.Generation), ("minutes", p.LineageSeconds / 60),
                ("minerals", Loc.GetString(p.NeedsMinerals ? "apiary-minerals-required" : "apiary-minerals-optional")))));
        _ui.SetUiState(ent.Owner, ApiaryUiKey.Key, new ApiaryState(ent.Comp.Status,
            ent.Comp.Mother == null ? Loc.GetString("apiary-no-queen") : Species(ent.Comp.Mother.Dominant),
            string.Join(", ", ent.Comp.Input.ContainedEntities.Select(uid => MetaData(uid).EntityName)),
            string.Join(", ", ent.Comp.Output.ContainedEntities.Select(uid => MetaData(uid).EntityName)), mutations,
            ent.Comp.CycleSeconds > 0 ? ent.Comp.Progress / ent.Comp.CycleSeconds : 0, ent.Comp.Nectar, ent.Comp.Water,
            ent.Comp.Minerals, ent.Comp.FrameDurability, ent.Comp.CompletedCycles, ent.Comp.Enabled)
        {
            ParentSlots = ent.Comp.Input.ContainedEntities.OrderBy(uid => Comp<BeeSpecimenComponent>(uid).Caste)
                .Select((uid, i) => new Content.Shared._Eclipse.WorkshopItemState(i, MetaData(uid).EntityPrototype?.ID, MetaData(uid).EntityName)
                { Role = Comp<BeeSpecimenComponent>(uid).Caste.ToString() }).ToArray(),
            OutputSlots = ent.Comp.Output.ContainedEntities.Select((uid, i) => new Content.Shared._Eclipse.WorkshopItemState(i,
                MetaData(uid).EntityPrototype?.ID, MetaData(uid).EntityName)).ToArray(),
            Species = _prototypes.EnumeratePrototypes<BeeSpeciesPrototype>().OrderBy(s => s.Tier).ThenBy(s => s.ID).Select(s => new ApiarySpeciesState(
                s.ID, Species(s.ID), s.Tier, s.CycleSeconds, s.Comb.Id, _prototypes.Index(s.Comb).Name,
                s.RareProduct?.Id, s.RareProduct == null ? null : _prototypes.Index(s.RareProduct.Value).Name,
                s.RareChance, s.RareGeneration, s.RareLineageSeconds / 60, s.NeedsMinerals)
                {
                    Branch = s.Branch, MinTemperature = s.MinTemperature, MaxTemperature = s.MaxTemperature,
                    Byproduct = s.Byproduct?.Id, ByproductName = s.Byproduct == null ? null : _prototypes.Index(s.Byproduct.Value).Name,
                    ByproductChance = s.ByproductChance,
                }).ToArray(),
            Crosses = _prototypes.EnumeratePrototypes<BeeMutationPrototype>().OrderBy(p => p.Generation).ThenBy(p => p.ID)
                .Select(p => new ApiaryMutationState(p.Result.Id, p.First.Id, p.Second.Id, p.Chance, p.Generation, p.LineageSeconds / 60, p.NeedsMinerals)).ToArray(),
            ActiveQueen = ent.Comp.Mother != null,
            SecondsRemaining = Math.Max(0, ent.Comp.CycleSeconds - ent.Comp.Progress),
            SmokeSeconds = ent.Comp.SmokeSeconds,
            FrameKind = ent.Comp.FrameKind,
            Generation = ent.Comp.Mother?.Generation ?? 0,
            LineageMinutes = (ent.Comp.Mother?.LineageSeconds ?? 0) / 60,
            Temperature = ent.Comp.Temperature,
            Pressure = ent.Comp.Pressure,
        });
    }

    private void OnOpen(Entity<ApiaryComponent> ent, ref BoundUIOpenedEvent args) => Publish(ent);
    private void Tell(EntityUid ent, EntityUid actor, string key) => _popup.PopupEntity(Loc.GetString(key), ent, actor);
    private void OnBreak(Entity<ApiaryComponent> ent, ref BreakageEventArgs args) => Evacuate(ent);
    private void OnDestroy(Entity<ApiaryComponent> ent, ref DestructionEventArgs args) => Evacuate(ent);
    private void OnDeconstructed(Entity<ApiaryComponent> ent, ref MachineDeconstructedEvent args) => Evacuate(ent);
    private void Evacuate(Entity<ApiaryComponent> ent)
    {
        if (ent.Comp.Input == null) return;
        var mother = ent.Comp.Mother; var father = ent.Comp.Father;
        ent.Comp.Mother = ent.Comp.Father = null; ent.Comp.Enabled = false; ent.Comp.Progress = 0;
        if (mother != null) CreateBee(ent, mother, BeeCaste.Princess);
        if (father != null) CreateBee(ent, father, BeeCaste.Drone);
        foreach (var container in new[] { ent.Comp.Input, ent.Comp.Output })
            foreach (var uid in container.ContainedEntities.ToArray()) _containers.Remove(uid, container);
    }
}
