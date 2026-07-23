using System.Numerics;
using Content.Client.Hands.Systems;
using Content.Shared._Eclipse.Industrial;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Maths;

namespace Content.Client._Eclipse.Industrial;

public sealed class IndustrialPortOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    private TransformSystem _transform = default!;
    private HandsSystem _hands = default!;
    private IndustrialProcessorSystem _processor = default!;

    private static readonly Color ZoneFill = Color.White.WithAlpha(0.06f);
    private static readonly Color ZoneBorder = Color.White.WithAlpha(0.22f);

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public IndustrialPortOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } player)
            return;

        _transform ??= _entMan.System<TransformSystem>();
        _hands ??= _entMan.System<HandsSystem>();
        _processor ??= _entMan.System<IndustrialProcessorSystem>();

        var held = _hands.GetActiveItem(player);
        if (held == null || !_processor.IsPortConfigurator(held.Value))
            return;

        var handle = args.WorldHandle;
        var eyeRot = args.Viewport.Eye?.Rotation ?? Angle.Zero;
        var rotationMatrix = Matrix3Helpers.CreateRotation(-eyeRot);

        var query = _entMan.AllEntityQueryEnumerator<IndustrialProcessorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var worldPos = _transform.GetWorldPosition(xform);
            if (!args.WorldBounds.Contains(worldPos))
                continue;

            var worldMatrix = Matrix3Helpers.CreateTranslation(worldPos);
            var matty = Matrix3x2.Multiply(rotationMatrix, worldMatrix);
            handle.SetTransform(matty);

            foreach (var (_, localBounds) in IndustrialPortClickZones.GetFaceZones(uid, _entMan))
            {
                handle.DrawRect(localBounds, ZoneFill, filled: true);
                handle.DrawRect(localBounds, ZoneBorder, filled: false);
            }

            DrawFacePort(handle, Direction.North, comp.NorthFacePort);
            DrawFacePort(handle, Direction.South, comp.SouthFacePort);
            DrawFacePort(handle, Direction.East, comp.EastFacePort);
            DrawFacePort(handle, Direction.West, comp.WestFacePort);
        }
    }

    private static void DrawFacePort(DrawingHandleWorld handle, Direction direction, FacePortState state)
    {
        var offset = IndustrialPortClickZones.GetFaceZoneCenter(direction);

        switch (state)
        {
            case FacePortState.Disabled:
                DrawCross(handle, offset, Color.Gray.WithAlpha(0.45f));
                break;
            case FacePortState.ItemInput:
                DrawArrow(handle, offset, direction, inward: true, Color.Orange.WithAlpha(0.7f));
                break;
            case FacePortState.ItemOutput:
                DrawArrow(handle, offset, direction, inward: false, Color.Orange.WithAlpha(0.7f));
                break;
            case FacePortState.LiquidInput:
                DrawArrow(handle, offset, direction, inward: true, Color.CornflowerBlue.WithAlpha(0.7f));
                break;
            case FacePortState.LiquidOutput:
                DrawArrow(handle, offset, direction, inward: false, Color.CornflowerBlue.WithAlpha(0.7f));
                break;
            case FacePortState.HeatInput:
                DrawArrow(handle, offset, direction, inward: true, Color.OrangeRed.WithAlpha(0.75f));
                break;
        }
    }

    private static void DrawCross(DrawingHandleWorld handle, Vector2 center, Color color)
    {
        const float size = 0.08f;
        handle.DrawLine(center + new Vector2(-size, -size), center + new Vector2(size, size), color);
        handle.DrawLine(center + new Vector2(-size, size), center + new Vector2(size, -size), color);
    }

    private static void DrawArrow(DrawingHandleWorld handle, Vector2 center, Direction direction, bool inward, Color color)
    {
        var dirVec = direction.ToVec();
        if (inward)
            dirVec = -dirVec;

        var tip = center + dirVec * 0.12f;
        var baseCenter = center - dirVec * 0.03f;
        var side = new Vector2(-dirVec.Y, dirVec.X) * 0.055f;

        handle.DrawLine(baseCenter - side, tip, color);
        handle.DrawLine(baseCenter + side, tip, color);
        handle.DrawLine(baseCenter - side, baseCenter + side, color);
    }
}
