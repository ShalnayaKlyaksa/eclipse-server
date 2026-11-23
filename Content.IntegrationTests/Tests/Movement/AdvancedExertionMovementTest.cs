using System.Numerics;
using Content.Client._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Input;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Movement;

public sealed class AdvancedExertionMovementTest : MovementTest
{
    protected override int Tiles => 10;
    protected override bool AddWalls => false;

    [Test]
    public async Task WalkingRunningCalmAndExhaustedMovementRemainSynchronized()
    {
        await Server.WaitPost(() => Server.CfgMan.SetCVar(CVars.NetTickrate, 60));
        await RunTicks(10);
        // Exercise actual network input, physics and prediction while vitals are updating.
        for (var phase = 0; phase < 4; phase++)
        {
            // Reset unrelated physiological penalties; exhaustion is applied explicitly below.
            await Server.WaitPost(() => SEntMan.EventBus.RaiseLocalEvent(SPlayer, new RejuvenateEvent()));
            await RunTicks(10);
            var runRequested = phase % 2 == 1;
            var direction = runRequested ? DirectionFlag.West : DirectionFlag.East;
            if (phase == 2)
                await TapAlt();
            if (phase == 3)
            {
                await Server.WaitPost(() =>
                {
                    var exertion = SEntMan.GetComponent<AdvancedExertionComponent>(SPlayer);
                    exertion.Reserve = 0;
                    SEntMan.System<SharedAdvancedExertionSystem>().Advance((SPlayer, exertion), 0.001f, false, false);
                });
            }
            await SetKey(EngineKeyFunctions.Walk, runRequested ? BoundKeyState.Down : BoundKeyState.Up);
            await SetMovementKey(direction, BoundKeyState.Down);
            await RunTicks(10);
            var expectedSprint = runRequested && phase != 3;
            var speedFactor = phase == 3 ? 0.85f * 0.5f : phase == 2 ? 0.5f : runRequested ? 1.25f : 1f;
            await Server.WaitAssertion(() =>
            {
                Assert.That(SEntMan.GetComponent<InputMoverComponent>(SPlayer).Sprinting, Is.EqualTo(expectedSprint), $"Phase {phase}: wrong authoritative gait.");
                var speed = SEntMan.GetComponent<MovementSpeedModifierComponent>(SPlayer);
                Assert.That(expectedSprint ? speed.CurrentSprintSpeed : speed.CurrentWalkSpeed,
                    Is.EqualTo(speed.BaseSprintSpeed * speedFactor).Within(0.001f), $"Phase {phase}: wrong authoritative speed; sleep={SEntMan.GetComponent<AdvancedExertionComponent>(SPlayer).SleepDebt}.");
            });
            await Client.WaitAssertion(() =>
            {
                Assert.That(CEntMan.GetComponent<InputMoverComponent>(CPlayer).Sprinting, Is.EqualTo(expectedSprint), $"Phase {phase}: wrong predicted gait.");
                var speed = CEntMan.GetComponent<MovementSpeedModifierComponent>(CPlayer);
                Assert.That(expectedSprint ? speed.CurrentSprintSpeed : speed.CurrentWalkSpeed,
                    Is.EqualTo(speed.BaseSprintSpeed * speedFactor).Within(0.001f), $"Phase {phase}: wrong predicted speed.");
            });

            var previous = Vector2.Zero;
            await Client.WaitPost(() => previous = CEntMan.System<SharedTransformSystem>().GetWorldPosition(CPlayer));
            for (var sample = 0; sample < 20; sample++)
            {
                await RunTicks(4);
                var authoritative = Vector2.Zero;
                await Server.WaitPost(() => authoritative = Transform.GetWorldPosition(SPlayer));
                await Client.WaitAssertion(() =>
                {
                    var position = CEntMan.System<SharedTransformSystem>().GetWorldPosition(CPlayer);
                    Assert.That(Vector2.Distance(position, authoritative), Is.LessThan(0.75f), $"Phase {phase}: client diverged from server.");
                    Assert.That(Vector2.Distance(position, previous), Is.LessThan(0.65f), $"Phase {phase}: position jumped within four ticks.");
                    Assert.That((position.X - previous.X) * (runRequested ? -1 : 1), Is.GreaterThanOrEqualTo(-0.02f), "Continuous movement must not snap backwards.");
                    var input = CEntMan.System<SharedMoverController>().GetVelocityInput(CEntMan.GetComponent<InputMoverComponent>(CPlayer));
                    Assert.That(input.Walking.Length() + input.Sprinting.Length(), Is.LessThanOrEqualTo(1.001f), "A tick must not accumulate more than one tick of movement.");
                    previous = position;
                });
            }
            await SetMovementKey(direction, BoundKeyState.Up);
            await RunTicks(10);
        }
    }

    private async Task TapAlt()
    {
        await Client.WaitPost(() =>
        {
            var input = CEntMan.System<AdvancedExertionInputSystem>();
            input.Handle(ContentKeyFunctions.AdvancedCalmWalk, BoundKeyState.Down);
            input.Handle(ContentKeyFunctions.AdvancedCalmWalk, BoundKeyState.Up);
        });
    }

    [Test]
    public async Task AltSwitchesDuringContinuousMovementWithoutBackwardCorrections()
    {
        await Server.WaitPost(() => Server.CfgMan.SetCVar(CVars.NetTickrate, 60));
        await RunTicks(10);
        await SetMovementKey(DirectionFlag.East, BoundKeyState.Down);
        await RunTicks(10);
        var previous = Vector2.Zero;
        await Client.WaitPost(() => previous = CEntMan.System<SharedTransformSystem>().GetWorldPosition(CPlayer));
        for (var transition = 0; transition < 8; transition++)
        {
            await TapAlt();
            for (var sample = 0; sample < 8; sample++)
            {
                await RunTicks(1);
                await Client.WaitAssertion(() =>
                {
                    var position = CEntMan.System<SharedTransformSystem>().GetWorldPosition(CPlayer);
                    Assert.That(position.X - previous.X, Is.InRange(-0.005f, 0.15f),
                        $"Transition {transition}, tick {sample}: movement snapped during a gait change.");
                    previous = position;
                });
            }
            var expected = transition % 2 == 0;
            await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<AdvancedExertionComponent>(SPlayer).CalmWalking, Is.EqualTo(expected)));
            await Client.WaitAssertion(() => Assert.That(CEntMan.GetComponent<AdvancedExertionComponent>(CPlayer).CalmWalking, Is.EqualTo(expected)));
        }
        await SetMovementKey(DirectionFlag.East, BoundKeyState.Up);
    }
}
