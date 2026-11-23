using Content.Server.Power.EntitySystems;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Research.Prototypes;
using Content.Shared.Tag;
using Content.Shared.Stacks;
using Robust.Shared.Serialization;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.Industrial;

/// <summary>Passable floor veins share finite stock between manual mining and powered extraction.</summary>
public sealed class IndustrialExtractionSystem : EntitySystem
{
    private static readonly ProtoId<TechnologyPrototype> Automation = "IndustrialAutomation";

    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly IndustrialProcessorSystem _processors = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<IndustrialResourceDepositComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<IndustrialResourceDepositComponent, InteractUsingEvent>(OnManualMine);
        SubscribeLocalEvent<IndustrialResourceDepositComponent, IndustrialDepositMineEvent>(OnMineFinished);
    }

    private void OnExamine(Entity<IndustrialResourceDepositComponent> ent, ref ExaminedEvent args)
        => args.PushMarkup(Loc.GetString("industrial-deposit-reading", ("resource", _prototypes.Index(ent.Comp.Resource).Name), ("amount", ent.Comp.Remaining)));

    private static readonly ProtoId<TagPrototype> PickaxeTag = "Pickaxe";

    private void OnManualMine(Entity<IndustrialResourceDepositComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.Remaining <= 0 || !_tags.HasTag(args.Used, PickaxeTag))
            return;
        args.Handled = true;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(4),
            new IndustrialDepositMineEvent(), ent, ent, used: args.Used)
            { BreakOnMove = true, BreakOnDamage = true, NeedHand = true });
    }

    private void OnMineFinished(Entity<IndustrialResourceDepositComponent> ent, ref IndustrialDepositMineEvent args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.Remaining <= 0)
            return;
        SpawnUnit(ent.Comp.Resource, Transform(ent).Coordinates);
        ent.Comp.Remaining--;
        args.Handled = true;
    }

    private EntityUid SpawnUnit(EntProtoId prototype, Robust.Shared.Map.EntityCoordinates coordinates)
    {
        var product = Spawn(prototype, coordinates);
        if (TryComp<StackComponent>(product, out var stack))
            _stacks.SetCount((product, stack), 1);
        return product;
    }

    public (string? Resource, int Remaining) GetDepositStatus(EntityUid uid)
    {
        var deposit = FindDeposit(uid, Transform(uid));
        return deposit == null ? (null, 0) : (_prototypes.Index(deposit.Resource).Name, deposit.Remaining);
    }

    private IndustrialResourceDepositComponent? FindDeposit(EntityUid uid, TransformComponent xform)
    {
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        var tile = _map.TileIndicesFor(gridUid, grid, _transform.GetMapCoordinates(uid));
        var entities = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        IndustrialResourceDepositComponent? exhausted = null;
        while (entities.MoveNext(out var entity))
        {
            if (!TryComp<IndustrialResourceDepositComponent>(entity, out var deposit))
                continue;
            if (deposit.Remaining > 0)
                return deposit;
            exhausted ??= deposit;
        }
        return exhausted;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<IndustrialExtractorComponent, IndustrialProcessorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var extractor, out var processor, out var xform))
        {
            var state = UpdateExtractor(uid, extractor, processor, xform, frameTime);
            if (extractor.State == state)
                continue;
            extractor.State = state;
            _processors.RefreshExternalStatus(uid);
        }
    }

    private IndustrialProcessorState UpdateExtractor(EntityUid uid, IndustrialExtractorComponent extractor,
        IndustrialProcessorComponent processor, TransformComponent xform, float frameTime)
    {
        if (!processor.Enabled)
            return IndustrialProcessorState.Paused;
        if (!xform.Anchored || FindDeposit(uid, xform) is not { Remaining: > 0 } deposit)
            return IndustrialProcessorState.Idle;
        if (!TryComp<IndustrialMachineKnowledgeComponent>(uid, out var knowledge) || !knowledge.Technologies.Contains(Automation))
            return IndustrialProcessorState.Unresearched;
        if (!_containers.TryGetContainer(uid, processor.OutputContainerId, out var output) || output.Count >= processor.MaxOutputSlots)
            return IndustrialProcessorState.Blocked;
        if (!this.IsPowered(uid, EntityManager))
            return IndustrialProcessorState.Unpowered;

        extractor.Elapsed = MathF.Min(extractor.Interval, extractor.Elapsed + frameTime);
        if (extractor.Elapsed < extractor.Interval)
            return IndustrialProcessorState.Working;

        extractor.Elapsed = 0;
        var amount = Math.Min(Math.Min(extractor.Amount, deposit.Remaining), processor.MaxOutputSlots - output.Count);
        for (var i = 0; i < amount; i++)
        {
            var product = SpawnUnit(deposit.Resource, xform.Coordinates);
            if (!_containers.Insert(product, output))
            {
                Del(product);
                return IndustrialProcessorState.Blocked;
            }
            deposit.Remaining--;
        }
        return IndustrialProcessorState.Working;
    }
}

