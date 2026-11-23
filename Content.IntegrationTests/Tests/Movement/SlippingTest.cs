#nullable enable
using Content.IntegrationTests.Tests.Helpers;
using Content.Client._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Input;
using Content.Shared.Movement.Components;
using Content.Shared.Slippery;
using Content.Shared.Stunnable;
using Robust.Shared.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Movement;

public sealed class SlippingTest : MovementTest
{
    public sealed class SlipTestSystem : TestListenerSystem<SlipEvent>;

    [Test]
    public async Task BananaSlipTest()
    {
        await SpawnTarget("TrashBananaPeel");

        var modifier = Comp<MovementSpeedModifierComponent>(Player).SprintSpeedModifier;
        Assert.That(modifier, Is.EqualTo(1.25f), "Player is not moving at full speed.");

        // Player is to the left of the banana peel.
        Assert.That(Delta(), Is.GreaterThan(0.5f));

        // Calm walking stays below the slip threshold; ordinary walking uses the old running speed.
        await Client.WaitPost(() =>
        {
            var input = CEntMan.System<AdvancedExertionInputSystem>();
            input.Handle(ContentKeyFunctions.AdvancedCalmWalk, BoundKeyState.Down);
            input.Handle(ContentKeyFunctions.AdvancedCalmWalk, BoundKeyState.Up);
        });
        await SetKey(EngineKeyFunctions.Walk, BoundKeyState.Up);
        await AssertFiresEvent<SlipEvent>(async () => await Move(DirectionFlag.East, 1f), count: 0);

        Assert.That(Delta(), Is.LessThan(0.5f));
        AssertComp<KnockedDownComponent>(false, Player);

        // Sprinting does trigger a slip.
        await SetKey(EngineKeyFunctions.Walk, BoundKeyState.Down);
        await AssertFiresEvent<SlipEvent>(async () => await Move(DirectionFlag.West, 1f));

        // And the person that slipped was the player
        AssertEvent<SlipEvent>(predicate: @event => @event.Slipped == SPlayer);
        AssertComp<KnockedDownComponent>(true, Player);
    }
}

