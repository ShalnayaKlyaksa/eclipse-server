using System.Numerics;
using Content.Shared.Atmos;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._Eclipse.Industrial;

/// <summary>Only machine endpoints allocate connector layers. Straight pipe sections keep their single body layer.</summary>
public sealed class IndustrialPipeConnectorSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprites = default!;

    public void Update(Entity<SpriteComponent?> ent, PipeDirection inputs, PipeDirection outputs, string rsi)
    {
        foreach (var (direction, offset, rotation) in Faces)
        {
            var visible = ((inputs | outputs) & direction) != 0;
            var socketName = "industrial-socket-" + direction;
            var arrowName = "industrial-arrow-" + direction;
            if (!visible && !_sprites.LayerMapTryGet(ent, socketName, out _, false))
                continue;
            var socket = _sprites.LayerMapReserve(ent, socketName);
            var arrow = _sprites.LayerMapReserve(ent, arrowName);
            _sprites.LayerSetVisible(ent, socket, visible);
            _sprites.LayerSetVisible(ent, arrow, visible);
            if (!visible)
                continue;
            _sprites.LayerSetRsi(ent, socket, new ResPath(rsi));
            _sprites.LayerSetRsiState(ent, socket, "connector");
            _sprites.LayerSetOffset(ent, socket, offset);
            _sprites.LayerSetRotation(ent, socket, rotation);
            _sprites.LayerSetRsi(ent, arrow, new ResPath(rsi));
            _sprites.LayerSetRsiState(ent, arrow, "flow_arrow");
            _sprites.LayerSetColor(ent, arrow, Color.FromHex("#E5BD52"));
            // Keep the whole chevron on the pipe side; a machine sprite can cover half the socket.
            _sprites.LayerSetOffset(ent, arrow, offset * .8125f);
            _sprites.LayerSetRotation(ent, arrow, (inputs & direction) != 0 ? rotation : rotation + Angle.FromDegrees(180));
        }
    }

    // The two source parts face north. Offsets put their centre on the tile boundary.
    private static readonly (PipeDirection Direction, Vector2 Offset, Angle Rotation)[] Faces =
    [
        (PipeDirection.North, new Vector2(0, .5f), Angle.Zero),
        (PipeDirection.South, new Vector2(0, -.5f), Angle.FromDegrees(180)),
        (PipeDirection.East, new Vector2(.5f, 0), Angle.FromDegrees(-90)),
        (PipeDirection.West, new Vector2(-.5f, 0), Angle.FromDegrees(90)),
    ];
}
