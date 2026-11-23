using System;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.WallDisplays;
using Content.Server.PDA;
using Content.Server.Power.Components;
using Content.Server.Research.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Eclipse.WallDisplays;
using Content.Shared.Cargo.Components;
using Content.Shared.Construction.Prototypes;
using Content.Shared.PDA;
using Content.Shared.Station.Components;
using Content.Shared.VendingMachines;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class WallDisplaysTest : GameTest
{
    [TestCase("EclipseWallBoard1", 1)]
    [TestCase("EclipseWallBoard2", 2)]
    [TestCase("EclipseWallBoard3", 3)]
    [TestCase("EclipseWallTelevision", 2)]
    public async Task DisplaysOccupyTheirFullTileWidthInEveryOrientation(string prototype, int width)
    {
        var offset = width == 2 ? new Vector2(0.5f, 0) : Vector2.Zero;
        await Client.WaitAssertion(() =>
        {
            var proto = CProtoMan.Index<EntityPrototype>(prototype);
            Assert.That(proto.TryGetComponent<SpriteComponent>(out var sprite, CEntMan.ComponentFactory), Is.True);
            Assert.That(sprite.Offset, Is.EqualTo(offset), "Even widths must align with tile edges.");
        });

        var map = await Pair.CreateTestMap();
        await Check(() =>
        {
            var lookup = SEntMan.System<EntityLookupSystem>();
            var transform = SEntMan.System<SharedTransformSystem>();
            var uid = SEntMan.SpawnEntity(prototype, new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            for (var degrees = 0; degrees < 360; degrees += 90)
            {
                var angle = Angle.FromDegrees(degrees);
                transform.SetLocalRotation(uid, angle);
                var bounds = lookup.GetAABBNoContainer(uid, Vector2.Zero, angle);
                Assert.That(bounds.Width, Is.EqualTo(degrees % 180 == 0 ? width : 1).Within(0.03f));
                Assert.That(bounds.Height, Is.EqualTo(degrees % 180 == 0 ? 1 : width).Within(0.03f));

                var first = width == 3 ? -1 : 0;
                for (var tile = first - 1; tile <= first + width; tile++)
                {
                    var position = angle.RotateVec(new Vector2(tile, 0));
                    var indices = new Vector2i((int) MathF.Round(position.X), (int) MathF.Round(position.Y));
                    Assert.That(lookup.GetLocalEntitiesIntersecting(map.Grid, indices).Contains(uid),
                        Is.EqualTo(tile >= first && tile < first + width),
                        $"{prototype}, {degrees} degrees, tile {indices}");
                }
            }
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    private async Task Check(Action action)
    {
        try { await Server.WaitAssertion(action); }
        catch (Exception error) { TestContext.Progress.WriteLine(error); throw; }
    }

    [Test]
    public async Task BoardVariantsAreBuildableAndEditsRejectStaleOrOversizedContent()
    {
        var map = await Pair.CreateTestMap();
        await Check(() =>
        {
            var system = SEntMan.System<EditableWallBoardSystem>();
            for (var width = 1; width <= 3; width++)
            {
                var id = "EclipseWallBoard" + width;
                Assert.That(SProtoMan.HasIndex<ConstructionPrototype>(id), Is.True);
                var board = SEntMan.SpawnEntity(id, map.GridCoords);
                var component = SEntMan.GetComponent<EditableWallBoardComponent>(board);
                Assert.That(component.Width, Is.EqualTo(width));
                Assert.That(system.TrySetText(board, component, "МЕНЮ\r\nСуп — 40 кр.\tЧай — 25 кр.", 0), Is.True);
                Assert.That(component.Text, Is.EqualTo("МЕНЮ\nСуп — 40 кр. Чай — 25 кр."));
                Assert.That(system.TrySetText(board, component, "Stale overwrite", 0), Is.False);
                Assert.That(component.Text, Does.StartWith("МЕНЮ"));
                Assert.That(system.TrySetText(board, component, new string('x', component.CharacterLimit + 1), 1), Is.False);
                Assert.That(system.TrySetText(board, component, "Bad\0text", 1), Is.False);
                var message = new WallBoardSaveMessage("Новое объявление", 1) { UiKey = EditableWallBoardUiKey.Key, Actor = board };
                SEntMan.EventBus.RaiseLocalEvent(board, message);
                Assert.That(component.Text, Is.EqualTo("Новое объявление"));
                Assert.That(component.Revision, Is.EqualTo(2));
                Assert.That(system.TrySetText(board, component, "", 2), Is.True);
                Assert.That(component.Text, Is.Empty);
                Assert.That(system.TrySetText(board, component, "КРУПНЫЙ ТЕКСТ", 3, 24), Is.True);
                Assert.That(component.FontSize, Is.EqualTo(24));
                foreach (var invalidSize in new[] { 0, 5, 9, 25, int.MaxValue })
                    Assert.That(system.TrySetText(board, component, "Invalid font overwrite", 4, invalidSize), Is.False);
                Assert.That(system.TrySetText(board, component, "Stale font overwrite", 3, 6), Is.False);
                Assert.That(component.Text, Is.EqualTo("КРУПНЫЙ ТЕКСТ"));
                Assert.That(component.FontSize, Is.EqualTo(24));
                Assert.That(component.Revision, Is.EqualTo(4));
                Assert.That(system.TrySetText(board, component, "Мелкий текст", 4, 6), Is.True);
                Assert.That(component.FontSize, Is.EqualTo(6));
            }
            Assert.That(SProtoMan.HasIndex<ConstructionPrototype>("EclipseWallTelevision"), Is.True);
            var television = SEntMan.SpawnEntity("EclipseWallTelevision", map.GridCoords);
            Assert.That(SEntMan.GetComponent<ApcPowerReceiverComponent>(television).Load, Is.EqualTo(50));
            Assert.That(SEntMan.HasComponent<PowerSwitchComponent>(television), Is.True);
            SEntMan.SpawnEntity("CrateEclipseWallTelevision", map.GridCoords);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [TestCase("DonutInventory", 50)]
    [TestCase("ChangInventory", 65)]
    [TestCase("DiscountDansInventory", 65)]
    [TestCase("DrGibbInventory", 45)]
    [TestCase("PwrGameInventory", 45)]
    [TestCase("BodaInventory", 35)]
    public async Task FoodAndDrinkVendorsDisplayConfiguredPricesAndChargeOnDelivery(string pack, int price)
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
            var employee = research.IssueCrewAccount(domain, actor, "Bartender", "Vendor test", "vendor.test");
            research.AssignMailbox(actor, domain, employee.Username);
            var bank = SEntMan.System<PdaPersonalServicesSystem>();
            Assert.That(SProtoMan.Index<PersonalEconomyPrototype>("StationPersonalEconomy").VendorPrices[pack], Is.EqualTo(price));
            var uid = SEntMan.SpawnEntity("VendingMachineSnack", map.GridCoords);
            var machine = SEntMan.GetComponent<VendingMachineComponent>(uid);
            machine.PackPrototypeId = pack;
            SEntMan.GetComponent<ApcPowerReceiverComponent>(uid).Powered = true;
            var vending = SEntMan.System<Content.Server.VendingMachines.VendingMachineSystem>();
            vending.RestockInventoryFromPrototype(uid, machine);
            var item = machine.Inventory.Values.First(); item.Amount = 1;
            vending.TryEjectVendorItem(uid, InventoryType.Regular, item.ID, false, actor);
            Assert.That(bank.Balance(domain, employee.Username), Is.EqualTo(1000 - price));
            machine.EjectEnd = SGameTiming.CurTime - TimeSpan.FromSeconds(1);
            vending.Update(0);
            Assert.That(departments.Accounts["Service"], Is.EqualTo(1000 + price));
            Assert.That(item.Amount, Is.Zero);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
