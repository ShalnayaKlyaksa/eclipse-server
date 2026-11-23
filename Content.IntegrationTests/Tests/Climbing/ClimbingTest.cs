#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.IntegrationTests.Tests.Movement;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;
using ClimbingComponent = Content.Shared.Climbing.Components.ClimbingComponent;
using ClimbSystem = Content.Shared.Climbing.Systems.ClimbSystem;

namespace Content.IntegrationTests.Tests.Climbing;

[TestFixture("MobHuman")]
[TestFixture("MobShadowkin")]
public sealed class ClimbingTest(string playerPrototype) : MovementTest
{
    protected override string PlayerPrototype => playerPrototype;

    [Test]
    public async Task DevMapTablesHaveCollision()
    {
        var map = await Pair.LoadTestMap(new ResPath("/Maps/Test/dev_map.yml"));
        try
        {
            await Server.WaitAssertion(() =>
            {
                var tables = 0;
                var query = SEntMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent, PhysicsComponent>();
                while (query.MoveNext(out var uid, out var meta, out var xform, out var physics))
                {
                    if (xform.MapUid != map.MapUid || meta.EntityPrototype?.ID != "Table")
                        continue;

                    tables++;
                    Assert.That(physics.CanCollide, Is.True, $"Mapped table {uid} at {xform.LocalPosition} must block movement.");
                }

                Assert.That(tables, Is.GreaterThan(0));
            });
        }
        finally
        {
            await Server.WaitPost(() => SEntMan.DeleteEntity(map.MapUid));
        }
    }

    [Test]
    public async Task ClimbTableTest()
    {
        // Spawn a table to the right of the player.
        await SpawnTarget("Table");
        Assert.That(Delta(), Is.GreaterThan(0));

        // Player is not initially climbing anything.
        var comp = Comp<ClimbingComponent>(Player);
        Assert.Multiple(() =>
        {
            Assert.That(comp.IsClimbing, Is.False);
            Assert.That(comp.DisabledFixtureMasks, Has.Count.EqualTo(0));
        });

        // Attempt (and fail) to walk past the table.
        await Move(DirectionFlag.East, 1f);
        Assert.That(Delta(), Is.GreaterThan(0));

        // Try to start climbing
        var sys = SEntMan.System<ClimbSystem>();
        await Server.WaitPost(() => sys.TryClimb(SEntMan.GetEntity(Player), SEntMan.GetEntity(Player), SEntMan.GetEntity(Target.Value), out _));
        await AwaitDoAfters();

        // Player should now be climbing
        Assert.Multiple(() =>
        {
            Assert.That(comp.IsClimbing, Is.True);
            Assert.That(comp.DisabledFixtureMasks, Has.Count.GreaterThan(0));
        });

        // Can now walk over the table.
        await Move(DirectionFlag.East, 1f);

        Assert.Multiple(() =>
        {
            Assert.That(Delta(), Is.LessThan(0));

            // After walking away from the table, player should have stopped climbing.
            Assert.That(comp.IsClimbing, Is.False);
            Assert.That(comp.DisabledFixtureMasks, Has.Count.EqualTo(0));
        });

        // Try to walk back to the other side (and fail).
        await Move(DirectionFlag.West, 1f);
        Assert.That(Delta(), Is.LessThan(0));

        // Start climbing
        await Server.WaitPost(() => sys.TryClimb(SEntMan.GetEntity(Player), SEntMan.GetEntity(Player), SEntMan.GetEntity(Target.Value), out _));
        await AwaitDoAfters();

        Assert.Multiple(() =>
        {
            Assert.That(comp.IsClimbing, Is.True);
            Assert.That(comp.DisabledFixtureMasks, Has.Count.GreaterThan(0));
        });

        // Walk past table and stop climbing again.
        await Move(DirectionFlag.West, 1f);
        Assert.Multiple(() =>
        {
            Assert.That(Delta(), Is.GreaterThan(0));
            Assert.That(comp.IsClimbing, Is.False);
            Assert.That(comp.DisabledFixtureMasks, Has.Count.EqualTo(0));
        });
    }
}
