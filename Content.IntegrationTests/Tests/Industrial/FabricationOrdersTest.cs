using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._Eclipse.Industrial.Orders;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Industrial.Orders;
using Content.Server.Power.Components;
using Content.Shared._Eclipse.Industrial.Orders;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared.Access.Components;
using Content.Shared.Station.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class FabricationOrdersTest : GameTest
{
    private EntityUid Actor(EntityCoordinates coords, string access)
    {
        var actor = SEntMan.SpawnEntity(null, coords);
        SEntMan.EnsureComponent<AccessComponent>(actor).Tags.Add(new(access));
        return actor;
    }
    private EntityUid Terminal(EntityCoordinates coords, string kind)
    {
        var terminal = SEntMan.SpawnEntity("ComputerFabrication" + kind, coords);
        SEntMan.GetComponent<ApcPowerReceiverComponent>(terminal).Powered = true;
        return terminal;
    }
    private async Task Check(Action action)
    {
        try { await Server.WaitAssertion(action); }
        catch (Exception error) { TestContext.Error.WriteLine(error); Assert.Fail(error.ToString()); }
    }

    [Test]
    public async Task DepartmentOrdersSurviveTerminalReplacementAndFollowAnAcknowledgedWorkflow()
    {
        var map = await Pair.CreateTestMap();
        await Check(() =>
        {
            var station = SEntMan.SpawnEntity(null, map.GridCoords);
            SEntMan.EnsureComponent<StationMemberComponent>(map.Grid.Owner).Station = station;
            var medical = Terminal(map.GridCoords, "Medical");
            var dispatch = Terminal(map.GridCoords, "Dispatcher");
            var doctor = Actor(map.GridCoords, "Medical"); var engineer = Actor(map.GridCoords, "Engineering");
            var system = SEntMan.System<FabricationOrderSystem>();
            Assert.That(system.Submit(medical, doctor, "OrderHandheldHealthAnalyzer", 2, "For the ward\nTwo treatment rooms", true), Is.True);
            var ledger = SEntMan.GetComponent<StationFabricationOrdersComponent>(station);
            var order = ledger.Orders.Single();
            Assert.That(order.Priority, Is.EqualTo(1), "Requested urgency must not grant department control over dispatch priority.");
            Assert.That(system.Act(dispatch, engineer, order.Id, 0, FabricationOrderAction.Complete, 2, ""), Is.False);
            Assert.That(system.Act(dispatch, engineer, order.Id, 0, FabricationOrderAction.WaitResearch, 2, "Need medical instrumentation"), Is.True);
            Assert.That(system.Act(dispatch, engineer, order.Id, 0, FabricationOrderAction.Accept, 2, "Stale operator"), Is.False, "Concurrent stale actions must not overwrite a newer response.");
            SEntMan.DeleteEntity(dispatch);
            dispatch = Terminal(map.GridCoords, "Dispatcher");
            Assert.That(system.GetState(dispatch, SEntMan.GetComponent<FabricationOrderConsoleComponent>(dispatch)).Orders.Single().Response,
                Is.EqualTo("Need medical instrumentation"));
            Assert.That(system.Act(dispatch, engineer, order.Id, order.Revision, FabricationOrderAction.Accept, 2, "On the bench"), Is.True);
            Assert.That(system.Act(dispatch, engineer, order.Id, order.Revision, FabricationOrderAction.Ready, 2, "Collect at engineering"), Is.True);
            Assert.That(system.Act(medical, doctor, order.Id, order.Revision, FabricationOrderAction.Complete, 2, ""), Is.True);
            Assert.That(order.Status, Is.EqualTo(FabricationOrderStatus.Completed));
            Assert.That(order.Response, Is.EqualTo("Collect at engineering"));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task StationIsolationDepartmentAccessLimitsAndUnpoweredConsolesAreEnforcedOnTheServer()
    {
        var first = await Pair.CreateTestMap(); var second = await Pair.CreateTestMap();
        await Check(() =>
        {
            var station = SEntMan.SpawnEntity(null, first.GridCoords);
            var otherStation = SEntMan.SpawnEntity(null, second.GridCoords);
            SEntMan.EnsureComponent<StationMemberComponent>(first.Grid.Owner).Station = station;
            SEntMan.EnsureComponent<StationMemberComponent>(second.Grid.Owner).Station = otherStation;
            var medical = Terminal(first.GridCoords, "Medical"); var duplicate = Terminal(first.GridCoords, "Medical");
            var security = Terminal(first.GridCoords, "Security"); var dispatch = Terminal(first.GridCoords, "Dispatcher");
            var foreignDispatch = Terminal(second.GridCoords, "Dispatcher");
            var doctor = Actor(first.GridCoords, "Medical"); var officer = Actor(first.GridCoords, "Security"); var engineer = Actor(first.GridCoords, "Engineering");
            var system = SEntMan.System<FabricationOrderSystem>();
            Assert.That(system.Submit(medical, officer, "OrderBeaker", 1, "Wrong card", false), Is.False);
            Assert.That(system.Submit(medical, doctor, "not-a-product", 1, "Invalid", false), Is.False);
            Assert.That(system.Submit(medical, doctor, "OrderBeaker", int.MaxValue, "Invalid", false), Is.False);
            Assert.That(system.Submit(medical, doctor, "OrderBeaker", 1, "[color=red]Literal user text[/color]", false), Is.True);
            var ledger = SEntMan.GetComponent<StationFabricationOrdersComponent>(station); var order = ledger.Orders.Single();
            Assert.That(system.Submit(duplicate, doctor, "OrderBeaker", 1, "Bypass delay", false), Is.False);
            Assert.That(system.Act(security, officer, order.Id, order.Revision, FabricationOrderAction.Cancel, 1, ""), Is.False);
            Assert.That(system.Act(security, officer, order.Id, order.Revision, FabricationOrderAction.Accept, 1, ""), Is.False);
            Assert.That(system.Act(foreignDispatch, engineer, order.Id, order.Revision, FabricationOrderAction.Accept, 1, ""), Is.False);
            Assert.That(system.GetState(foreignDispatch, SEntMan.GetComponent<FabricationOrderConsoleComponent>(foreignDispatch)).Orders, Is.Empty);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(dispatch).Powered = false;
            Assert.That(system.Act(dispatch, engineer, order.Id, order.Revision, FabricationOrderAction.Accept, 1, ""), Is.False);
            for (var i = 1; i < 12; i++)
            {
                ledger.NextRequest.Clear();
                Assert.That(system.Submit(medical, doctor, "OrderBeaker", 1, "Ward stock " + i, false), Is.True);
            }
            ledger.NextRequest.Clear();
            Assert.That(system.Submit(medical, doctor, "OrderBeaker", 1, "Over department limit", false), Is.False);
            Assert.That(ledger.Orders, Has.Count.EqualTo(12));
            SEntMan.DeleteEntity(first.MapUid); SEntMan.DeleteEntity(second.MapUid);
        });
    }

    [Test]
    public async Task EveryCatalogueEntryHasARealProductionRouteAndAnAvailableConsoleBoard()
    {
        await Check(() =>
        {
            var outputs = SProtoMan.EnumeratePrototypes<IndustrialRecipePrototype>().SelectMany(r => r.Outputs.Keys)
                .Concat(SProtoMan.EnumeratePrototypes<IndustrialFabricationRecipePrototype>().Select(r => r.Result.Id)).ToHashSet();
            var products = SProtoMan.EnumeratePrototypes<FabricationOrderProductPrototype>().ToArray();
            Assert.That(products.Length, Is.GreaterThanOrEqualTo(50));
            foreach (var product in products) Assert.That(outputs, Does.Contain(product.Product.Id), product.ID);
            foreach (var kind in new[] { "Dispatcher", "Medical", "Science", "Security", "Cargo", "Service", "Command" })
                Assert.That(SProtoMan.EnumeratePrototypes<IndustrialFabricationRecipePrototype>()
                    .Any(r => r.Result.Id == "Fabrication" + kind + "Circuitboard" && r.RequiredTechnology == null), Is.True,
                    "The ordering network must be buildable before its first R&D unlock.");
        });
    }

    [Test]
    public async Task BothConsoleModesKeepFormsAndQueueCardsInsideTheWindow()
    {
        try
        {
            await Client.WaitAssertion(() =>
            {
                foreach (var dispatcher in new[] { false, true })
                {
                    var window = new FabricationOrderWindow();
                    var products = new[] { new FabricationOrderProductInfo("test", "Beaker", "Большой лабораторный стакан", "medical", 20, false, "Точная электроника") };
                    var orders = new[] { new FabricationOrderInfo(1, 0, FabricationDepartment.Medical, "Beaker", "Многофункциональный анализатор аномальных полей с комплектом выносных датчиков", 10,
                        "Доктор [color=red]Иванов[/color]", "Для лаборатории и приёмного отделения", true, 2, FabricationOrderStatus.New,
                        "", "", 120, false, "Точная электроника") };
                    window.Update(new(dispatcher, FabricationDepartment.Medical, true, products, orders));
                    window.OpenCentered();
                    foreach (var size in new[] { new Vector2(960, 620), new Vector2(1140, 760) })
                    {
                        window.SetSize = size; window.Measure(size); window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                        var search = Find<LineEdit>(window, dispatcher ? "OrderSearch" : "ProductSearch");
                        Assert.That(search.GlobalRect.Right, Is.LessThanOrEqualTo(size.X + 1));
                        Assert.That(search.GlobalRect.Bottom, Is.LessThanOrEqualTo(size.Y + 1));
                        foreach (var bar in VisibleControls(window).OfType<VScrollBar>())
                            Assert.That(bar.Width, Is.GreaterThanOrEqualTo(6), "Overflow must have a visible, usable scroll thumb.");
                        if (dispatcher)
                        {
                            var action = Find<Button>(window, "PrimaryOrderAction");
                            var panel = Find<PanelContainer>(window, "OrderDetails");
                            Assert.That(action, Is.Not.Null);
                            Assert.That(action.GlobalRect.Bottom, Is.LessThanOrEqualTo(size.Y - 4), "The main order action must remain visible without scrolling.");
                            Assert.That(action.GlobalRect.Left, Is.GreaterThanOrEqualTo(panel.GlobalRect.Left));
                            Assert.That(action.GlobalRect.Right, Is.LessThanOrEqualTo(panel.GlobalRect.Right));
                            var card = Find<ContainerButton>(window, "Order-1");
                            var quantity = Find<Label>(card, "OrderQuantity-1");
                            Assert.That(quantity.Width, Is.GreaterThanOrEqualTo(35), "Long product names must preserve the quantity column.");
                            Assert.That(quantity.GlobalRect.Right, Is.LessThanOrEqualTo(card.GlobalRect.Right));
                            Assert.That(card.GlobalRect.Right, Is.LessThanOrEqualTo(panel.GlobalRect.Left), "Order cards must not run beneath the details panel.");
                        }
                        if (!dispatcher)
                        {
                            Find<TextEdit>(window, "OrderReason").InsertAtCursor("For the ward");
                            Find<LineEdit>(window, "OrderQuantity").SetText("21", true);
                            Assert.That(Find<Button>(window, "SubmitOrder").Disabled, Is.True);
                            Find<LineEdit>(window, "OrderQuantity").SetText("2", true);
                            var submit = Find<Button>(window, "SubmitOrder");
                            Assert.That(submit.Disabled, Is.False);
                            Assert.That(submit.GlobalRect.Bottom, Is.LessThanOrEqualTo(size.Y - 4), "Submit stays outside the scrolling request form.");
                        }
                    }
                    window.Close();
                }
            });
        }
        catch (Exception error) { TestContext.Error.WriteLine(error); Assert.Fail(error.ToString()); }
    }

    private static IEnumerable<Control> VisibleControls(Control control)
    {
        if (!control.Visible) yield break;
        yield return control;
        foreach (var child in control.Children)
            foreach (var descendant in VisibleControls(child)) yield return descendant;
    }

    private static T Find<T>(Control parent, string name) where T : Control
    {
        foreach (var child in parent.Children)
        {
            if (child.Name == name) return (T) child;
            var nested = Find<T>(child, name); if (nested != null) return nested;
        }
        return null!;
    }
}
