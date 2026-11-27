#nullable enable annotations

using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Client._Eclipse.Industrial;
using Content.Client.UserInterface.Controls;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class IndustrialFluidInteractionTest : GameTest
{
    [Test]
    public async Task EmptyContainerSelectsTankAndOnlyDrawsTheChosenReagentAtItsTransferLimit()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var machine = SEntMan.SpawnEntity("IndustrialChemicalReactor", map.GridCoords);
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var beaker = SEntMan.SpawnEntity("Beaker", map.GridCoords);
            Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(user, beaker));
            var solutions = Server.System<SharedSolutionContainerSystem>();
            solutions.TryGetSolution(machine, "industrial_fluid_input", out var input, out var inputContents);
            solutions.TryGetSolution(machine, "industrial_fluid_output", out var output, out var outputContents);
            solutions.TryGetRefillableSolution(beaker, out _, out var beakerContents);
            solutions.TryAddReagent(input.Value, "Water", FixedPoint2.New(20));
            solutions.TryAddReagent(input.Value, "Ethanol", FixedPoint2.New(10));
            solutions.TryAddReagent(output.Value, "IndustrialSolvent", FixedPoint2.New(15));
            SEntMan.GetComponent<SolutionTransferComponent>(beaker).TransferAmount = FixedPoint2.New(7);
            SEntMan.GetComponent<RefillableSolutionComponent>(beaker).MaxRefill = FixedPoint2.New(3);
            Assert.That(Server.System<SharedInteractionSystem>().InteractUsing(user, beaker, machine, map.GridCoords));
            var ui = Server.System<UserInterfaceSystem>();
            Assert.That(ui.TryGetUiState<IndustrialFluidSelectionState>(machine, IndustrialProcessorUiKey.Fluid, out var menu));
            Assert.That(menu!.Tanks, Has.Length.EqualTo(2));
            Assert.That(menu.Tanks.Single(t => t.Input).Contents, Has.Length.EqualTo(2));
            Assert.That(beakerContents!.Volume, Is.EqualTo(FixedPoint2.Zero), "Opening the menu must not transfer anything.");
            SEntMan.EventBus.RaiseLocalEvent(machine,
                new IndustrialProcessorDrainMessage(true, "Ethanol", SEntMan.GetNetEntity(beaker)) { Actor = user });
            Assert.That(beakerContents.GetTotalPrototypeQuantity("Ethanol"), Is.EqualTo(FixedPoint2.New(3)));
            Assert.That(beakerContents.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(inputContents!.GetTotalPrototypeQuantity("Ethanol"), Is.EqualTo(FixedPoint2.New(7)));
            Assert.That(inputContents.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(20)));
            Assert.That(outputContents!.Volume, Is.EqualTo(FixedPoint2.New(15)), "The other tank must be untouched.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task FilledContainerPoursIntoTheInputAndASingleReservoirFluidNeedsNoMenu()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var machine = SEntMan.SpawnEntity("IndustrialChemicalReactor", map.GridCoords);
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var beaker = SEntMan.SpawnEntity("Beaker", map.GridCoords);
            Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(user, beaker));
            var solutions = Server.System<SharedSolutionContainerSystem>();
            solutions.TryGetRefillableSolution(beaker, out var held, out var contents);
            solutions.TryAddReagent(held.Value, "Water", FixedPoint2.New(5));
            solutions.TryGetSolution(machine, "industrial_fluid_input", out _, out var input);
            solutions.TryGetSolution(machine, "industrial_fluid_output", out _, out var output);
            var interaction = Server.System<SharedInteractionSystem>();
            Assert.That(interaction.InteractUsing(user, beaker, machine, map.GridCoords));
            Assert.That(contents!.Volume, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(input!.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(output!.Volume, Is.EqualTo(FixedPoint2.Zero));
            SEntMan.DeleteEntity(machine);
            var reservoir = SEntMan.SpawnEntity("IndustrialFluidReservoir", map.GridCoords);
            solutions.TryGetSolution(reservoir, "industrial_fluid_input", out var tank, out var stored);
            solutions.TryAddReagent(tank.Value, "Water", FixedPoint2.New(20));
            Assert.That(interaction.InteractUsing(user, beaker, reservoir, map.GridCoords));
            Assert.That(contents.Volume, Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(stored!.Volume, Is.EqualTo(FixedPoint2.New(15)));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [TestCase("changed-hand")]
    [TestCase("filled-container")]
    [TestCase("out-of-range")]
    [TestCase("empty-source")]
    [TestCase("wrong-reagent")]
    public async Task StaleRadialChoicesCannotDrainTheWrongContainerOrInventReagents(string changed)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var machine = SEntMan.SpawnEntity("IndustrialChemicalReactor", map.GridCoords);
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var beaker = SEntMan.SpawnEntity("Beaker", map.GridCoords);
            var hands = Server.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(user, beaker));
            var solutions = Server.System<SharedSolutionContainerSystem>();
            solutions.TryGetSolution(machine, "industrial_fluid_input", out var tank, out var stored);
            solutions.TryAddReagent(tank.Value, "Water", FixedPoint2.New(20));
            solutions.TryGetRefillableSolution(beaker, out var held, out var contents);
            switch (changed)
            {
                case "changed-hand":
                    Assert.That(hands.TryDrop(user, beaker));
                    Assert.That(hands.TryPickupAnyHand(user, SEntMan.SpawnEntity("Beaker", map.GridCoords)));
                    break;
                case "filled-container":
                    solutions.TryAddReagent(held.Value, "Ethanol", FixedPoint2.New(1));
                    break;
                case "out-of-range":
                    Server.System<SharedTransformSystem>().SetCoordinates(user, new EntityCoordinates(map.Grid, 20.5f, .5f));
                    break;
                case "empty-source":
                    solutions.RemoveAllSolution(tank.Value);
                    break;
            }
            var before = stored!.Volume;
            SEntMan.EventBus.RaiseLocalEvent(machine, new IndustrialProcessorDrainMessage(true,
                changed == "wrong-reagent" ? "IndustrialSolvent" : "Water", SEntMan.GetNetEntity(beaker)) { Actor = user });
            Assert.That(stored.Volume, Is.EqualTo(before));
            Assert.That(contents!.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.Zero));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task RadialUsesTankThenReagentAndTheSharedLiquidMarker()
    {
        await Client.WaitAssertion(() =>
        {
            var water = new IndustrialFluidSlotState("Water", "Water", 10, Color.Blue);
            var ethanol = new IndustrialFluidSlotState("Ethanol", "Ethanol", 5, Color.Yellow);
            var input = new IndustrialFluidTankState(true, 250, [water, ethanol]);
            var output = new IndustrialFluidTankState(false, 250, [water]);
            (bool Input, string? Reagent)? selected = null;
            void Select(bool side, string? reagent) => selected = (side, reagent);
            var root = IndustrialFluidSelectionBoundUserInterface.BuildOptions(new IndustrialFluidSelectionState([input, output]), Select);
            Assert.That(root, Has.Length.EqualTo(2));
            Assert.That(root[0], Is.TypeOf<RadialMenuNestedLayerOption>());
            var fluids = ((RadialMenuNestedLayerOption)root[0]).Nested.ToArray();
            Assert.That(fluids, Has.Length.EqualTo(2));
            Assert.That(fluids[1].IconColor, Is.EqualTo(Color.Yellow));
            Assert.That(fluids[1].IconSpecifier, Is.TypeOf<RadialMenuTextureIconSpecifier>());
            ((RadialMenuActionOptionBase)fluids[1]).OnPressed();
            Assert.That(selected, Is.EqualTo((true, "Ethanol")));
            ((RadialMenuActionOptionBase)root[1]).OnPressed();
            Assert.That(selected, Is.EqualTo((false, "Water")));
            var reservoir = IndustrialFluidSelectionBoundUserInterface.BuildOptions(new IndustrialFluidSelectionState([input]), Select);
            Assert.That(reservoir.All(o => o is RadialMenuActionOptionBase), Is.True, "A single shared tank must skip tank selection.");
        });
    }
}
