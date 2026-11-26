using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Research.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Research.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.ActionBlocker;
using Content.Shared.Access.Components;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.Industrial;

public sealed class IndustrialProgressionSystem : EntitySystem
{
    [Dependency] private readonly ResearchSystem _research = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IMapManager _maps = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly StationSystem _stations = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<IndustrialDataDiskComponent, AfterInteractEvent>(OnDisk);
        SubscribeLocalEvent<IndustrialDataDiskComponent, ExaminedEvent>(OnDiskExamined);
        SubscribeLocalEvent<IndustrialBlueprintComponent, AfterInteractEvent>(OnBlueprint);
        SubscribeLocalEvent<ResearchConsoleComponent, IndustrialOpenDiskWriterMessage>(OnOpenDiskWriter);
        SubscribeLocalEvent<IndustrialDataDiskComponent, IndustrialWriteDiskMessage>(OnWriteDisk);
        SubscribeLocalEvent<IndustrialResearchSampleComponent, AfterInteractEvent>(OnSample);
        SubscribeLocalEvent<IndustrialResearchSampleComponent, IndustrialAnalyzeSampleEvent>(OnSampleStudied);
    }

    private void OnDisk(Entity<IndustrialDataDiskComponent> disk, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;
        if (HasComp<ResearchConsoleComponent>(target))
        {
            args.Handled = true;
            OpenDiskWriter(disk, target, args.User);
        }
        else if (HasComp<IndustrialProcessorComponent>(target) || HasComp<IndustrialWorkbenchComponent>(target))
        {
            InstallDisk(target, disk.Comp);
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("industrial-disk-installed", ("count", disk.Comp.Technologies.Count)), target, args.User);
        }
    }

    private void OnOpenDiskWriter(Entity<ResearchConsoleComponent> ent, ref IndustrialOpenDiskWriterMessage args)
    {
        if (!_hands.TryGetActiveItem(args.Actor, out var held) || !TryComp<IndustrialDataDiskComponent>(held, out var disk))
        {
            _popup.PopupEntity(Loc.GetString("industrial-disk-hold"), ent, args.Actor);
            return;
        }

        OpenDiskWriter((held.Value, disk), ent, args.Actor);
    }

    private void OpenDiskWriter(Entity<IndustrialDataDiskComponent> disk, EntityUid console, EntityUid user)
    {
        if (!TryGetDiskDatabase(disk, console, user, out var database))
        {
            _popup.PopupEntity(Loc.GetString("industrial-disk-writer-unavailable"), console, user);
            return;
        }

        var technologies = database.UnlockedTechnologies.Where(IsRecordableTechnology).Distinct().ToArray();
        _ui.SetUiState(disk.Owner, IndustrialDiskUiKey.Write,
            new IndustrialDiskWriterState(GetNetEntity(console), technologies, disk.Comp.Technologies.ToArray()));
        _ui.TryOpenUi(disk.Owner, IndustrialDiskUiKey.Write, user);
    }

    private void OnWriteDisk(Entity<IndustrialDataDiskComponent> disk, ref IndustrialWriteDiskMessage args)
    {
        if (!_ui.TryGetUiState<IndustrialDiskWriterState>(disk.Owner, IndustrialDiskUiKey.Write, out var state) ||
            !TryGetEntity(state.Console, out var console) ||
            !TryWriteDisk(disk, console.Value, args.Actor, args.Technologies))
        {
            _popup.PopupEntity(Loc.GetString("industrial-disk-writer-rejected"), args.Actor, args.Actor);
            return;
        }

        _ui.CloseUi(disk.Owner, IndustrialDiskUiKey.Write, args.Actor);
    }

    public bool TryWriteDisk(Entity<IndustrialDataDiskComponent> disk, EntityUid console, EntityUid user,
        ProtoId<TechnologyPrototype>[] selection)
    {
        if (selection.Length is 0 or > IndustrialDataDiskComponent.Capacity ||
            !TryGetDiskDatabase(disk, console, user, out var database))
            return false;

        var technologies = selection.ToHashSet();
        var unlocked = database.UnlockedTechnologies;
        if (technologies.Count != selection.Length ||
            !technologies.All(id => unlocked.Contains(id) && IsRecordableTechnology(id)))
            return false;

        disk.Comp.Technologies = technologies;
        Dirty(disk);
        _popup.PopupEntity(Loc.GetString("industrial-disk-written", ("count", disk.Comp.Technologies.Count)), console, user);
        return true;
    }

    private bool TryGetDiskDatabase(EntityUid disk, EntityUid console, EntityUid user,
        [NotNullWhen(true)] out TechnologyDatabaseComponent? database)
    {
        database = null;
        return _hands.IsHolding(user, disk) && _actionBlocker.CanInteract(user, console) &&
               _interaction.InRangeAndAccessible(user, console) && _research.CanResearchAtTerminal(console) &&
               _research.TryGetClientServer(console, out var server, out _) && TryComp(server, out database);
    }

    private bool IsRecordableTechnology(ProtoId<TechnologyPrototype> id)
        => _prototypes.TryIndex(id, out var technology) && !technology.Hidden;

    public void InstallDisk(EntityUid machine, IndustrialDataDiskComponent disk)
    {
        var knowledge = EnsureComp<IndustrialMachineKnowledgeComponent>(machine);
        knowledge.Technologies.UnionWith(disk.Technologies);
        Dirty(machine, knowledge);
        if (TryComp<IndustrialProcessorComponent>(machine, out var processor))
        {
            processor.PendingStateRefresh = true;
            processor.RecipeCatalog = null;
        }
    }

    private void OnSample(Entity<IndustrialResearchSampleComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasComp<ResearchConsoleComponent>(target))
            return;
        args.Handled = true;
        if (!_research.CanResearchAtTerminal(target))
        {
            _popup.PopupEntity(Loc.GetString("research-terminal-login-required"), target, args.User);
            return;
        }
        if (!_research.TryGetClientServer(target, out var server, out _) || SampleAward(ent, server.Value) <= 0)
        {
            _popup.PopupEntity(Loc.GetString("industrial-sample-exhausted"), target, args.User);
            return;
        }
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(ent.Comp.StudySeconds),
            new IndustrialAnalyzeSampleEvent(), ent, target, used: ent)
            { BreakOnMove = true, BreakOnDamage = true, NeedHand = true, DistanceThreshold = 1.5f });
    }

    private void OnSampleStudied(Entity<IndustrialResearchSampleComponent> ent, ref IndustrialAnalyzeSampleEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target || !HasComp<ResearchConsoleComponent>(target) ||
            !this.IsPowered(target, EntityManager) ||
            !_research.CanResearchAtTerminal(target) ||
            !_research.TryGetClientServer(target, out var server, out _))
            return;
        var points = SampleAward(ent, server.Value);
        if (points <= 0)
        {
            _popup.PopupEntity(Loc.GetString("industrial-sample-exhausted"), target, args.User);
            return;
        }
        var ledger = EnsureComp<IndustrialSampleLedgerComponent>(_stations.GetOwningStation(server.Value) ?? server.Value);
        var family = MetaData(ent).EntityPrototype!.ID;
        ledger.Studied[family] = ledger.Studied.GetValueOrDefault(family) + 1;
        _research.ModifyServerPoints(server.Value, points);
        _popup.PopupEntity(Loc.GetString("industrial-sample-studied", ("points", points)), target, args.User);
        Del(ent);
        args.Handled = true;
    }

    private int SampleAward(Entity<IndustrialResearchSampleComponent> sample, EntityUid server)
    {
        if (MetaData(sample).EntityPrototype is not { } prototype) return 0;
        var ledger = EnsureComp<IndustrialSampleLedgerComponent>(_stations.GetOwningStation(server) ?? server);
        var studied = ledger.Studied.GetValueOrDefault(prototype.ID);
        return studied >= sample.Comp.MaximumStudies ? 0 : Math.Max(0, sample.Comp.Points / (studied + 1));
    }

    private void OnDiskExamined(Entity<IndustrialDataDiskComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("industrial-disk-description", ("count", ent.Comp.Technologies.Count)));
        if (ent.Comp.Technologies.Count == 0)
            return;

        var names = ent.Comp.Technologies.Select(id => _prototypes.TryIndex(id, out var technology) ? Loc.GetString(technology.Name) : id.Id)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase);
        args.PushText(string.Join(", ", names));
    }

    private void OnBlueprint(Entity<IndustrialBlueprintComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.ClickLocation.EntityId == EntityUid.Invalid)
            return;
        var coords = _transform.ToMapCoordinates(args.ClickLocation);
        if (!_maps.TryFindGridAt(coords, out var gridUid, out var grid))
            return;
        var tile = _map.TileIndicesFor(gridUid, grid, coords);
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var other))
        {
            if (HasComp<IndustrialProcessorComponent>(other) || HasComp<IndustrialMachineChassisComponent>(other) ||
                HasComp<Content.Server.Construction.Components.ConstructionComponent>(other) ||
                TryComp<Robust.Shared.Physics.Components.PhysicsComponent>(other, out var body) &&
                    (body.CollisionLayer & (int) Content.Shared.Physics.CollisionGroup.Impassable) != 0)
                return;
        }
        Spawn(ent.Comp.Frame, _map.GridTileToLocal(gridUid, grid, tile));
        Del(ent);
        args.Handled = true;
    }
}
