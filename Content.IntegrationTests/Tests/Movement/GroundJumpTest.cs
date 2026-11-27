using System.Numerics;
using Content.Client.Movement.Systems;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Input;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Robust.Client.Input;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests.Movement;

public sealed class GroundJumpTest : MovementTest
{
    protected override int Tiles => 5;

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
    [TestCase("Rack", true)]
    [TestCase("WallSolid", false)]
    public async Task JumpClearsTableButStopsAtWall(string obstacle, bool crosses)
    {
        await SpawnTarget(obstacle);
        await Move(DirectionFlag.East, 0.8f);
        Assert.That(Delta(), Is.Positive);
        var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(SPlayer);
        var reserve = exertion.Reserve;

        await PressSpace();
        await RunSeconds(0.6f);

        Assert.That(Delta() < 0, Is.EqualTo(crosses));
        Assert.That(exertion.Reserve, Is.EqualTo(reserve - SharedAdvancedExertionSystem.JumpCost).Within(0.2f));
        Assert.That(SEntMan.HasComponent<JumpFlightComponent>(SPlayer), Is.False);
    }

    [Test]
    public async Task JumpClearsTableWithWallInsideNormalJumpRangeAndLiftsSprite()
    {
        await SpawnTarget("Table");
        var wall = await SpawnEntity("WallSolid", ToServer(TargetCoords).Offset(new Vector2(2, 0)));
        await Move(DirectionFlag.East, 0.8f);
        var groundOffset = Vector2.Zero;
        var groundColor = Color.White;
        await Client.WaitPost(() =>
        {
            var sprite = CEntMan.GetComponent<SpriteComponent>(CPlayer);
            groundOffset = sprite.Offset;
            groundColor = sprite.Color;
        });

        await PressSpace();
        await RunTicks(3);
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.HasComponent<JumpFlightComponent>(CPlayer), Is.True);
            CEntMan.System<JumpFlightVisualsSystem>().FrameUpdate(0);
            var sprite = CEntMan.GetComponent<SpriteComponent>(CPlayer);
            Assert.That(sprite.Offset.Y, Is.GreaterThan(groundOffset.Y + 0.1f));
            Assert.That(sprite.Color.R, Is.LessThan(groundColor.R));
            Assert.That(sprite.Color.B, Is.EqualTo(groundColor.B));
            Assert.That(sprite.Color.A, Is.EqualTo(groundColor.A));
        });
        await RunSeconds(0.6f);

        Assert.That(Delta(), Is.Negative);
        Assert.That(Delta(SEntMan.GetNetEntity(wall)), Is.Positive);
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.HasComponent<JumpFlightComponent>(CPlayer), Is.False);
            Assert.That(CEntMan.GetComponent<SpriteComponent>(CPlayer).Color, Is.EqualTo(groundColor));
            // The gravity visualizer can also animate the sprite independently of the jump.
            Assert.That(Vector2.Distance(CEntMan.GetComponent<SpriteComponent>(CPlayer).Offset, groundOffset), Is.LessThan(0.15f));
        });
    }

    [Test]
    public async Task JumpCanClearSuccessiveLowObstacles()
    {
        await SpawnTarget("Rack");
        var secondRack = await SpawnEntity("Rack", ToServer(TargetCoords).Offset(Vector2.UnitX));
        await Move(DirectionFlag.East, 0.8f);

        await PressSpace();
        await RunSeconds(0.6f);

        Assert.That(Delta(SEntMan.GetNetEntity(secondRack)), Is.Negative);
        Assert.That(SEntMan.HasComponent<JumpFlightComponent>(SPlayer), Is.False);
    }

    [TestCase(19f, false)]
    [TestCase(35f, true)]
    public async Task SpaceUsesJumpReserveWhenSprintingIsUnavailable(float reserve, bool jumps)
    {
        await Server.WaitPost(() =>
        {
            var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(SPlayer);
            exertion.SleepDebt = 90f;
            exertion.Reserve = reserve;
            SEntMan.Dirty(SPlayer, exertion);
        });
        await RunTicks(5);

        await PressSpace();

        var body = SEntMan.GetComponent<PhysicsComponent>(SPlayer);
        Assert.That(body.BodyStatus, Is.EqualTo(jumps ? BodyStatus.InAir : BodyStatus.OnGround));
        var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(SPlayer);
        Assert.That(exertion.Reserve, Is.EqualTo(reserve - (jumps ? SharedAdvancedExertionSystem.JumpCost : 0)).Within(0.4f));
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
        await PressSpace();
        Assert.That(body.BodyStatus, Is.EqualTo(BodyStatus.InAir));

        await RunSeconds(0.8f);
        await PressSpace();
        Assert.That(body.BodyStatus, Is.EqualTo(BodyStatus.OnGround));
        Assert.That(exertion.Reserve, Is.EqualTo(reserve).Within(0.2f));
    }

    private async Task PressSpace()
    {
        var space = new KeyEventArgs(Keyboard.Key.Space, false, false, false, false, false, 0);
        try
        {
            await Client.WaitAssertion(() =>
            {
                InputManager.KeyDown(space);
                Assert.That(InputSystem.CmdStates.GetState(ContentKeyFunctions.GroundJump), Is.EqualTo(BoundKeyState.Down));
            });
            await RunTicks(1);
        }
        finally
        {
            await Client.WaitPost(() => InputManager.KeyUp(space));
        }

        await RunTicks(1);
    }
}
