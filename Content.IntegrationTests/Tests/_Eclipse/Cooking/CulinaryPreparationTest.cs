using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Cooking;
using Content.Server.Power.Components;
using Content.Shared._Eclipse.Cooking;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Eclipse.Cooking;

public sealed class CulinaryPreparationTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task SeparateAppliancesAndPhysicalVesselPreservePreparationAndChemistry()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var cooking = SEntMan.System<CulinarySystem>();
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var person = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var bowl = SEntMan.SpawnEntity("EclipseCulinaryMixingBowl", map.MapCoords);
            var cold = SEntMan.GetComponent<CulinaryStationComponent>(bowl);
            var oven = SEntMan.SpawnEntity("EclipseCulinaryOven", map.MapCoords);
            var hot = SEntMan.GetComponent<CulinaryStationComponent>(oven);
            Assert.That(cold.AllowedMethods, Is.EquivalentTo(new[] { CulinaryMethod.Soak, CulinaryMethod.Marinate }));
            Assert.That(hot.AllowedMethods, Is.EqualTo(new[] { CulinaryMethod.Bake }));
            foreach (var (id, method) in new[] { ("EclipseCulinaryGriddle", CulinaryMethod.Fry), ("EclipseCulinarySteamer", CulinaryMethod.Steam) })
                Assert.That(SEntMan.GetComponent<CulinaryStationComponent>(SEntMan.SpawnEntity(id, map.MapCoords)).AllowedMethods, Is.EqualTo(new[] { method }));
            var potato = SEntMan.SpawnEntity("FoodPotato", map.MapCoords);
            Assert.That(cooking.ProcessFood(potato, CulinaryMethod.Chop), Is.True);
            var insert = new InteractUsingEvent(person, potato, bowl, SEntMan.GetComponent<TransformComponent>(bowl).Coordinates);
            SEntMan.EventBus.RaiseLocalEvent(bowl, insert);
            Assert.That(insert.Handled, Is.True);
            solutions.TryGetSolution(bowl, "food", out var mixture);
            solutions.TryAddReagent(mixture.Value, "Water", FixedPoint2.New(20));
            solutions.TryAddReagent(mixture.Value, "Lexorin", FixedPoint2.New(1));
            cold.Method = CulinaryMethod.Bake;
            Assert.That(cooking.Start((bowl, cold)), Is.False, "A cold bowl cannot bake even if the method is forged.");
            cold.Method = CulinaryMethod.Soak;
            Assert.That(cooking.Start((bowl, cold)), Is.True, "Preparation bowls work without electricity.");
            cooking.Complete((bowl, cold));
            var volume = mixture.Value.Comp.Solution.Volume;
            var pot = SEntMan.SpawnEntity("EclipseCulinaryVessel", map.MapCoords);
            var vessel = SEntMan.GetComponent<CulinaryVesselComponent>(pot);
            Assert.That(cooking.TransferVessel((bowl, cold), pot, vessel), Is.True);
            solutions.TryGetSolution(pot, "food", out var carried);
            Assert.That(carried.Value.Comp.Solution.Volume, Is.EqualTo(volume));
            Assert.That(mixture.Value.Comp.Solution.Volume, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(cooking.TransferVessel((oven, hot), pot, vessel), Is.True);
            solutions.TryGetSolution(oven, "food", out var ovenFood);
            var ovenSolution = ovenFood.Value.Comp.Solution;
            Assert.That(ovenFood.Value.Comp.Solution.Volume, Is.EqualTo(volume));
            Assert.That(ovenSolution.GetTotalPrototypeQuantity("Lexorin"), Is.EqualTo(FixedPoint2.New(1)));
            var before = CulinarySystem.ReadData(ovenSolution);
            Assert.That(before.Methods & (1u << (int) CulinaryMethod.Chop), Is.Not.Zero);
            Assert.That(before.Methods & (1u << (int) CulinaryMethod.Soak), Is.Not.Zero);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(oven).Powered = false;
            Assert.That(cooking.Start((oven, hot)), Is.False);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(oven).Powered = true;
            Assert.That(cooking.Start((oven, hot)), Is.True);
            cooking.Complete((oven, hot));
            Assert.That(cooking.TransferVessel((oven, hot), pot, vessel), Is.True);
            var carriedSolution = carried.Value.Comp.Solution;
            var after = CulinarySystem.ReadData(carriedSolution);
            Assert.That(after.Cooked, Is.True);
            Assert.That(after.Methods & (1u << (int) CulinaryMethod.Bake), Is.Not.Zero);
            Assert.That(carried.Value.Comp.Solution.Volume, Is.EqualTo(volume));
            Assert.That(carriedSolution.GetTotalPrototypeQuantity("Lexorin"), Is.EqualTo(FixedPoint2.New(1)));
        });
    }

    [Test]
    public async Task BreadSlicesKeepPropertiesAfterARealKnifeInteraction()
    {
        var map = await Pair.CreateTestMap();
        EntityUid loaf = default;
        await Server.WaitAssertion(() =>
        {
            var person = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var knife = SEntMan.SpawnEntity("KitchenKnife", map.MapCoords);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(person, knife), Is.True);
            loaf = SEntMan.SpawnEntity("FoodBreadPlain", map.MapCoords);
            var use = new InteractUsingEvent(person, knife, loaf, SEntMan.GetComponent<TransformComponent>(loaf).Coordinates);
            SEntMan.EventBus.RaiseLocalEvent(loaf, use);
            Assert.That(use.Handled, Is.True);
            Assert.That(SEntMan.EntityExists(loaf), Is.True, "Slicing must finish through the normal timed action.");
        });
        await Pair.RunTicksSync(180);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(loaf), Is.False);
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var slices = SEntMan.EntityQuery<MetaDataComponent>().Where(m => m.EntityPrototype?.ID == "FoodBreadPlainSlice").ToArray();
            Assert.That(slices.Length, Is.EqualTo(5));
            var total = FixedPoint2.Zero;
            foreach (var slice in slices)
            {
                solutions.TryGetSolution(slice.Owner, "food", out var food);
                total += food.Value.Comp.Solution.Volume;
                var solution = food.Value.Comp.Solution;
                var data = CulinarySystem.ReadData(solution);
                Assert.That(data, Is.Not.Null);
                Assert.That(data.Methods & (1u << (int) CulinaryMethod.Chop), Is.Not.Zero);
            }
            Assert.That(total, Is.EqualTo(FixedPoint2.New(20)), "The loaf's nutrition must be split, not duplicated.");
        });
    }

    [Test]
    public async Task PestleAndSpoonPrepareFoodWithoutAnInterface()
    {
        var map = await Pair.CreateTestMap();
        EntityUid person = default, potato = default, pestle = default, bowl = default;
        FixedPoint2 rawProtein = default;
        await Server.WaitAssertion(() =>
        {
            person = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            potato = SEntMan.SpawnEntity("FoodPotato", map.MapCoords);
            pestle = SEntMan.SpawnEntity("EclipseCulinaryPestle", map.MapCoords);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(person, pestle), Is.True);
            var use = new InteractUsingEvent(person, pestle, potato, SEntMan.GetComponent<TransformComponent>(potato).Coordinates);
            SEntMan.EventBus.RaiseLocalEvent(potato, use);
            Assert.That(use.Handled, Is.True);
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            solutions.TryGetSolution(potato, "food", out var food);
            var foodSolution = food.Value.Comp.Solution;
            Assert.That(CulinarySystem.ReadData(foodSolution).Methods, Is.Zero, "Preparation cannot apply before the action finishes.");
        });
        await Pair.RunTicksSync(600);
        await Server.WaitAssertion(() =>
        {
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            solutions.TryGetSolution(potato, "food", out var prepared);
            var preparedSolution = prepared.Value.Comp.Solution;
            Assert.That(CulinarySystem.ReadData(preparedSolution).Methods & (1u << (int) CulinaryMethod.Grind), Is.Not.Zero);
            var hands = SEntMan.System<SharedHandsSystem>();
            Assert.That(hands.TryDrop(person, pestle), Is.True);
            var spoon = SEntMan.SpawnEntity("Spoon", map.MapCoords);
            Assert.That(hands.TryPickupAnyHand(person, spoon), Is.True);
            bowl = SEntMan.SpawnEntity("EclipseCulinaryMixingBowl", map.MapCoords);
            var meat = SEntMan.SpawnEntity("FoodMeat", map.MapCoords);
            solutions.TryGetSolution(meat, "food", out var raw);
            var rawSolution = raw.Value.Comp.Solution;
            rawProtein = rawSolution.GetTotalPrototypeQuantity("UncookedAnimalProteins");
            Assert.That(rawProtein, Is.GreaterThan(FixedPoint2.Zero));
            foreach (var item in new[] { potato, meat, spoon })
            {
                var use = new InteractUsingEvent(person, item, bowl, SEntMan.GetComponent<TransformComponent>(bowl).Coordinates);
                SEntMan.EventBus.RaiseLocalEvent(bowl, use);
                Assert.That(use.Handled, Is.True);
            }
        });
        await Pair.RunTicksSync(240);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.GetComponent<CulinaryStationComponent>(bowl).Ready, Is.True);
            Assert.That(SEntMan.GetComponent<CulinaryStationComponent>(bowl).Ingredients.ContainedEntities, Is.Empty);
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            solutions.TryGetSolution(bowl, "food", out var mixture);
            var mixtureSolution = mixture.Value.Comp.Solution;
            Assert.That(mixtureSolution.GetTotalPrototypeQuantity("UncookedAnimalProteins"), Is.EqualTo(rawProtein), "A spoon cannot replace heat treatment.");
        });
    }
}
