using System.Numerics;
using System.Linq;
using Content.Client.PDA;
using Content.Client.Resources;
using Content.Shared._Eclipse.WallDisplays;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._Eclipse.WallDisplays;

public sealed class TelevisionBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private TelevisionWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<TelevisionWindow>();
        _window.Tune += (channel, camera) => SendMessage(new TelevisionTuneMessage(channel, camera));
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is TelevisionState television) _window?.Update(television, EntMan);
    }
}

public sealed class TelevisionWindow : DefaultWindow
{
    public event Action<TelevisionChannel, NetEntity?>? Tune;
    private readonly OptionButton _channels = new() { MinWidth = 230, MinHeight = 32, HorizontalExpand = true };
    private readonly Label _status = new() { StyleClasses = { "terminal-muted" } };
    private readonly BoxContainer _content = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, VerticalExpand = true };
    private TelevisionChannel? _channel;
    private PublicBroadcastChannel[]? _listedBroadcasts;
    private PublicBroadcastView? _video;
    private bool _expanded;

    public TelevisionWindow()
    {
        Title = Loc.GetString("television-title"); SetSize = new Vector2(980, 650); MinSize = new Vector2(760, 520);
        Stylesheet = PdaTheme.Create(IoCManager.Resolve<IUserInterfaceManager>().Stylesheet);
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10, Margin = new Thickness(12) };
        var toolbar = new BoxContainer { SeparationOverride = 8 };
        _channels.OnItemSelected += args =>
        {
            if (_listedBroadcasts == null || args.Id < 0 || args.Id >= _listedBroadcasts.Length) return;
            _channels.SelectId(args.Id); Tune?.Invoke(TelevisionChannel.Broadcast, _listedBroadcasts[args.Id].Camera);
        };
        toolbar.AddChild(_channels);
        foreach (var channel in new[] { TelevisionChannel.Flappy, TelevisionChannel.Blocks })
        {
            var button = PdaAppUi.Action(Loc.GetString(channel == TelevisionChannel.Flappy ? "television-flappy" : "television-blocks"));
            button.OnPressed += _ => Tune?.Invoke(channel, null); toolbar.AddChild(button);
        }
        var expand = PdaAppUi.Action(Loc.GetString("television-expand"));
        expand.OnPressed += _ =>
        {
            _expanded = !_expanded;
            SetSize = _expanded ? UserInterfaceManager.RootControl.Size - new Vector2(40) : new Vector2(980, 650);
            OpenCentered();
        };
        toolbar.AddChild(expand);
        root.AddChild(toolbar); root.AddChild(_status);
        _content.HorizontalExpand = true;
        var screen = PdaAppUi.Card(_content, "#081217", "#354951", 8); screen.VerticalExpand = true;
        root.AddChild(screen); ContentsContainer.AddChild(root);
    }

    public void Update(TelevisionState state, IEntityManager entities)
    {
        if (_listedBroadcasts == null || !_listedBroadcasts.SequenceEqual(state.Broadcasts))
        {
            _listedBroadcasts = state.Broadcasts; _channels.Clear();
            if (state.Broadcasts.Length == 0) _channels.AddItem(Loc.GetString("television-no-broadcasts"), -1);
            for (var i = 0; i < state.Broadcasts.Length; i++) _channels.AddItem(state.Broadcasts[i].Reporter, i);
        }
        _channels.Disabled = state.Broadcasts.Length == 0;
        var selected = Array.FindIndex(state.Broadcasts, broadcast => broadcast.Camera == state.Camera);
        if (selected >= 0) _channels.SelectId(selected);
        _status.Text = Loc.GetString(state.Channel == TelevisionChannel.Broadcast && state.Camera != null ? "television-live-status" : state.Channel == TelevisionChannel.Broadcast ? "television-no-signal" : "television-game-status");
        if (_channel != state.Channel)
        {
            _channel = state.Channel; _video = null; _content.RemoveAllChildren();
            switch (state.Channel)
            {
                case TelevisionChannel.Broadcast:
                    _video = new PublicBroadcastView(entities.System<TelevisionRenderSystem>()) { HorizontalExpand = true, VerticalExpand = true };
                    _content.AddChild(_video); break;
                case TelevisionChannel.Flappy: _content.AddChild(new PocketFlappyControl()); break;
                case TelevisionChannel.Blocks: _content.AddChild(new PocketBlockinatorControl()); break;
            }
        }
        if (_video != null) _video.Camera = entities.GetEntity(state.Camera);
    }
}

public sealed class PublicBroadcastView(TelevisionRenderSystem renderer) : Control
{
    public EntityUid? Camera;
    private readonly Font _font = IoCManager.Resolve<IResourceCache>().GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 14);
    protected override void Draw(DrawingHandleScreen handle)
    {
        handle.DrawRect(new UIBox2(Vector2.Zero, PixelSize), Color.Black);
        if (Camera is { } camera && renderer.RenderFeed(camera, detailed: true) is { } texture)
        {
            const float aspect = 16f / 9;
            var width = Math.Min(PixelWidth, PixelHeight * aspect);
            var size = new Vector2(width, width / aspect);
            var origin = (PixelSize - size) / 2;
            handle.DrawTextureRect(texture, new UIBox2(origin, origin + size));
        }
        else handle.DrawString(_font, new Vector2(18, PixelHeight / 2f), Loc.GetString("television-start-camera"), Color.FromHex("#91B6B9"));
    }
}
