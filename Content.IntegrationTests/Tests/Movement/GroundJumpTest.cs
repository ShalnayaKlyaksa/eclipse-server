using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Input;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Robust.Client.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests.Movement;

public sealed class GroundJumpTest : MovementTest
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task SprintAndPrimaryUseCanBeHeldTogether(bool shiftFirst)
    {
        await Server.WaitPost(() => SCombatMode.SetInCombatMode(SPlayer, true));
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            var shift = new KeyEventArgs(Keyboard.Key.Shift, false, false, false, true, false, 0);
            var click = new KeyEventArgs(Keyboard.Key.MouseLeft, false, false, false, true, false, 0);

            var first = shiftFirst ? shift : click;
            var second = shiftFirst ? click : shift;
            try
            {
                InputManager.KeyDown(first);
                InputManager.KeyDown(second);
                Assert.That(InputSystem.CmdStates.GetState(EngineKeyFunctions.Use), Is.EqualTo(BoundKeyState.Down));
                Assert.That(InputSystem.CmdStates.GetState(EngineKeyFunctions.Walk), Is.EqualTo(BoundKeyState.Down));

                InputManager.KeyUp(shift);
                Assert.That(InputSystem.CmdStates.GetState(EngineKeyFunctions.Use), Is.EqualTo(BoundKeyState.Down));
            }
            finally
            {
                InputManager.KeyUp(click);
                if (InputManager.IsKeyDown(Keyboard.Key.Shift))
                    InputManager.KeyUp(shift);
            }

            Assert.That(InputSystem.CmdStates.GetState(EngineKeyFunctions.Use), Is.EqualTo(BoundKeyState.Up));
        });
    }

    [TestCase("Table", true)]
    [TestCase("WallSolid", false)]
    public async Task JumpClearsTableButStopsAtWall(string obstacle, bool crosses)
    {
        await SpawnTarget(obstacle);
        await Move(DirectionFlag.East, 0.8f);
        Assert.That(Delta(), Is.Positive);
        var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(SPlayer);
        var reserve = exertion.Reserve;

        await PressKey(ContentKeyFunctions.GroundJump);
        await RunSeconds(0.6f);

        Assert.That(Delta() < 0, Is.EqualTo(crosses));
        Assert.That(exertion.Reserve, Is.EqualTo(reserve - SharedAdvancedExertionSystem.JumpCost).Within(0.2f));
        Assert.That(exertion.JumpObstacle, Is.Null);
    }

    [TestCase("ClothingShoesBootsJump", "shoes")]
    [TestCase("LavalandEquipmentExplorerSuitReinforcedHEV2", "outerClothing")]
    public async Task EquipmentReplacesOrdinaryJumpAndKeepsItsCooldown(string prototype, string slot)
    {
        await Server.WaitAssertion(() =>
        {
            var equipment = SEntMan.SpawnEntity(prototype, ToServer(PlayerCoords));
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(SPlayer, equipment, slot, force: true), Is.True);
        });
        await RunTicks(5);
        var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(SPlayer);
        var reserve = exertion.Reserve;
        var body = SEntMan.GetComponent<PhysicsComponent>(SPlayer);

        await SetMovementKey(DirectionFlag.East, BoundKeyState.Down);
        await RunTicks(3);
        await SetMovementKey(DirectionFlag.East, BoundKeyState.Up);
        await PressKey(ContentKeyFunctions.GroundJump);
        Assert.That(body.BodyStatus, Is.EqualTo(BodyStatus.InAir));

        await RunSeconds(0.8f);
        await PressKey(ContentKeyFunctions.GroundJump);
        Assert.That(body.BodyStatus, Is.EqualTo(BodyStatus.OnGround));
        Assert.That(exertion.Reserve, Is.EqualTo(reserve).Within(0.2f));
    }
}
