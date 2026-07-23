using System;
using System.Numerics;
using Content.Client.MainMenu.UI;
using Content.Client.Message;
using Content.Client.UserInterface.Systems.Alerts.Widgets;
using Content.Shared._Eclipse.Tutorial;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;
using static Robust.Client.UserInterface.Controls.LayoutContainer;

namespace Content.Client._Eclipse.Tutorial;

/// <summary>
/// Full-screen tutorial overlay: a visual-novel dialogue mode (large side portrait + bottom name/text
/// plate, click anywhere to advance) and a task mode (top objective banner + a spotlight that darkens the
/// screen except a highlighted HUD area, with clicks passing through to it).
/// </summary>
public sealed class TutorialDialogueControl : LayoutContainer
{
    private const int PortraitSize = 300;
    private const float BottomBarHeight = 150f;
    private const float BottomBarInset = 60f;
    private const float BottomBarBottom = 34f;

    private static readonly Color Square = Color.FromHex("#2EC4B6");
    private static readonly Color Circle = Color.FromHex("#E6A11A");

    private readonly Action _onAdvance;

    private readonly SpotlightLayer _spotlight;
    private readonly PanelContainer _dim;
    private readonly Control _portraitSlot;
    private readonly PanelContainer _bottomBar;
    private readonly BoxContainer _bottomContent;
    private readonly PanelContainer _taskBanner;
    private readonly RichTextLabel _taskText;

    private Control? _portrait;
    private TutorialAnimation _animation = TutorialAnimation.None;
    private float _animTime;
    private bool _dialogueMode = true;

    public TutorialDialogueControl(Action onAdvance)
    {
        _onAdvance = onAdvance;
        MouseFilter = MouseFilterMode.Stop;

        _spotlight = new SpotlightLayer { Visible = false, MouseFilter = MouseFilterMode.Ignore };
        AddChild(_spotlight);
        SetAnchorPreset(_spotlight, LayoutPreset.Wide);

        _dim = new PanelContainer
        {
            Visible = false,
            MouseFilter = MouseFilterMode.Ignore,
            PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#080610F2") },
        };
        AddChild(_dim);
        SetAnchorPreset(_dim, LayoutPreset.Wide);

        _portraitSlot = new Control { Visible = false, MouseFilter = MouseFilterMode.Ignore };
        AddChild(_portraitSlot);

        _bottomBar = new PanelContainer
        {
            Visible = false,
            MouseFilter = MouseFilterMode.Ignore,
            PanelOverride = new EclipseStyleBoxRounded
            {
                BackgroundColor = Color.FromHex("#0A0602F5"),
                BorderColor = Color.FromHex("#A85E12B0"),
                BorderThickness = new Thickness(2),
                Radius = 12,
                ContentMarginLeftOverride = 26,
                ContentMarginRightOverride = 26,
                ContentMarginTopOverride = 18,
                ContentMarginBottomOverride = 18,
            },
        };
        _bottomContent = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            MouseFilter = MouseFilterMode.Ignore,
            SeparationOverride = 8,
        };
        _bottomBar.AddChild(_bottomContent);
        AddChild(_bottomBar);
        SetAnchorLeft(_bottomBar, 0f);
        SetAnchorRight(_bottomBar, 1f);
        SetAnchorTop(_bottomBar, 1f);
        SetAnchorBottom(_bottomBar, 1f);
        SetMarginLeft(_bottomBar, BottomBarInset);
        SetMarginRight(_bottomBar, -BottomBarInset);
        SetMarginTop(_bottomBar, -(BottomBarHeight + BottomBarBottom));
        SetMarginBottom(_bottomBar, -BottomBarBottom);
        SetGrowHorizontal(_bottomBar, GrowDirection.Constrain);
        SetGrowVertical(_bottomBar, GrowDirection.Constrain);

