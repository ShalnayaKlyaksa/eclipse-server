using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Erida.Research.UI;

/// <summary>
/// One reusable render target for the terminal's contents. Only this window is post-processed;
/// hit testing remains in normal UI coordinates. Released when the window leaves the tree.
/// </summary>
public sealed class ResearchTerminalSurface : Control
{
    public ProtoId<ShaderPrototype> EffectShader { get; set; } = "EclipseResearchCRT";
    private readonly IClyde _clyde = IoCManager.Resolve<IClyde>();
    private ShaderInstance? _shader;
    public bool EffectsEnabled = true;
    private IRenderTexture? _target;
    private float _elapsed;

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _elapsed = (_elapsed + MathF.Min(args.DeltaSeconds, .1f)) % 3600;
    }

    protected override void RenderChildOverride(ref ControlRenderArguments args, int childIndex, Vector2i position)
    {
        if (!EffectsEnabled)
        {
            ReleaseTargets();
            base.RenderChildOverride(ref args, childIndex, position);
            return;
        }
        if (PixelWidth <= 0 || PixelHeight <= 0) return;
        _shader ??= IoCManager.Resolve<IPrototypeManager>().Index(EffectShader).InstanceUnique();
        if (_target == null || _target.Size != PixelSize)
        {
            _target?.Dispose();
            _target = _clyde.CreateRenderTarget(PixelSize,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb), name: "Research terminal");
        }
        var child = GetChild(childIndex);
        var render = args.Handle;
        var screen = render.DrawingHandleScreen;
        var transform = screen.GetTransform();
        render.RenderInRenderTarget(_target, () =>
        {
            render.SetScissor(null);
            screen.SetTransform(Matrix3x2.Identity);
            UserInterfaceManager.RenderControl(render, child, Vector2i.Zero);
        }, Color.Black);
        screen.SetTransform(Matrix3x2.CreateTranslation(position));
        _shader.SetParameter("screen_size", (Vector2) PixelSize);
        _shader.SetParameter("elapsed", _elapsed);
        screen.UseShader(_shader);
        screen.DrawTextureRect(_target.Texture, new UIBox2(Vector2.Zero, PixelSize));
        screen.UseShader(null);
        screen.SetTransform(transform);
    }

    private void ReleaseTargets()
    {
        _target?.Dispose();
        _target = null;
        _shader?.Dispose();
        _shader = null;
    }

    protected override void ExitedTree()
    {
        ReleaseTargets();
        base.ExitedTree();
    }
}
