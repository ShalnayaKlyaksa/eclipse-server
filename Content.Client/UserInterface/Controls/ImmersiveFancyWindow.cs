using System.Numerics;
using Content.Shared.CCVar;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client.UserInterface.Controls;

/// <summary>Opt-in window mode shared by the research terminal and future redesigned interfaces.</summary>
[Virtual]
public class ImmersiveFancyWindow : FancyWindow
{
    private readonly IConfigurationManager _configuration = IoCManager.Resolve<IConfigurationManager>();
    private bool _immersive;
    private Vector2 _normalSize;
    private Vector2 _normalPosition;
    private Vector2 _normalMinimum;
    private bool _normalResizable;

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (Root == null || Size.X <= 0 || Size.Y <= 0) return;
        var enabled = _configuration.GetCVar(CCVars.ImmersiveInterfaces);
        if (enabled && !_immersive)
        {
            _normalSize = Size;
            _normalMinimum = MinSize;
            MinSize = Vector2.Zero;
            _normalPosition = Position;
            _normalResizable = Resizable;
            _immersive = true;
            Resizable = false;
        }
        else if (!enabled && _immersive)
        {
            _immersive = false;
            Resizable = _normalResizable;
            MinSize = _normalMinimum;
            SetSize = _normalSize;
            PlaceWindow(_normalPosition, _normalSize);
        }
        if (_immersive)
        {
            if (SetSize != Root.Size || Position != Vector2.Zero)
            {
                SetSize = Root.Size;
                PlaceWindow(Vector2.Zero, Root.Size);
            }
        }
    }

    private void PlaceWindow(Vector2 position, Vector2 size)
    {
        LayoutContainer.SetMarginLeft(this, position.X);
        LayoutContainer.SetMarginTop(this, position.Y);
        LayoutContainer.SetMarginRight(this, position.X + size.X);
        LayoutContainer.SetMarginBottom(this, position.Y + size.Y);
    }

    protected override DragMode GetDragModeFor(Vector2 relativeMousePos)
        => _immersive ? DragMode.None : base.GetDragModeFor(relativeMousePos);
}