        _taskBanner = new PanelContainer
        {
            Visible = false,
            MouseFilter = MouseFilterMode.Ignore,
            PanelOverride = new EclipseStyleBoxRounded
            {
                BackgroundColor = Color.FromHex("#070300F2"),
                BorderColor = Color.FromHex("#E6A11A"),
                BorderThickness = new Thickness(1),
                Radius = 10,
                ContentMarginLeftOverride = 24,
                ContentMarginRightOverride = 24,
                ContentMarginTopOverride = 12,
                ContentMarginBottomOverride = 12,
            },
        };
        _taskText = new RichTextLabel();
        _taskBanner.AddChild(_taskText);
        AddChild(_taskBanner);
        SetAnchorLeft(_taskBanner, 0.5f);
        SetAnchorRight(_taskBanner, 0.5f);
        SetAnchorTop(_taskBanner, 0f);
        SetAnchorBottom(_taskBanner, 0f);
        SetMarginTop(_taskBanner, 40f);
        SetGrowHorizontal(_taskBanner, GrowDirection.Both);
        SetGrowVertical(_taskBanner, GrowDirection.End);
    }

    public void ShowDialogue(string speaker, string text, string? portrait, TutorialSide side, TutorialAnimation animation)
    {
        _dialogueMode = true;
        _animation = animation;
        _animTime = 0f;
        MouseFilter = MouseFilterMode.Stop; // capture clicks: click anywhere to advance

        _spotlight.Visible = false;
        _taskBanner.Visible = false;
        _dim.Visible = true;
        _bottomBar.Visible = true;
        _portraitSlot.Visible = true;

        BuildPortrait(portrait, side);

        _bottomContent.RemoveAllChildren();
        var name = new RichTextLabel { HorizontalExpand = true };
        name.SetMarkup($"[color=#E6A11A][bold]{FormattedMessage.EscapeText(speaker)}[/bold][/color]");
        _bottomContent.AddChild(name);

        var body = new RichTextLabel { HorizontalExpand = true };
        body.SetMarkup(text);
        _bottomContent.AddChild(body);

        var hint = new RichTextLabel { HorizontalAlignment = HAlignment.Right };
        hint.SetMarkup("[color=#8A8A8A]нажмите, чтобы продолжить ▼[/color]");
        _bottomContent.AddChild(hint);

        Visible = true;
    }

    public void ShowHealthTask(string speaker, string objective)
    {
        _dialogueMode = false;
        MouseFilter = MouseFilterMode.Ignore; // let clicks reach the real health alert underneath

        _dim.Visible = false;
        _bottomBar.Visible = false;
        _portraitSlot.Visible = false;
        _spotlight.Visible = true;
        _taskBanner.Visible = true;

        _taskText.SetMarkup($"[color=#E6A11A][bold]{FormattedMessage.EscapeText(objective)}[/bold][/color]");
        Visible = true;
    }

    private void BuildPortrait(string? portrait, TutorialSide side)
    {
        _portraitSlot.RemoveAllChildren();
        _portrait = MakePortrait(portrait);

        var bottom = BottomBarBottom + BottomBarHeight + 6f;
        SetAnchorTop(_portraitSlot, 1f);
        SetAnchorBottom(_portraitSlot, 1f);
        SetMarginBottom(_portraitSlot, -bottom);
        SetMarginTop(_portraitSlot, -(bottom + PortraitSize));
        SetGrowHorizontal(_portraitSlot, GrowDirection.Constrain);
        SetGrowVertical(_portraitSlot, GrowDirection.Constrain);

        if (side == TutorialSide.Right)
        {
            SetAnchorLeft(_portraitSlot, 1f);
            SetAnchorRight(_portraitSlot, 1f);
            SetMarginRight(_portraitSlot, -BottomBarInset);
            SetMarginLeft(_portraitSlot, -(BottomBarInset + PortraitSize));
        }
        else
        {
            SetAnchorLeft(_portraitSlot, 0f);
            SetAnchorRight(_portraitSlot, 0f);
            SetMarginLeft(_portraitSlot, BottomBarInset);
            SetMarginRight(_portraitSlot, BottomBarInset + PortraitSize);
        }

        if (_portrait != null)
            _portraitSlot.AddChild(_portrait);
    }

    private static Control? MakePortrait(string? portrait)
    {
        if (string.IsNullOrWhiteSpace(portrait))
            return null;

        switch (portrait)
        {
            case "shape:square":
                return new PanelContainer
                {
                    MouseFilter = MouseFilterMode.Ignore,
                    PanelOverride = new StyleBoxFlat { BackgroundColor = Square },
                };
            case "shape:circle":
                return new PanelContainer
                {
                    MouseFilter = MouseFilterMode.Ignore,
                    PanelOverride = new EclipseStyleBoxRounded { BackgroundColor = Circle, Radius = PortraitSize / 2f },
                };
            default:
                return new TextureRect
                {
                    TexturePath = portrait,
                    Stretch = TextureRect.StretchMode.KeepAspectCentered,
                    MouseFilter = MouseFilterMode.Ignore,
                };
        }
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (_dialogueMode && args.Function == EngineKeyFunctions.UIClick)
        {
            _onAdvance();
            args.Handle();
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (Parent != null)
            SetSize = Parent.Size;

        if (_portrait == null || !_dialogueMode)
            return;

        _animTime += args.DeltaSeconds;
        var (dx, dy) = ComputeOffset();
        _portrait.Margin = new Thickness(dx, dy, -dx, -dy);
    }

    private (float dx, float dy) ComputeOffset()
    {
        var decay = MathF.Exp(-_animTime * 3f);
        switch (_animation)
        {
            case TutorialAnimation.Bounce:
                return (0f, -MathF.Abs(MathF.Sin(_animTime * 9f)) * 14f * decay);
            case TutorialAnimation.Nod:
                return (0f, MathF.Sin(_animTime * 7f) * 7f * decay);
            case TutorialAnimation.Shake:
                return (MathF.Sin(_animTime * 40f) * 5f * decay, 0f);
            case TutorialAnimation.SwayLeft:
                return (-4f + MathF.Sin(_animTime * 2.5f) * 6f, 0f);
            case TutorialAnimation.SwayRight:
                return (4f + MathF.Sin(_animTime * 2.5f) * 6f, 0f);
            default:
                return (0f, 0f);
        }
    }

    /// <summary>Darkens the whole screen except the active alerts widget (where the health icon lives).</summary>
    private sealed class SpotlightLayer : Control
    {
        private static readonly Color Dim = Color.FromHex("#000000C0");
        private static readonly Color BorderColor = Color.FromHex("#E6A11A");

        private readonly IUserInterfaceManager _ui;

        public SpotlightLayer()
        {
            _ui = IoCManager.Resolve<IUserInterfaceManager>();
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            var size = PixelSize;
            var full = new UIBox2(0, 0, size.X, size.Y);

            var alerts = _ui.GetActiveUIWidgetOrNull<AlertsUI>();
            if (alerts is not { Visible: true })
            {
                handle.DrawRect(full, Dim);
                return;
            }

            const float pad = 8f;
            var t = alerts.GlobalPixelRect;
            var r = new UIBox2(t.Left - pad, t.Top - pad, t.Right + pad, t.Bottom + pad);

            handle.DrawRect(new UIBox2(0, 0, size.X, r.Top), Dim);
            handle.DrawRect(new UIBox2(0, r.Bottom, size.X, size.Y), Dim);
            handle.DrawRect(new UIBox2(0, r.Top, r.Left, r.Bottom), Dim);
            handle.DrawRect(new UIBox2(r.Right, r.Top, size.X, r.Bottom), Dim);

            const float bt = 2f;
            handle.DrawRect(new UIBox2(r.Left - bt, r.Top - bt, r.Right + bt, r.Top), BorderColor);
            handle.DrawRect(new UIBox2(r.Left - bt, r.Bottom, r.Right + bt, r.Bottom + bt), BorderColor);
            handle.DrawRect(new UIBox2(r.Left - bt, r.Top, r.Left, r.Bottom), BorderColor);
            handle.DrawRect(new UIBox2(r.Right, r.Top, r.Right + bt, r.Bottom), BorderColor);
        }
    }
}
