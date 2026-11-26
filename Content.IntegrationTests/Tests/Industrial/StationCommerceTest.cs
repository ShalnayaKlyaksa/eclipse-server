using System;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Cargo.Systems;
using Content.Server.Lathe;
using Content.Server.Materials;
using Content.Server.PDA;
using Content.Server.Power.Components;
using Content.Server.Research.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Cargo.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Lathe;
using Content.Shared.PDA;
using Content.Shared.Research.Prototypes;
using Content.Shared.Station.Components;
using Content.Shared.VendingMachines;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class StationCommerceTest : GameTest
{
    private async Task Check(Action action)
    {
        try { await Server.WaitAssertion(action); }
        catch (Exception error) { TestContext.Progress.WriteLine(error); throw; }
    }
    [Test]
    public async Task SplitPaymentsConserveCreditsAndRefundCapacityCannotBeSpent()
    {
        var map = await Pair.CreateTestMap();
        await Check(() =>
        {
            var domain = SEntMan.SpawnEntity(null, map.GridCoords);
            var departments = SEntMan.EnsureComponent<StationBankAccountComponent>(domain);
            var research = SEntMan.System<ResearchSystem>();
            var bank = SEntMan.System<PdaPersonalServicesSystem>();
            var a = research.IssueCrewAccount(domain, SEntMan.SpawnEntity(null, map.GridCoords), "Bartender", "Seller", "seller");
            var b = research.IssueCrewAccount(domain, SEntMan.SpawnEntity(null, map.GridCoords), "Clown", "Buyer", "buyer");
            var cargo = SEntMan.System<CargoSystem>();
            var before = departments.Accounts.Values.Sum() + bank.Balance(domain, a.Username) + bank.Balance(domain, b.Username);
            Assert.That(bank.PayBusiness(domain, b.Username, 101, a.Username, "Service", 10, "Cargo", "Meal"), Is.True);
            Assert.That(bank.Balance(domain, b.Username), Is.EqualTo(899));
            Assert.That(bank.Balance(domain, a.Username), Is.EqualTo(1091));
            Assert.That(departments.Accounts["Cargo"], Is.EqualTo(2010));
            Assert.That(bank.PayBusiness(domain, b.Username, 100, null, "Service", 10, "Service", "Dept"), Is.True);
            Assert.That(departments.Accounts["Service"], Is.EqualTo(1100));
            Assert.That(departments.Accounts.Values.Sum() + bank.Balance(domain, a.Username) + bank.Balance(domain, b.Username), Is.EqualTo(before));
            Assert.That(bank.PayBusiness(domain, b.Username, 1, null, "Missing", 0, "Cargo", "Invalid"), Is.False);
            Assert.That(bank.PayBusiness(domain, b.Username, -5, a.Username, "Service", 0, "Cargo", "Invalid"), Is.False);
            Assert.That(bank.PayBusiness(domain, b.Username, 1, a.Username, "Service", 101, "Cargo", "Invalid"), Is.False);
            cargo.TrySetBankAccount((domain, departments), "Cargo", int.MaxValue);
            Assert.That(bank.PayBusiness(domain, b.Username, 100, a.Username, "Service", 10, "Cargo", "Overflow"), Is.False);
            Assert.That(bank.Balance(domain, b.Username), Is.EqualTo(799));
            Assert.That(bank.ReserveBusiness(domain, b.Username, 100), Is.True);
            var balance = bank.Balance(domain, b.Username);
            Assert.That(bank.AdjustCredits(domain, b.Username, int.MaxValue - balance - 100, "Ceiling"), Is.True);
            Assert.That(bank.AdjustCredits(domain, b.Username, 1, "Cannot consume refund capacity"), Is.False);
            bank.ReleaseBusiness(domain, b.Username, 100);
            Assert.That(bank.Balance(domain, b.Username), Is.EqualTo(int.MaxValue));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task RegisterChecksOwnerCardInvoiceVersionAndDeliversGoodsOnce()
    {
        var map = await Pair.CreateTestMap();
        await Check(() =>
        {
            var domain = SEntMan.SpawnEntity(null, map.GridCoords);
            SEntMan.EnsureComponent<StationBankAccountComponent>(domain);
            var research = SEntMan.System<ResearchSystem>();
            var bank = SEntMan.System<PdaPersonalServicesSystem>();
            var commerce = SEntMan.System<StationCommerceSystem>();
            var seller = SEntMan.SpawnEntity("PassengerPDA", map.GridCoords);
            var buyer = SEntMan.SpawnEntity("PassengerPDA", map.GridCoords);
            var a = research.IssueCrewAccount(domain, seller, "Bartender", "Seller", "seller");
            var b = research.IssueCrewAccount(domain, buyer, "Clown", "Buyer", "buyer");
            research.AssignMailbox(seller, domain, a.Username); research.AssignMailbox(buyer, domain, b.Username);
            var uid = SEntMan.SpawnEntity("ImperialPaymentTerminal", map.GridCoords);
            var terminal = SEntMan.GetComponent<PaymentTerminalComponent>(uid); terminal.Domain = domain;
            Assert.That(commerce.Claim(uid, terminal, seller), Is.True);
            Assert.That(commerce.Claim(uid, terminal, buyer), Is.False);
            var containers = SEntMan.System<SharedContainerSystem>();
            var slot = containers.EnsureContainer<ContainerSlot>(uid, "commerce-goods");
            var food = SEntMan.SpawnEntity("FoodSnackRaisins", map.GridCoords);
            Assert.That(containers.Insert(food, slot), Is.True);
            var message = new PaymentTerminalMessage(PaymentTerminalAction.Configure, terminal.Version)
            { Personal = true, Recipient = "seller", Price = 100, Commission = 5, Description = "Lunch" };
            Assert.That(commerce.Configure(uid, terminal, buyer, message), Is.False);
            Assert.That(commerce.Configure(uid, terminal, seller, message), Is.True);
            var oldVersion = terminal.Version;
            message.Version = terminal.Version; message.Price = 200;
            Assert.That(commerce.Configure(uid, terminal, seller, message), Is.True);
            Assert.That(commerce.PayInvoice(uid, terminal, buyer, oldVersion), Is.False);
            Assert.That(bank.Balance(domain, b.Username), Is.EqualTo(1000));
            var version = terminal.Version;
            Assert.That(commerce.PayInvoice(uid, terminal, buyer, version), Is.True);
            Assert.That(slot.ContainedEntities, Is.Empty);
            Assert.That(SEntMan.EntityExists(food), Is.True);
            Assert.That(commerce.PayInvoice(uid, terminal, buyer, version), Is.False);
            Assert.That(bank.Balance(domain, b.Username), Is.EqualTo(800));
            Assert.That(bank.Balance(domain, a.Username), Is.EqualTo(1190));
            var hack = new GotEmaggedEvent(buyer, EmagType.Interaction);
            SEntMan.EventBus.RaiseLocalEvent(uid, ref hack);
            Assert.That(hack.Handled, Is.True);
            Assert.That(terminal.OwnerUsername, Is.EqualTo(b.Username));
            Assert.That(commerce.CanManage(uid, terminal, seller), Is.False);
            Assert.That(commerce.CanManage(uid, terminal, buyer), Is.True);
            message.Version = terminal.Version; message.Recipient = "buyer"; message.Price = 5000;
            Assert.That(commerce.Configure(uid, terminal, buyer, message), Is.True);
            Assert.That(commerce.PayInvoice(uid, terminal, seller, terminal.Version), Is.False, "Extortion still requires buyer consent and sufficient funds");
            // Goods disappearing or being replaced invalidates the displayed invoice.
            Assert.That(containers.Insert(food, slot), Is.True);
            Assert.That(terminal.BillOpen, Is.False);
            message.Version = terminal.Version; message.Price = 25;
            Assert.That(commerce.Configure(uid, terminal, buyer, message), Is.True);
            var itemVersion = terminal.Version;
            Assert.That(containers.TryRemoveFromContainer(food), Is.True);
            Assert.That(commerce.PayInvoice(uid, terminal, seller, itemVersion), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task DocumentQueueChargesOnlyPrintedCopiesAndRefundsCancellationAndPowerAbort()
    {
        var map = await Pair.CreateTestMap();
        await Check(() =>
        {
            var domain = SEntMan.SpawnEntity(null, map.GridCoords);
            SEntMan.EnsureComponent<StationDataComponent>(domain);
            SEntMan.System<StationSystem>().AddGridToStation(domain, map.Grid.Owner);
            var departments = SEntMan.EnsureComponent<StationBankAccountComponent>(domain);
            var actor = SEntMan.SpawnEntity("PassengerPDA", map.GridCoords);
            var research = SEntMan.System<ResearchSystem>();
            var employee = research.IssueCrewAccount(domain, actor, "Scientist", "Anna", "anna");
            research.AssignMailbox(actor, domain, employee.Username);
            var bank = SEntMan.System<PdaPersonalServicesSystem>();
            var printer = SEntMan.SpawnEntity("PrinterDoc", map.GridCoords);
            var lathe = SEntMan.System<LatheSystem>();
            var comp = SEntMan.GetComponent<LatheComponent>(printer);
            var recipe = Server.ProtoMan.Index<LatheRecipePrototype>("PrintedDocumentReportStationRecipe");
            Assert.That(lathe.TryAddToQueue(printer, recipe, 2, comp, actor, 5), Is.False, "No paper must not debit the account");
            SEntMan.System<MaterialStorageSystem>().TryChangeMaterialAmount(printer, "PrinterPaper", 1000);
            Assert.That(lathe.TryAddToQueue(printer, recipe, 2, comp, actor, 1), Is.False, "Stale quote");
            Assert.That(lathe.TryAddToQueue(printer, recipe, 2, comp, actor, 5), Is.True);
            Assert.That(bank.Balance(domain, employee.Username), Is.EqualTo(990));
            SEntMan.GetComponent<ApcPowerReceiverComponent>(printer).Powered = true;
            Assert.That(lathe.TryStartProducing(printer, comp), Is.True);
            lathe.FinishProducing(printer, comp);
            Assert.That(departments.Accounts["Service"], Is.EqualTo(1005));
            // Second copy is now running; power loss returns its fee and materials.
            lathe.AbortProduction(printer, comp);
            Assert.That(bank.Balance(domain, employee.Username), Is.EqualTo(995));
            Assert.That(comp.Queue, Is.Empty);
            Assert.That(SEntMan.GetComponent<PdaPersonalServicesComponent>(domain).Accounts[employee.Username].ReservedCredits, Is.Zero);
            Assert.That(lathe.TryAddToQueue(printer, recipe, 3, comp, actor, 5), Is.True);
            var cancel = new LatheDeleteRequestMessage(0) { Actor = actor };
            lathe.OnLatheDeleteRequestMessage(printer, comp, ref cancel);
            Assert.That(bank.Balance(domain, employee.Username), Is.EqualTo(995));
            Assert.That(lathe.TryAddToQueue(printer, recipe, 2, comp, actor, 5), Is.True);
            SEntMan.DeleteEntity(printer);
            Assert.That(bank.Balance(domain, employee.Username), Is.EqualTo(995), "Deleting printer refunds outstanding copies");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task VendingReservesAfterStockChecksAndChargesOnlyOnDelivery()
    {
        var map = await Pair.CreateTestMap();
        await Check(() =>
        {
            var domain = SEntMan.SpawnEntity(null, map.GridCoords);
            SEntMan.EnsureComponent<StationDataComponent>(domain);
            SEntMan.System<StationSystem>().AddGridToStation(domain, map.Grid.Owner);
            var departments = SEntMan.EnsureComponent<StationBankAccountComponent>(domain);
            var actor = SEntMan.SpawnEntity("PassengerPDA", map.GridCoords);
            var research = SEntMan.System<ResearchSystem>();
            var employee = research.IssueCrewAccount(domain, actor, "Scientist", "Anna", "anna");
            research.AssignMailbox(actor, domain, employee.Username);
            var bank = SEntMan.System<PdaPersonalServicesSystem>();
            var uid = SEntMan.SpawnEntity("VendingMachineSnack", map.GridCoords);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(uid).Powered = true;
            var vendor = SEntMan.GetComponent<VendingMachineComponent>(uid);
            var system = SEntMan.System<Content.Server.VendingMachines.VendingMachineSystem>();
            var entry = vendor.Inventory.Values.First();
            entry.Amount = 1;
            system.TryEjectVendorItem(uid, InventoryType.Regular, entry.ID, false, actor);
            Assert.That(bank.Balance(domain, employee.Username), Is.EqualTo(935));
            Assert.That(departments.Accounts["Service"], Is.EqualTo(1000), "Funds held until delivery");
            system.TryEjectVendorItem(uid, InventoryType.Regular, entry.ID, false, actor);
            Assert.That(bank.Balance(domain, employee.Username), Is.EqualTo(935), "Double click cannot reserve twice");
            vendor.EjectEnd = SGameTiming.CurTime - TimeSpan.FromSeconds(1);
            system.Update(0);
            Assert.That(departments.Accounts["Service"], Is.EqualTo(1065));
            system.TryEjectVendorItem(uid, InventoryType.Regular, entry.ID, false, actor);
            Assert.That(bank.Balance(domain, employee.Username), Is.EqualTo(935), "Empty stock cannot debit");
            entry.Amount = 1;
            system.TryEjectVendorItem(uid, InventoryType.Regular, entry.ID, false, actor);
            Assert.That(bank.Balance(domain, employee.Username), Is.EqualTo(870));
            SEntMan.DeleteEntity(uid);
            Assert.That(bank.Balance(domain, employee.Username), Is.EqualTo(935), "Destroyed vendor refunds undelivered goods");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
