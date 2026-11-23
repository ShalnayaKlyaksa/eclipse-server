using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared.CCVar;
using Content.Shared.Movement.Components;
using Content.Shared.NPC;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.NPC;

public sealed class NPCSteeringUpdateTest : GameTest
{
    [Test]
    public async Task ReusedSnapshotTracksActiveSteerers()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var ents = Server.EntMan;
            var system = Server.System<NPCSteeringSystem>();
            var enabled = Server.CfgMan.GetCVar(CCVars.NPCEnabled);
            try
            {
                Server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
                var uid = ents.SpawnEntity(null, map.GridCoords);
                ents.AddComponent<ActiveNPCComponent>(uid);
                var mover = ents.AddComponent<InputMoverComponent>(uid);
                var steering = ents.AddComponent<NPCSteeringComponent>(uid);
                steering.Coordinates = new EntityCoordinates(uid, Vector2.Zero);
                steering.Status = SteeringStatus.NoPath;

                mover.CurTickSprintMovement = Vector2.UnitX;
                system.Update(1f / 30f);
                Assert.That(mover.CurTickSprintMovement, Is.EqualTo(Vector2.Zero));

                ents.RemoveComponent<ActiveNPCComponent>(uid);
                mover.CurTickSprintMovement = Vector2.UnitX;
                system.Update(1f / 30f);
                Assert.That(mover.CurTickSprintMovement, Is.EqualTo(Vector2.UnitX),
                    "The next update must not process entries retained from the previous snapshot.");

                ents.AddComponent<ActiveNPCComponent>(uid);
                system.Update(1f / 30f);
                Assert.That(mover.CurTickSprintMovement, Is.EqualTo(Vector2.Zero));
            }
            finally
            {
                Server.CfgMan.SetCVar(CCVars.NPCEnabled, enabled);
                ents.DeleteEntity(map.MapUid);
            }
        });
    }
}
