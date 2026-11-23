using System.Numerics;
using Content.Client.Hands.Systems;
using Content.Shared._Eclipse.Industrial;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Maths;

namespace Content.Client._Eclipse.Industrial;

public sealed class IndustrialPortOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly IInputManager _input = default!;

    private TransformSystem _transform = default!;
    private HandsSystem _hands = default!;
    private IndustrialProcessorSystem _processor = default!;
    private readonly Vector2[] _arrowShaft = new Vector2[4];
    private readonly Vector2[] _arrowHead = new Vector2[3];

    private static readonly Color Backplate = Color.FromHex("#10161DEB");
    private static readonly Color Closed = Color.FromHex("#7F8B97");
    private static readonly Color Input = Color.FromHex("#8DBAA6");
    private static readonly Color Output = Color.FromHex("#D8B774");
    private static readonly Color Liquid = Color.FromHex("#7EADD7");
    private static readonly Color Heat = Color.FromHex("#DC9A78");

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public IndustrialPortOverlay() => IoCManager.InjectDependencies(this);

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } player)
            return;
        _transform ??= _entMan.System<TransformSystem>();
        _hands ??= _entMan.System<HandsSystem>();
        _processor ??= _entMan.System<IndustrialProcessorSystem>();
        if (_hands.GetActiveItem(player) is not { } held || !_processor.IsPortConfigurator(held))
            return;

        var handle = args.WorldHandle;
        var mouse = _eye.PixelToMap(_input.MouseScreenPosition);
        var query = _entMan.AllEntityQueryEnumerator<IndustrialProcessorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var processor, out var xform))
        {
            if (xform.MapID != args.MapId || !args.WorldBounds.Contains(_transform.GetWorldPosition(xform)))
                continue;
            var matrix = IndustrialPortClickZones.GetPortWorldMatrix(xform, _transform);
            handle.SetTransform(matrix);
            Direction? hover = null;
            if (mouse.MapId == args.MapId && Matrix3x2.Invert(matrix, out var inverse) &&
                IndustrialPortClickZones.TryGetLocalFace(Vector2.Transform(mouse.Position, inverse), out var hovered))
                hover = hovered;
            var faces = IndustrialPortClickZones.GetFaceZones(uid, _entMan);
            for (var i = 0; i < faces.Count; i++)
            {
                var (face, bounds) = faces[i];
                DrawPort(handle, face, bounds, processor.GetFacePort(face), hover == face);
            }
        }
        handle.SetTransform(Matrix3x2.Identity);
    }

    private void DrawPort(DrawingHandleWorld handle, Direction face, Box2 bounds, FacePortState state, bool hovered)
    {
        var color = state switch
        {
            FacePortState.ItemInput => Input,
            FacePortState.ItemOutput => Output,
            FacePortState.LiquidInput or FacePortState.LiquidOutput => Liquid,
            FacePortState.HeatInput => Heat,
            _ => Closed,
        };
        var center = IndustrialPortClickZones.GetFaceZoneCenter(face);
        handle.DrawRect(bounds, color.WithAlpha(hovered ? 0.25f : 0.07f));
        handle.DrawRect(bounds, hovered ? Color.White : color.WithAlpha(0.35f), filled: false);
        var badge = new Box2(center - new Vector2(0.115f), center + new Vector2(0.115f));
        handle.DrawRect(badge, Backplate);
        handle.DrawRect(badge, color.WithAlpha(0.9f), filled: false);
        if (state == FacePortState.Disabled)
        {
            const float radius = 0.045f;
            handle.DrawLine(center - new Vector2(radius), center + new Vector2(radius), color);
            handle.DrawLine(center + new Vector2(-radius, radius), center + new Vector2(radius, -radius), color);
            return;
        }
        var normal = face.ToVec();
        var tangent = new Vector2(-normal.Y, normal.X);
        if (state == FacePortState.HeatInput)
        {
            for (var i = -1; i <= 1; i++)
            {
                var bar = center + tangent * (i * 0.08f);
                handle.DrawLine(bar - normal * 0.065f, bar + normal * 0.065f, color);
            }
            return;
        }
        if (state is FacePortState.ItemInput or FacePortState.LiquidInput)
            normal = -normal;
        _arrowShaft[0] = center - normal * 0.075f - tangent * 0.022f;
        _arrowShaft[1] = center - normal * 0.075f + tangent * 0.022f;
        _arrowShaft[2] = center + tangent * 0.022f;
        _arrowShaft[3] = center - tangent * 0.022f;
        _arrowHead[0] = center + normal * 0.085f;
        _arrowHead[1] = center - normal * 0.005f + tangent * 0.065f;
        _arrowHead[2] = center - normal * 0.005f - tangent * 0.065f;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _arrowShaft, color);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _arrowHead, color);
    }
}
