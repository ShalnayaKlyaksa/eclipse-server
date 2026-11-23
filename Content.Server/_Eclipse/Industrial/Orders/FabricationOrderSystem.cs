using System.Linq;
using Content.Server.Power.EntitySystems;
using Content.Server.Research.Components;
using Content.Server.Station.Systems;
using Content.Shared._Eclipse.Industrial.Orders;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared.Access.Systems;
using Content.Shared.Popups;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Eclipse.Industrial.Orders;

public sealed class FabricationOrderSystem : EntitySystem
{
    [Dependency] private readonly StationSystem _stations = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    private float _elapsed;

    public override void Initialize()
    {
        SubscribeLocalEvent<FabricationOrderConsoleComponent, BoundUIOpenedEvent>((EntityUid uid, FabricationOrderConsoleComponent comp, BoundUIOpenedEvent args) => Refresh(uid, comp));
        SubscribeLocalEvent<FabricationOrderConsoleComponent, FabricationOrderSubmitMessage>((uid, _, args) => Submit(uid, args.Actor, args.Product, args.Quantity, args.Reason, args.Urgent));
        SubscribeLocalEvent<FabricationOrderConsoleComponent, FabricationOrderActionMessage>((uid, _, args) => Act(uid, args.Actor, args.Id, args.Revision, args.Action, args.Priority, args.Response));
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 2) return;
        _elapsed = 0;
        var query = EntityQueryEnumerator<FabricationOrderConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
            if (_ui.IsUiOpen(uid, FabricationOrderUiKey.Key)) Refresh(uid, console);
    }

    private bool Context(EntityUid console, EntityUid actor, out FabricationOrderConsoleComponent component, out EntityUid station)
    {
        station = default;
        if (!TryComp(console, out component!) || !this.IsPowered(console, EntityManager) ||
            !_access.IsAllowed(actor, console) || _stations.GetOwningStation(console) is not { } owner || Deleted(owner))
        {
            return Deny(console, actor, "fabrication-orders-denied");
        }
        station = owner;
        return true;
    }

    private bool Deny(EntityUid console, EntityUid actor, string key)
    {
        var message = Loc.GetString(key);
        _popup.PopupEntity(message, console, actor);
        if (TryComp<FabricationOrderConsoleComponent>(console, out var component))
        {
            component.LastNotice = message;
            Refresh(console, component);
        }
        return false;
    }

    public bool Submit(EntityUid console, EntityUid actor, string product, int quantity, string reason, bool urgent)
    {
        if (!Context(console, actor, out var terminal, out var station)) return false;
        if (terminal.Dispatcher || string.IsNullOrWhiteSpace(product) || !_prototypes.TryIndex<FabricationOrderProductPrototype>(product, out var entry) ||
            quantity < 1 || quantity > entry.MaxQuantity || string.IsNullOrWhiteSpace(reason) || reason.Length > 240 || reason.Any(ch => char.IsControl(ch) && ch != '\n' && ch != '\r'))
            return Deny(console, actor, "fabrication-orders-invalid");
        var ledger = EnsureComp<StationFabricationOrdersComponent>(station);
        if (ledger.NextRequest.GetValueOrDefault(terminal.Department) > _timing.CurTime)
            return Deny(console, actor, "fabrication-orders-throttle");
        if (ledger.Orders.Count(o => !FabricationOrderInfo.Closed(o.Status) && o.Department == terminal.Department) >= 12 ||
            ledger.Orders.Count(o => !FabricationOrderInfo.Closed(o.Status)) >= 96)
            return Deny(console, actor, "fabrication-orders-full");
        // Preserve the live queue and bound the archive; never recycle order numbers.
        if (ledger.Orders.Count >= 192)
        {
            var oldest = ledger.Orders.FirstOrDefault(o => FabricationOrderInfo.Closed(o.Status));
            if (oldest != null) ledger.Orders.Remove(oldest);
        }
        var order = new FabricationOrderRecord
        {
            Id = ledger.NextId++, Department = terminal.Department, Product = product, Quantity = quantity,
            Requester = Name(actor), Reason = reason.Trim(), Urgent = urgent, Created = _timing.CurTime,
        };
        ledger.Orders.Add(order);
        ledger.NextRequest[terminal.Department] = _timing.CurTime + TimeSpan.FromSeconds(8);
        terminal.LastNotice = Loc.GetString("fabrication-orders-submitted", ("id", order.Id));
        _popup.PopupEntity(terminal.LastNotice, console, actor);
        RefreshStation(station);
        return true;
    }

    public bool Act(EntityUid console, EntityUid actor, int id, int revision, FabricationOrderAction action, int priority, string response)
    {
        if (!Context(console, actor, out var terminal, out var station)) return false;
        if (!TryComp<StationFabricationOrdersComponent>(station, out var ledger) ||
            ledger.Orders.FirstOrDefault(o => o.Id == id) is not { } order || order.Revision != revision)
            return Deny(console, actor, "fabrication-orders-stale");
        if (priority is < 0 or > 2 || response == null || response.Length > 240 || response.Any(char.IsControl) || FabricationOrderInfo.Closed(order.Status))
            return Deny(console, actor, "fabrication-orders-invalid");
        var ownDepartment = !terminal.Dispatcher && terminal.Department == order.Department;
        var allowed = action switch
        {
            FabricationOrderAction.Cancel => ownDepartment && order.Status != FabricationOrderStatus.Ready,
            FabricationOrderAction.Complete => (terminal.Dispatcher || ownDepartment) && order.Status == FabricationOrderStatus.Ready,
            FabricationOrderAction.Accept => terminal.Dispatcher && order.Status is FabricationOrderStatus.New or FabricationOrderStatus.WaitingMaterials or FabricationOrderStatus.WaitingResearch,
            FabricationOrderAction.WaitMaterials or FabricationOrderAction.WaitResearch => terminal.Dispatcher && order.Status != FabricationOrderStatus.Ready,
            FabricationOrderAction.Ready => terminal.Dispatcher && order.Status == FabricationOrderStatus.InProgress,
            FabricationOrderAction.Reject => terminal.Dispatcher && order.Status != FabricationOrderStatus.Ready && !string.IsNullOrWhiteSpace(response),
            FabricationOrderAction.Update => terminal.Dispatcher,
            _ => false,
        };
        if (!allowed) return Deny(console, actor, "fabrication-orders-denied");
        order.Status = action switch
        {
            FabricationOrderAction.Accept => FabricationOrderStatus.InProgress,
            FabricationOrderAction.WaitMaterials => FabricationOrderStatus.WaitingMaterials,
            FabricationOrderAction.WaitResearch => FabricationOrderStatus.WaitingResearch,
            FabricationOrderAction.Ready => FabricationOrderStatus.Ready,
            FabricationOrderAction.Complete => FabricationOrderStatus.Completed,
            FabricationOrderAction.Cancel => FabricationOrderStatus.Cancelled,
            FabricationOrderAction.Reject => FabricationOrderStatus.Rejected,
            _ => order.Status,
        };
        if (terminal.Dispatcher)
        {
            order.Priority = priority;
            order.Response = response.Trim();
            if (action == FabricationOrderAction.Accept) order.Assignee = Name(actor);
        }
        order.Revision++;
        terminal.LastNotice = Loc.GetString("fabrication-orders-updated", ("id", order.Id));
        RefreshStation(station);
        return true;
    }

    private void RefreshStation(EntityUid station)
    {
        var query = EntityQueryEnumerator<FabricationOrderConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
            if (_stations.GetOwningStation(uid) == station && _ui.IsUiOpen(uid, FabricationOrderUiKey.Key)) Refresh(uid, console);
    }

    public FabricationOrderState GetState(EntityUid uid, FabricationOrderConsoleComponent console)
    {
        var station = _stations.GetOwningStation(uid);
        var connected = station != null && !Deleted(station.Value) && this.IsPowered(uid, EntityManager);
        var knowledge = new HashSet<string>();
        var servers = EntityQueryEnumerator<ResearchServerComponent, TechnologyDatabaseComponent>();
        while (servers.MoveNext(out var server, out _, out var database))
            if (station != null && _stations.GetOwningStation(server) == station) knowledge.UnionWith(database.UnlockedTechnologies.Select(t => t.Id));
        var gates = new Dictionary<string, List<string?>>();
        void Gate(string product, string? technology)
        {
            if (!gates.TryGetValue(product, out var list)) gates[product] = list = new();
            list.Add(technology);
        }
        foreach (var recipe in _prototypes.EnumeratePrototypes<IndustrialRecipePrototype>())
            foreach (var product in recipe.Outputs.Keys) Gate(product, recipe.RequiredTechnology?.Id);
        foreach (var recipe in _prototypes.EnumeratePrototypes<IndustrialFabricationRecipePrototype>()) Gate(recipe.Result.Id, recipe.RequiredTechnology?.Id);
        var products = _prototypes.EnumeratePrototypes<FabricationOrderProductPrototype>().Select(product =>
        {
            var licenses = gates.GetValueOrDefault(product.Product.Id) ?? new();
            var researched = licenses.Any(t => t == null || knowledge.Contains(t));
            var missing = researched ? null : licenses.Where(t => t != null).Select(t => _prototypes.Index<TechnologyPrototype>(t!))
                .OrderBy(t => t.Cost).FirstOrDefault();
            return new FabricationOrderProductInfo(product.ID, product.Product.Id, _prototypes.Index(product.Product).Name,
                product.Category, product.MaxQuantity, researched, missing == null ? null : Loc.GetString(missing.Name));
        }).ToArray();
        var catalog = products.ToDictionary(p => p.Id);
        var orders = new List<FabricationOrderInfo>();
        if (connected && TryComp<StationFabricationOrdersComponent>(station, out var ledger))
            foreach (var order in ledger.Orders)
            {
                if (!catalog.TryGetValue(order.Product, out var product)) continue;
                orders.Add(new(order.Id, order.Revision, order.Department, product.Product, product.Name, order.Quantity, order.Requester,
                    order.Reason, order.Urgent, order.Priority, order.Status, order.Assignee, order.Response,
                    Math.Max(0, (int) (_timing.CurTime - order.Created).TotalSeconds), product.Researched, product.Technology));
            }
        return new(console.Dispatcher, console.Department, connected, products, orders.ToArray()) { Feedback = console.LastNotice };
    }

    private void Refresh(EntityUid uid, FabricationOrderConsoleComponent console)
        => _ui.SetUiState(uid, FabricationOrderUiKey.Key, GetState(uid, console));
}
