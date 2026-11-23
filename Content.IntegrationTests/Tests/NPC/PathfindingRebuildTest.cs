using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.NPC;

public sealed class PathfindingRebuildTest : GameTest
{
    [Test]
    public async Task RebuildConnectsAllFourChunkGroups()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var maps = Server.System<SharedMapSystem>();
            var paths = Server.System<PathfindingSystem>();
            var graph = Server.EntMan.GetComponent<GridPathfindingComponent>(map.Grid);

            for (var pass = 0; pass < 2; pass++)
            {
                // Four neighboring chunks, including negative origins. Rebuilding an unchanged
                // floor must keep their boundary connections intact on subsequent updates too.
                for (var x = -1; x <= 0; x++)
                for (var y = -1; y <= 0; y++)
                {
                    var tile = new Vector2i(x, y);
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, tile, Tile.Empty);
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, tile, map.Tile.Tile);
                }

#pragma warning disable RA0002 // Bypass the rebuild cooldown for this synchronous regression.
                graph.NextUpdate = TimeSpan.Zero;
#pragma warning restore RA0002
                paths.Update(0f);
                Assert.That(graph.DirtyChunks, Is.Empty);

                var targets = new List<PathPoly>();
                for (var x = -1; x <= 0; x++)
                for (var y = -1; y <= 0; y++)
                {
                    var poly = paths.GetPoly(new EntityCoordinates(map.Grid, new Vector2(x + 0.5f, y + 0.5f)));
                    Assert.That(poly, Is.Not.Null);
                    Assert.That(poly.IsValid(), Is.True);
                    targets.Add(poly);
                }

                var visited = new HashSet<PathPoly>();
                var pending = new Queue<PathPoly>();
                pending.Enqueue(targets[0]);
                while (pending.TryDequeue(out var poly))
                {
                    if (!visited.Add(poly))
                        continue;
                    Assert.That(poly.IsValid(), Is.True);
                    foreach (var neighbor in poly.Neighbors)
                        pending.Enqueue(neighbor);
                }

                foreach (var target in targets)
                    Assert.That(visited, Does.Contain(target), "A neighboring parity group lost its graph connection.");
            }

            Server.EntMan.DeleteEntity(map.MapUid);
        });
    }
}
