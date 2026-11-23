using System.Linq;
using Content.Client._Eclipse.Cooking;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Cooking;
using Content.Server.Power.Components;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.Cooking;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Nutrition;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Eclipse.Cooking;

public sealed class CulinaryIntegrationTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task PrepareCookServeAndDigestPreservesChemistryAndPortionProperties()
    {
        var map = await Pair.CreateTestMap();
        NetEntity servedNet = default;
        await Server.WaitAssertion(() =>
        {
            var cooking = SEntMan.System<CulinarySystem>();
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var eater = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            Server.PlayerMan.SetAttachedEntity(Server.PlayerMan.Sessions.Single(), eater);
            var station = SEntMan.SpawnEntity("EclipseCulinaryStation", map.MapCoords);
            var component = SEntMan.GetComponent<CulinaryStationComponent>(station);
            Assert.That(solutions.TryGetSolution(station, "food", out var batch), Is.True);
            var batchSolution = batch.Value.Comp.Solution;

            foreach (var profile in SProtoMan.EnumeratePrototypes<CulinaryIngredientPrototype>())
            {
                Assert.That(profile.Traits.Values.All(value => value >= 0 && float.IsFinite(value)), Is.True, profile.ID);
                foreach (var id in profile.Foods.Where(id => id.Id.StartsWith("EclipseFood")))
                {
                    var ingredient = SEntMan.SpawnEntity(id, map.MapCoords);
                    Assert.That(solutions.TryGetSolution(ingredient, "food", out var source), Is.True);
                    var before = source.Value.Comp.Solution;
                    var volume = before.Volume;
                    var nutrition = before.GetTotalPrototypeQuantity("Nutriment");
                    cooking.Stamp(ingredient, source.Value);
                    Assert.That(before.Volume, Is.EqualTo(volume));
                    Assert.That(before.GetTotalPrototypeQuantity("Nutriment"), Is.EqualTo(nutrition));
                    Assert.That(CulinarySystem.ReadData(before), Is.Not.Null, id.Id);
                    cooking.Stamp(ingredient, source.Value);
                    Assert.That(before.Volume, Is.EqualTo(volume), "Stamping twice cannot duplicate the payload.");
                    SEntMan.QueueDeleteEntity(ingredient);
                }
            }

            var meat = SEntMan.SpawnEntity("FoodMeat", map.MapCoords);
            var pepper = SEntMan.SpawnEntity("FoodChiliPepper", map.MapCoords);
            foreach (var ingredient in new[] { meat, pepper })
            {
                var interact = new InteractUsingEvent(eater, ingredient, station, SEntMan.GetComponent<TransformComponent>(station).Coordinates);
                SEntMan.EventBus.RaiseLocalEvent(station, interact);
                Assert.That(interact.Handled, Is.True);
            }
            Assert.That(component.Ingredients.ContainedEntities.Count, Is.EqualTo(2));
            var beforeChop = component.Ingredients.ContainedEntities.Sum(uid =>
            {
                solutions.TryGetSolution(uid, "food", out var sol);
                var contents = sol.Value.Comp.Solution;
                return contents.Volume.Float();
            });

            component.Method = CulinaryMethod.Chop;
            Assert.That(cooking.Start((station, component)), Is.False, "The range cannot replace a knife.");
            foreach (var ingredient in component.Ingredients.ContainedEntities)
            {
                Assert.That(cooking.ProcessFood(ingredient, CulinaryMethod.Chop), Is.True);
                Assert.That(cooking.ProcessFood(ingredient, CulinaryMethod.Chop), Is.False, "Repeated preparation cannot multiply properties.");
            }
            Assert.That(component.Ingredients.ContainedEntities.Sum(uid =>
            { solutions.TryGetSolution(uid, "food", out var food); var contents = food.Value.Comp.Solution; return contents.Volume.Float(); }), Is.EqualTo(beforeChop));

            var beaker = SEntMan.SpawnEntity("LargeBeaker", map.MapCoords);
            var drain = SEntMan.GetComponent<DrainableSolutionComponent>(beaker);
            Assert.That(solutions.TryGetSolution(beaker, drain.Solution, out var waterSource), Is.True);
            solutions.TryAddReagent(waterSource.Value, "Water", FixedPoint2.New(20));
            for (var i = 0; i < 2; i++)
            {
                var pour = new InteractUsingEvent(eater, beaker, station, SEntMan.GetComponent<TransformComponent>(station).Coordinates);
                SEntMan.EventBus.RaiseLocalEvent(station, pour);
            }
            Assert.That(batchSolution.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(20)));
            solutions.TryAddReagent(batch.Value, "Lexorin", FixedPoint2.New(1));
            component.Method = CulinaryMethod.Boil;
            SEntMan.GetComponent<ApcPowerReceiverComponent>(station).Powered = false;
            Assert.That(cooking.Start((station, component)), Is.False, "Heating requires power.");
            SEntMan.GetComponent<ApcPowerReceiverComponent>(station).Powered = true;
            Assert.That(cooking.Start((station, component)), Is.True);
            cooking.Complete((station, component));
            Assert.That(batchSolution.GetTotalPrototypeQuantity("Lexorin"), Is.EqualTo(FixedPoint2.New(1)), "Cooking cannot erase poison.");
            Assert.That(batchSolution.GetTotalPrototypeQuantity("UncookedAnimalProteins"), Is.EqualTo(FixedPoint2.Zero));
            var originalVolume = batchSolution.Volume;
            var originalToxin = batchSolution.GetTotalPrototypeQuantity("Lexorin");
            Assert.That(cooking.Serve((station, component), eater), Is.Null, "Serving must require an empty bowl.");
            Assert.That(batchSolution.Volume, Is.EqualTo(originalVolume));
            var bowl = SEntMan.SpawnEntity("FoodBowlBig", map.MapCoords);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(eater, bowl), Is.True);
            var portion = cooking.Serve((station, component), eater);
            Assert.That(portion, Is.Not.Null);
            Assert.That(solutions.TryGetSolution(portion.Value, "food", out var served), Is.True);
            var servedSolution = served.Value.Comp.Solution;
            Assert.That(servedSolution.Volume + batchSolution.Volume, Is.EqualTo(originalVolume));
            Assert.That(servedSolution.GetTotalPrototypeQuantity("Lexorin") + batchSolution.GetTotalPrototypeQuantity("Lexorin"), Is.EqualTo(originalToxin),
                $"Served: {servedSolution}; remaining: {batchSolution}");
            Assert.That(CulinarySystem.ReadData(servedSolution).Cooked, Is.True);
            servedNet = SEntMan.GetNetEntity(portion.Value);

            var eating = new IngestingEvent(portion.Value, servedSolution.Clone(), false);
            SEntMan.EventBus.RaiseLocalEvent(eater, ref eating);
            var nutritionComponent = SEntMan.GetComponent<CulinaryNutritionComponent>(eater);
            Assert.That(nutritionComponent.Pending.Sum(), Is.GreaterThan(0));
            var nutritionSystem = SEntMan.System<CulinaryNutritionSystem>();
            nutritionSystem.Advance((eater, nutritionComponent), 1);
            Assert.That(nutritionComponent.Recovery, Is.Zero, "Food must have an absorption delay.");
            for (var i = 0; i < 10; i++) nutritionSystem.Advance((eater, nutritionComponent), 1);
            Assert.That(nutritionComponent.Recovery, Is.GreaterThan(0));
            for (var i = 0; i < 100; i++) SEntMan.EventBus.RaiseLocalEvent(eater, ref eating);
            Assert.That(nutritionComponent.Doses.Zip(nutritionComponent.Pending).All(pair => pair.First + pair.Second <= 30.001f));

            var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(eater);
            var exertionSystem = SEntMan.System<SharedAdvancedExertionSystem>();
            nutritionComponent.Efficiency = 1;
            nutritionComponent.Heaviness = 0;
            exertion.Reserve = 100;
            exertionSystem.Advance((eater, exertion), 1, true, false);
            var efficientCost = 100 - exertion.Reserve;
            nutritionComponent.Efficiency = 0;
            nutritionComponent.Heaviness = 1;
            exertion.Reserve = 100;
            exertionSystem.Advance((eater, exertion), 1, true, false);
            Assert.That(100 - exertion.Reserve, Is.GreaterThan(efficientCost));
        });
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var portion = CEntMan.GetEntity(servedNet);
            var solutions = CEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(portion, "food", out var food), Is.True);
            Assert.That(CulinarySystem.ReadData(food.Value.Comp.Solution), Is.Not.Null, "Culinary data must survive server/client serialization.");
            using var window = new CulinaryWindow();
            window.Apply(new CulinaryState(new[] { "test" }, "test properties", 20, CulinaryMethod.Boil, 1, false, true, true, "culinary-status-ready"));
        });
    }
}
