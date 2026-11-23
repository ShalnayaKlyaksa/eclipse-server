using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Eclipse.Industrial.Orders;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Eclipse.Industrial.Orders;

public sealed partial class FabricationOrderWindow : ImmersiveFancyWindow
{
    public Action<string, int, string, bool>? OnSubmit;
    public Action<int, int, FabricationOrderAction, int, string>? OnAction;
    private FabricationOrderState _state = new(false, FabricationDepartment.Medical, false, [], []);
    private readonly Label _heading = new() { ClipText = true, HorizontalExpand = true };
    private readonly Label _network = new();
    private readonly BoxContainer _summary = Row(20);
    private readonly Label _newCount = new();
    private readonly Label _progressCount = new();
    private readonly Label _readyCount = new();
    private readonly RichTextLabel _feedback = new() { HorizontalExpand = true };
    private readonly Button _requestTab;
    private readonly Button _boardTab;
    private readonly BoxContainer _tabs = Row();
    private readonly BoxContainer _compose = Column();
    private readonly BoxContainer _board = Column();
    private readonly BoxContainer _products = Column(0);
    private readonly BoxContainer _orders = Column(0);
    private readonly BoxContainer _details = Column(12);
    private readonly BoxContainer _actions = Column(8);
    private readonly ScrollContainer _productScroll = new() { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
    private readonly Label _catalogCount = new() { ClipText = true, HorizontalExpand = true };
    private readonly Label _reasonCount = new() { MinWidth = 72, HorizontalAlignment = HAlignment.Right };
    private readonly ScrollContainer _orderScroll = new() { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
    private readonly LineEdit _catalogSearch = new() { Name = "ProductSearch", HorizontalExpand = true };
    private readonly OptionButton _category = new() { MinWidth = 180 };
    private readonly LineEdit _orderSearch = new() { Name = "OrderSearch", HorizontalExpand = true };
    private readonly OptionButton _department = new() { MinWidth = 184 };
    private readonly OptionButton _status = new() { MinWidth = 176 };
    private readonly TextureRect _productIcon = new() { SetSize = new(48), TextureScale = new(1.5f), Stretch = TextureRect.StretchMode.KeepAspectCentered };
    private readonly RichTextLabel _productName = new() { HorizontalExpand = true };
    private readonly RichTextLabel _license = new() { HorizontalExpand = true };
    private readonly LineEdit _quantity = new() { Name = "OrderQuantity", Text = "1", SetWidth = 90 };
    private readonly TextEdit _reason = new() { Name = "OrderReason", HorizontalExpand = true, SetHeight = 100 };
    private string Reason => Rope.Collapse(_reason.TextRope);
    private readonly CheckBox _urgent = new();
    private Button _submit = default!;
    private readonly Label _limit = new() { ClipText = true, HorizontalExpand = true, VerticalAlignment = VAlignment.Center };
    private readonly SpriteSystem _sprites = IoCManager.Resolve<IEntityManager>().System<SpriteSystem>();
    private readonly IPrototypeManager _prototypes = IoCManager.Resolve<IPrototypeManager>();
    private string? _product;
    private int? _selected;
    private int _detailRevision = -1;
    private bool _detailResearched;
    private string[] _categories = [];
    private readonly Color _muted = Color.FromHex("#A6B1B6");
    private readonly Color _amber = Color.FromHex("#D4B47F");

    public FabricationOrderWindow()
    {
        Title = Loc.GetString("fabrication-orders-title");
        SetSize = new Vector2(1140, 760);
        MinSize = new Vector2(960, 620);
        ApplyTheme();
        var root = Column(12);
        root.VerticalExpand = _compose.VerticalExpand = _board.VerticalExpand = true;
        root.Margin = new Thickness(16);
        var backdrop = new PanelContainer { HorizontalExpand = true, VerticalExpand = true, PanelOverride = new StyleBoxFlat(Color.FromHex(PageHex)) };
        backdrop.AddChild(root); ContentsContainer.AddChild(backdrop);
        var header = Row(14);
        _heading.AddStyleClass("orders-heading");
        _catalogCount.AddStyleClass("orders-small"); _reasonCount.AddStyleClass("orders-small");
        header.AddChild(_heading);
        header.AddChild(_network);
        var headerPanel = Surface(header, PageHex);
        ((StyleBoxFlat) headerPanel.PanelOverride!).BorderThickness = new Thickness(0, 0, 0, 1);
        root.AddChild(headerPanel);
        AddCount("fabrication-orders-count-new", _newCount, "#8CA7B2");
        AddCount("fabrication-orders-count-progress", _progressCount, "#D4B47F");
        AddCount("fabrication-orders-count-ready", _readyCount, "#9DBCA7");
        root.AddChild(_summary);
        var tabs = _tabs;
        _requestTab = Button("fabrication-orders-compose", () => ShowBoard(false));
        _boardTab = Button("fabrication-orders-board", () => ShowBoard(true));
        _requestTab.ToggleMode = _boardTab.ToggleMode = true;
        tabs.AddChild(_requestTab); tabs.AddChild(_boardTab); root.AddChild(tabs);
        root.AddChild(_compose); root.AddChild(_board);
        BuildCompose(); BuildBoard();
        root.AddChild(_feedback);

    }

    private void BuildCompose()
    {
        var columns = Row(16); columns.VerticalExpand = true; _compose.AddChild(columns);
        var catalogue = Column(10); catalogue.VerticalExpand = true;
        catalogue.AddChild(Caption("fabrication-orders-products-heading"));
        _catalogSearch.PlaceHolder = Loc.GetString("fabrication-orders-product-search");
        catalogue.AddChild(_catalogSearch);
        var categories = Row(); categories.AddChild(_category); categories.AddChild(_catalogCount); catalogue.AddChild(categories);
        _productScroll.AddChild(_products); catalogue.AddChild(_productScroll); columns.AddChild(catalogue);
        var form = Column(8); form.VerticalExpand = true; form.MinWidth = 340; form.MaxWidth = 420;
        var fields = Column(8);
        var formScroll = new ScrollContainer { Name = "RequestFormScroll", HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        formScroll.AddChild(fields); form.AddChild(formScroll);
        fields.AddChild(Caption("fabrication-orders-request-heading"));
        var title = Row(12); title.AddChild(_productIcon); title.AddChild(_productName); fields.AddChild(title);
        _productName.AddStyleClass("orders-item-title");
        fields.AddChild(_license); fields.AddChild(Divider());
        fields.AddChild(Text(Loc.GetString("fabrication-orders-quantity"), true));
        var amount = Row();
        amount.AddChild(Button("fabrication-orders-minus", () => ChangeQuantity(-1)));
        amount.AddChild(_quantity); amount.AddChild(Button("fabrication-orders-plus", () => ChangeQuantity(1))); amount.AddChild(_limit); fields.AddChild(amount);
        _quantity.IsValid = value => value.Length <= 3 && (value.Length == 0 || int.TryParse(value, out var count) && count is >= 0 and <= 100);
        var purpose = Row(); purpose.AddChild(Text(Loc.GetString("fabrication-orders-reason"), true)); purpose.AddChild(_reasonCount); fields.AddChild(purpose);
        var reasonFrame = Surface(_reason, InputHex); reasonFrame.Name = "OrderReasonField";
        fields.AddChild(reasonFrame);
        _reason.ToolTip = Loc.GetString("fabrication-orders-reason-hint");
        _reason.Placeholder = new Rope.Leaf(Loc.GetString("fabrication-orders-reason-hint"));
        _urgent.Text = Loc.GetString("fabrication-orders-urgent"); _urgent.ToolTip = Loc.GetString("fabrication-orders-urgency-hint");
        form.AddChild(_urgent);
        _submit = Button("fabrication-orders-submit", () =>
        {
            if (_product != null && int.TryParse(_quantity.Text, out var quantity)) OnSubmit?.Invoke(_product, quantity, Reason, _urgent.Pressed);
        });
        _submit.Name = "SubmitOrder"; _submit.MinHeight = 38; _submit.AddStyleClass("orders-primary"); _submit.Label.FontColorOverride = Color.White; form.AddChild(_submit);
        var formPanel = Surface(form); formPanel.HorizontalExpand = false; formPanel.MinWidth = 364; formPanel.MaxWidth = 444;
        columns.AddChild(formPanel);
        _catalogSearch.OnTextChanged += _ => RebuildCatalog();
        _category.OnItemSelected += args => { _category.SelectId(args.Id); RebuildCatalog(); };
        _reason.OnTextChanged += _ => UpdateCompose(); _quantity.OnTextChanged += _ => UpdateCompose();
    }

    private void BuildBoard()
    {
        _orderSearch.PlaceHolder = Loc.GetString("fabrication-orders-order-search");
        _department.AddItem(Loc.GetString("fabrication-orders-all-departments"), 0);
        foreach (var department in Enum.GetValues<FabricationDepartment>()) _department.AddItem(Department(department), (int) department + 1);
        foreach (var value in new[] { "active", "new", "progress", "waiting", "ready", "archive" }) _status.AddItem(Loc.GetString("fabrication-orders-filter-" + value));
        var filters = Row(); filters.AddChild(_orderSearch); filters.AddChild(_department); filters.AddChild(_status); _board.AddChild(filters);
        var columns = Row(16); columns.VerticalExpand = true;
        var queue = Column(0); queue.VerticalExpand = true;
        _orderScroll.AddChild(_orders); queue.AddChild(_orderScroll); columns.AddChild(queue);
        var detailColumn = Column(10); detailColumn.VerticalExpand = true;
        var detailScroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
        detailScroll.AddChild(_details); detailColumn.AddChild(detailScroll); detailColumn.AddChild(_actions);
        var detailPanel = Surface(detailColumn); detailPanel.Name = "OrderDetails";
        detailPanel.HorizontalExpand = false; detailPanel.MinWidth = 336; detailPanel.MaxWidth = 372;
        _actions.Name = "OrderActions";
        columns.AddChild(detailPanel); _board.AddChild(columns);
        _orderSearch.OnTextChanged += _ => RebuildOrders();
        _department.OnItemSelected += args => { _department.SelectId(args.Id); RebuildOrders(); };
        _status.OnItemSelected += args => { _status.SelectId(args.Id); RebuildOrders(); };
    }

    public void Update(FabricationOrderState state)
    {
        var first = _state.Products.Length == 0;
        _state = state;
        _feedback.Visible = state.Feedback.Length > 0;
        _feedback.SetMessage(FormattedMessage.FromUnformatted(state.Feedback), defaultColor: _amber);
        _heading.Text = state.Dispatcher ? Loc.GetString("fabrication-orders-dispatcher") : Loc.GetString("fabrication-orders-department-terminal", ("department", Department(state.Department)));
        _network.Text = Loc.GetString(state.Connected ? "fabrication-orders-online" : "fabrication-orders-offline");
        _network.FontColorOverride = state.Connected ? Color.FromHex("#9DBCA7") : _amber;
        _newCount.Text = state.Orders.Count(o => o.Status == FabricationOrderStatus.New).ToString();
        _progressCount.Text = state.Orders.Count(o => o.Status == FabricationOrderStatus.InProgress).ToString();
        _readyCount.Text = state.Orders.Count(o => o.Status == FabricationOrderStatus.Ready).ToString();
        _requestTab.Visible = !state.Dispatcher; _tabs.Visible = !state.Dispatcher;
        if (first) ShowBoard(state.Dispatcher);
        var categories = state.Products.Select(p => p.Category).Distinct().OrderBy(c => c).ToArray();
        if (!_categories.SequenceEqual(categories))
        {
            _categories = categories; _category.Clear();
            _category.AddItem(Loc.GetString("fabrication-orders-all-products"), 0);
            for (var i = 0; i < categories.Length; i++) _category.AddItem(Loc.GetString("fabrication-orders-category-" + categories[i]), i + 1);
        }
        if (first)
        {
            var category = state.Department == FabricationDepartment.Medical ? "medical" : state.Department is FabricationDepartment.Science or FabricationDepartment.Security ? "equipment" : "tools";
            var index = Array.IndexOf(_categories, category); if (index >= 0) _category.SelectId(index + 1);
        }
        RebuildCatalog(); RebuildOrders(); UpdateCompose();
    }

    private void ShowBoard(bool board)
    {
        _board.Visible = board; _compose.Visible = !board;
        _requestTab.Pressed = !board; _boardTab.Pressed = board;
    }

    private void RebuildCatalog()
    {
        var scroll = _productScroll.VScrollTarget;
        _products.RemoveAllChildren(); _products.AddChild(CatalogHeader());
        var products = _state.Products.Where(p => (_category.SelectedId <= 0 || p.Category == _categories[_category.SelectedId - 1]) &&
            p.Name.Contains(_catalogSearch.Text.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(p => p.Category).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        if (!products.Any(p => p.Id == _product)) _product = products.FirstOrDefault()?.Id;
        _catalogCount.Text = Loc.GetString("fabrication-orders-product-count", ("count", products.Length));
        foreach (var product in products)
        {
            var button = new ContainerButton { HorizontalExpand = true, ToggleMode = true, Pressed = _product == product.Id };
            button.AddStyleClass("orders-product");
            var row = new Control { HorizontalExpand = true, MinHeight = 38 };
            var item = Row(10); item.Margin = new Thickness(0, 0, 110, 0);
            item.AddChild(Icon(product.Product, 32));
            var caption = Column(2); caption.AddChild(Text(product.Name));
            if (_category.SelectedId == 0) caption.AddChild(Text(Loc.GetString("fabrication-orders-category-" + product.Category), true));
            item.AddChild(caption); row.AddChild(item);
            var documentation = Cell(Loc.GetString(product.Researched ? "fabrication-orders-documentation-ready" : "fabrication-orders-documentation-needed"), 100);
            documentation.HorizontalAlignment = HAlignment.Right;
            documentation.FontColorOverride = product.Researched ? _muted : _amber;
            row.AddChild(documentation); button.AddChild(row);
            button.OnPressed += _ => { _product = product.Id; UpdateCompose(); RebuildCatalog(); };
            _products.AddChild(button);
        }
        if (products.Length == 0) _products.AddChild(EmptyState("fabrication-orders-empty-search", "fabrication-orders-no-products"));
        _productScroll.VScrollTarget = scroll;
        UpdateCompose();
    }

    private void UpdateCompose()
    {
        var product = _state.Products.FirstOrDefault(p => p.Id == _product);
        _quantity.Editable = product != null;
        _urgent.Disabled = product == null;
        if (product == null)
        {
            _submit.Disabled = true; _productIcon.Texture = null; _license.SetMessage(""); _limit.Text = "";
            _productName.SetMessage(Loc.GetString("fabrication-orders-select-product"), defaultColor: Ink); return;
        }
        _productName.SetMessage(FormattedMessage.FromUnformatted(product.Name), defaultColor: Ink);
        _productIcon.Texture = _sprites.Frame0(_prototypes.Index<EntityPrototype>(product.Product));
        _license.SetMessage(FormattedMessage.FromUnformatted(product.Researched ? Loc.GetString("fabrication-orders-license-known") :
            Loc.GetString("fabrication-orders-license-detail", ("technology", product.Technology ?? "—"))), defaultColor: product.Researched ? _muted : _amber);
        _reasonCount.Text = $"{Reason.Length} / 240";
        _limit.Text = Loc.GetString("fabrication-orders-limit", ("count", product.Maximum));
        _submit.Disabled = !_state.Connected || string.IsNullOrWhiteSpace(Reason) || Reason.Length > 240 || !int.TryParse(_quantity.Text, out var amount) || amount < 1 || amount > product.Maximum;
    }

    private void ChangeQuantity(int delta)
    {
        var maximum = _state.Products.FirstOrDefault(p => p.Id == _product)?.Maximum ?? 50;
        _quantity.Text = Math.Clamp((int.TryParse(_quantity.Text, out var value) ? value : 1) + delta, 1, maximum).ToString();
    }

    private bool Visible(FabricationOrderInfo order)
    {
        if (_department.SelectedId > 0 && (int) order.Department != _department.SelectedId - 1) return false;
        var matches = $"{order.Id} {order.Name} {order.Requester} {order.Reason}".Contains(_orderSearch.Text.Trim(), StringComparison.CurrentCultureIgnoreCase);
        return matches && (_status.SelectedId switch
        {
            1 => order.Status == FabricationOrderStatus.New,
            2 => order.Status == FabricationOrderStatus.InProgress,
            3 => order.Status is FabricationOrderStatus.WaitingMaterials or FabricationOrderStatus.WaitingResearch,
            4 => order.Status == FabricationOrderStatus.Ready,
            5 => FabricationOrderInfo.Closed(order.Status),
            _ => !FabricationOrderInfo.Closed(order.Status),
        });
    }

    private void RebuildOrders()
    {
        var scroll = _orderScroll.VScrollTarget;
        _orders.RemoveAllChildren(); _orders.AddChild(OrderHeader());
        var orders = _state.Orders.Where(Visible).OrderBy(o => o.Status).ThenByDescending(o => o.Priority).ThenBy(o => o.Id).ToArray();
        if (!orders.Any(o => o.Id == _selected)) { _selected = orders.FirstOrDefault()?.Id; _detailRevision = -1; }
        FabricationOrderStatus? previous = null;
        foreach (var order in orders)
        {
            if (previous != order.Status)
            {
                var group = Text(Status(order.Status) + $" · {orders.Count(o => o.Status == order.Status)}", true);
                group.Margin = new Thickness(10, 12, 10, 6);
                _orders.AddChild(group);
                previous = order.Status;
            }
            var button = new ContainerButton { Name = "Order-" + order.Id, HorizontalExpand = true, ToggleMode = true, Pressed = _selected == order.Id };
            button.AddStyleClass("orders-product");
            button.ToolTip = order.Requester + " · " + Loc.GetString("fabrication-orders-priority-" + order.Priority) +
                (order.Urgent ? " · " + Loc.GetString("fabrication-orders-urgent-short") : "");
            var row = new Control { HorizontalExpand = true, MinHeight = 40 };
            var number = Cell($"{order.Id:000}", 44); number.AddStyleClass("orders-key");
            if (order.Priority == 2) number.FontColorOverride = _amber;
            number.HorizontalAlignment = HAlignment.Left; row.AddChild(number);
            var item = Row(8); item.VerticalAlignment = VAlignment.Center; item.Margin = new Thickness(52, 0, 128, 0);
            item.AddChild(Icon(order.Product, 32)); item.AddChild(Text(order.Name)); row.AddChild(item);
            var department = Cell(Loc.GetString("fabrication-orders-short-" + order.Department.ToString().ToLowerInvariant()), 76);
            department.HorizontalAlignment = HAlignment.Right; department.Margin = new Thickness(0, 0, 44, 0); row.AddChild(department);
            var quantity = Cell(order.Quantity.ToString(), 36, HAlignment.Right); quantity.Name = "OrderQuantity-" + order.Id;
            quantity.HorizontalAlignment = HAlignment.Right; row.AddChild(quantity);
            button.AddChild(row);
            button.OnPressed += _ => { _selected = order.Id; _detailRevision = -1; RebuildOrders(); };
            _orders.AddChild(button);
        }
        _orderScroll.VScrollTarget = scroll;
        if (orders.Length == 0) _orders.AddChild(EmptyState("fabrication-orders-empty-queue", _state.Orders.Length == 0 ? "fabrication-orders-queue-empty" : "fabrication-orders-no-orders"));
        var selected = _state.Orders.FirstOrDefault(o => o.Id == _selected);
        if (selected == null) { _details.RemoveAllChildren(); _actions.RemoveAllChildren(); _details.AddChild(EmptyState("fabrication-orders-no-selection", "fabrication-orders-select-order")); return; }
        if (_detailRevision == selected.Revision && _detailResearched == selected.Researched) return;
        _detailRevision = selected.Revision;
        _detailResearched = selected.Researched;
        ShowDetails(selected);
    }

    private void ShowDetails(FabricationOrderInfo order)
    {
        _details.RemoveAllChildren(); _actions.RemoveAllChildren();
        _details.AddChild(Text(Loc.GetString("fabrication-orders-order-number", ("id", order.Id)), true));
        var title = Row(12); title.AddChild(Icon(order.Product, 40));
        var name = Text($"{order.Name} ×{order.Quantity}"); name.AddStyleClass("orders-item-title"); title.AddChild(name); _details.AddChild(title);
        _details.AddChild(Text(Department(order.Department) + " · " + order.Requester, true));
        _details.AddChild(Text(Status(order.Status), true)); _details.AddChild(Divider());
        _details.AddChild(Text(Loc.GetString("fabrication-orders-reason"), true)); _details.AddChild(Text(order.Reason));
        if (order.Urgent) _details.AddChild(Text(Loc.GetString("fabrication-orders-urgency-requested"), true));
        if (!order.Researched) _details.AddChild(Text(Loc.GetString("fabrication-orders-license-detail", ("technology", order.Technology ?? "—")), true));
        if (order.Assignee.Length > 0) _details.AddChild(Text(Loc.GetString("fabrication-orders-assignee", ("name", order.Assignee)), true));
        if (order.Response.Length > 0) { _details.AddChild(Text(Loc.GetString("fabrication-orders-response"), true)); _details.AddChild(Text(order.Response)); }
        if (FabricationOrderInfo.Closed(order.Status)) return;
        _actions.AddChild(Divider());
        var priority = new OptionButton { SetWidth = 170, ClipText = true };
        var response = new LineEdit { Name = "DispatchResponse", HorizontalExpand = true, Text = order.Response, PlaceHolder = Loc.GetString("fabrication-orders-response-hint") };
        response.IsValid = value => value.Length <= 240;
        if (_state.Dispatcher)
        {
            var row = Row(); row.AddChild(Text(Loc.GetString("fabrication-orders-priority"), true)); row.AddChild(priority); _actions.AddChild(row);
            for (var i = 0; i < 3; i++) priority.AddItem(Loc.GetString("fabrication-orders-priority-" + i), i);
            priority.SelectId(order.Priority); priority.OnItemSelected += args => priority.SelectId(args.Id); StyleOption(priority);
            _actions.AddChild(response);
        }
        void Send(FabricationOrderAction action) => OnAction?.Invoke(order.Id, order.Revision, action,
            _state.Dispatcher ? priority.SelectedId : order.Priority, _state.Dispatcher ? response.Text : "");
        void Primary(FabricationOrderAction action, string caption)
        {
            var button = Button("fabrication-orders-action-" + caption, () => Send(action));
            button.Name = "PrimaryOrderAction"; button.AddStyleClass("orders-primary"); button.Label.FontColorOverride = Color.White; button.MinHeight = 36;
            button.Disabled = !_state.Connected; _actions.AddChild(button);
        }
        if (_state.Dispatcher)
        {
            if (order.Status is FabricationOrderStatus.New or FabricationOrderStatus.WaitingMaterials or FabricationOrderStatus.WaitingResearch) Primary(FabricationOrderAction.Accept, "accept");
            if (order.Status == FabricationOrderStatus.InProgress) Primary(FabricationOrderAction.Ready, "ready");
            if (order.Status == FabricationOrderStatus.Ready) Primary(FabricationOrderAction.Complete, "complete");
            else
            {
                var other = new OptionButton { HorizontalExpand = true, ClipText = true, ToolTip = Loc.GetString("fabrication-orders-reject-hint") };
                var actions = new[] { FabricationOrderAction.Update, FabricationOrderAction.WaitMaterials, FabricationOrderAction.WaitResearch, FabricationOrderAction.Reject };
                foreach (var caption in new[] { "update", "materials", "research", "reject" }) other.AddItem(Loc.GetString("fabrication-orders-action-" + caption));
                other.OnItemSelected += args => other.SelectId(args.Id); StyleOption(other);
                var apply = Button("fabrication-orders-apply", () => Send(actions[other.SelectedId]));
                void Validate() => apply.Disabled = !_state.Connected || other.SelectedId == 3 && string.IsNullOrWhiteSpace(response.Text);
                other.OnItemSelected += _ => Validate(); response.OnTextChanged += _ => Validate(); Validate();
                var row = Row(); row.AddChild(other); row.AddChild(apply); _actions.AddChild(row);
            }
        }
        else if (_state.Department == order.Department)
        {
            if (order.Status == FabricationOrderStatus.Ready) Primary(FabricationOrderAction.Complete, "complete");
            else Primary(FabricationOrderAction.Cancel, "cancel");
        }
    }

    private PanelContainer CatalogHeader()
    {
        var row = new Control { HorizontalExpand = true };
        var name = Text(Loc.GetString("fabrication-orders-column-product"), true); name.Margin = new Thickness(0, 0, 110, 0); row.AddChild(name);
        var documentation = Cell(Loc.GetString("fabrication-orders-column-documentation"), 100); documentation.HorizontalAlignment = HAlignment.Right;
        row.AddChild(documentation); return TableHeader(row);
    }

    private PanelContainer OrderHeader()
    {
        var row = new Control { HorizontalExpand = true };
        var number = Cell(Loc.GetString("fabrication-orders-column-number"), 44); number.HorizontalAlignment = HAlignment.Left; row.AddChild(number);
        var name = Text(Loc.GetString("fabrication-orders-column-product"), true); name.Margin = new Thickness(52, 0, 128, 0); row.AddChild(name);
        var department = Cell(Loc.GetString("fabrication-orders-column-department"), 76);
        department.HorizontalAlignment = HAlignment.Right; department.Margin = new Thickness(0, 0, 44, 0); row.AddChild(department);
        var quantity = Cell(Loc.GetString("fabrication-orders-column-quantity"), 36, HAlignment.Right); quantity.HorizontalAlignment = HAlignment.Right;
        row.AddChild(quantity); return TableHeader(row);
    }

    private static PanelContainer TableHeader(Control row)
    {
        var panel = new PanelContainer { PanelOverride = TableRow("#343F44"), HorizontalExpand = true };
        panel.AddChild(row); return panel;
    }

    private Label Cell(string text, int width, HAlignment alignment = HAlignment.Left)
    {
        var label = new Label { Text = text, SetWidth = width, ClipText = true, VerticalAlignment = VAlignment.Center,
            Align = alignment == HAlignment.Right ? Label.AlignMode.Right : Label.AlignMode.Left, FontColorOverride = _muted };
        label.AddStyleClass("orders-small"); return label;
    }

    private void AddCount(string caption, Label count, string color)
    {
        var row = Row(6); row.HorizontalExpand = false;
        row.AddChild(new PanelContainer { SetSize = new Vector2(6), VerticalAlignment = VAlignment.Center, PanelOverride = new StyleBoxFlat(Color.FromHex(color)) });
        var label = new Label { Text = Loc.GetString(caption), VerticalAlignment = VAlignment.Center }; label.AddStyleClass("orders-small");
        count.AddStyleClass("orders-item-title"); row.AddChild(label); row.AddChild(count); _summary.AddChild(row);
    }

    private Control EmptyState(string caption, string hint)
    {
        var column = Column(12); column.MaxWidth = 340; column.MinHeight = 180; column.Margin = new Thickness(20); column.HorizontalAlignment = HAlignment.Center;
        var icon = Icon("Paper", 40); icon.HorizontalAlignment = HAlignment.Center; column.AddChild(icon);
        var title = new Label { Text = Loc.GetString(caption), HorizontalAlignment = HAlignment.Center }; title.AddStyleClass("orders-item-title"); column.AddChild(title);
        column.AddChild(Text(Loc.GetString(hint), true)); return column;
    }

    private static PanelContainer Divider() => new() { SetHeight = 1, PanelOverride = new StyleBoxFlat(Color.FromHex(BorderHex)) };
    private RichTextLabel Caption(string key)
    {
        var caption = Text(Loc.GetString(key)); caption.AddStyleClass("orders-item-title"); return caption;
    }

    private TextureRect Icon(string prototype, int size) => new()
    {
        Texture = _sprites.Frame0(_prototypes.Index<EntityPrototype>(prototype)), SetSize = new Vector2(size), Stretch = TextureRect.StretchMode.KeepAspectCentered,
    };
    private RichTextLabel Text(string text, bool muted = false)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(FormattedMessage.FromUnformatted(text), defaultColor: muted ? _muted : Ink);
        if (muted) label.AddStyleClass("orders-small");
        return label;
    }
    private static string Department(FabricationDepartment department) => Loc.GetString("fabrication-orders-department-" + department.ToString().ToLowerInvariant());
    private static string Status(FabricationOrderStatus status) => Loc.GetString("fabrication-orders-status-" + status.ToString().ToLowerInvariant());
    private static BoxContainer Column(int separation = 8) => new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = separation, HorizontalExpand = true };
    private static BoxContainer Row(int separation = 8) => new() { SeparationOverride = separation, HorizontalExpand = true };
    private static PanelContainer Surface(Control content, string color = PanelHex)
    {
        var panel = new PanelContainer { HorizontalExpand = true, VerticalExpand = content.VerticalExpand, PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(color), BorderColor = Color.FromHex(BorderHex), BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 12, ContentMarginRightOverride = 12, ContentMarginTopOverride = 10, ContentMarginBottomOverride = 10,
        } };
        panel.AddChild(content); return panel;
    }
    private static Button Button(string key, Action pressed)
    {
        var button = new Button { Text = Loc.GetString(key) };
        button.Label.FontColorOverride = Ink;
        button.AddStyleClass("orders-control"); button.OnPressed += _ => pressed(); return button;
    }
    private void StyleOption(OptionButton option)
    {
        option.AddStyleClass("orders-control"); option.OptionStyleClasses.Add("orders-control");
        foreach (var row in option.Children.OfType<BoxContainer>()) row.SeparationOverride = 10;
        if (option.OptionsScroll.Parent is { } popup) popup.Stylesheet = ((Control) this).Stylesheet;
    }
}

