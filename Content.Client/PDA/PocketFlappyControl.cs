using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;
using Content.Client.Resources;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Client.ResourceManagement;

namespace Content.Client.PDA;

/// <summary>A local minigame. Scores never award currency or affect the round.</summary>
public sealed class PocketFlappyControl : BoxContainer
{
    private readonly PocketFlappyModel _game = new();
    private readonly FlappyBoard _board;
    private readonly Label _status;
    private readonly IInputManager _input = IoCManager.Resolve<IInputManager>();
    private bool _paused;
    private bool _manualPause;
    private readonly IConfigurationManager _config = IoCManager.Resolve<IConfigurationManager>();

    public PocketFlappyControl()
    {
        _game.LoadBest(_config.GetCVar(CCVars.PdaFlappyBest));
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        SeparationOverride = 6;
        AddChild(new Label { Text = Loc.GetString("pda-os-flappy-title"), StyleClasses = { "pda-title" } });
        _status = new Label();
        AddChild(_status);
        _board = new FlappyBoard(_game) { MinSize = new Vector2(400, 190), HorizontalExpand = true, VerticalExpand = true, CanKeyboardFocus = true, MouseFilter = MouseFilterMode.Stop, RectClipContent = true };
        _board.OnKeyBindDown += args => { if (args.Function == EngineKeyFunctions.UIClick) { Flap(); args.Handle(); } };
        AddChild(_board);
        var controls = new BoxContainer { SeparationOverride = 8 };
        var flap = new Button { Text = Loc.GetString("pda-os-flappy-flap"), HorizontalExpand = true };
        flap.OnPressed += _ => Flap();
        var restart = new Button { Text = Loc.GetString("pda-os-flappy-restart") };
        restart.OnPressed += _ => { SaveBest(); _game.Restart(); _board.GrabKeyboardFocus(); };
        var pause = new Button { Text = Loc.GetString("pda-game-pause"), ToggleMode = true };
        pause.OnToggled += args => { _manualPause = args.Pressed; pause.Text = Loc.GetString(_manualPause ? "pda-game-resume" : "pda-game-pause"); if (!_manualPause) _board.GrabKeyboardFocus(); };
        controls.AddChild(flap); controls.AddChild(pause); controls.AddChild(restart); AddChild(controls);
        AddChild(new Label { Text = Loc.GetString("pda-os-flappy-help"), StyleClasses = { "terminal-muted" } });
        UpdateStatus();
    }

    protected override void EnteredTree() { base.EnteredTree(); _input.FirstChanceOnKeyEvent += OnKey; }
    protected override void ExitedTree() { SaveBest(); _input.FirstChanceOnKeyEvent -= OnKey; base.ExitedTree(); }
    private bool FocusedInside()
    {
        var focused = UserInterfaceManager.KeyboardFocused;
        while (focused != null) { if (focused == this) return true; focused = focused.Parent; }
        return false;
    }
    public void FocusGame() => _board.GrabKeyboardFocus();
    private void OnKey(KeyEventArgs args, KeyEventType type)
    {
        if (!VisibleInTree || !FocusedInside() || args.Key != Keyboard.Key.Space) return;
        args.Handle();
        if (type == KeyEventType.Down) Flap();
    }
    private void Flap() { if (_manualPause) return; if (_game.GameOver) { SaveBest(); _game.Restart(); } _game.Flap(); _board.GrabKeyboardFocus(); }
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _paused = _manualPause || !VisibleInTree || !FocusedInside();
        if (!_paused) _game.Tick(args.DeltaSeconds);
        _board.Paused = _paused;
        if (_game.GameOver) SaveBest();
        UpdateStatus();
    }
    private void SaveBest() { if (_game.Best > _config.GetCVar(CCVars.PdaFlappyBest)) _config.SetCVar(CCVars.PdaFlappyBest, _game.Best); }
    private void UpdateStatus() => _status.Text = Loc.GetString("pda-os-flappy-score", ("score", _game.Score), ("best", _game.Best));

    private sealed class FlappyBoard(PocketFlappyModel game) : Control
    {
        public bool Paused;
        private readonly Texture _ship = IoCManager.Resolve<IResourceCache>().GetResource<TextureResource>("/Textures/_Eclipse/Interface/PDA/Icons/Field/flappy.png").Texture;
        private readonly Font _font = IoCManager.Resolve<IResourceCache>().GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 14);
        private readonly StyleBox _gate = PdaTheme.Box("#2D4449", "#82958B", radius: 5);
        protected override void Draw(DrawingHandleScreen h)
        {
            var scale = Math.Min(PixelWidth / (float) PocketFlappyModel.Width, PixelHeight / 280f);
            var origin = (PixelSize - new Vector2(PocketFlappyModel.Width, 280) * scale) / 2;
            Vector2 P(float x, float y) => origin + new Vector2(x, y) * scale;
            h.DrawRect(PixelSizeBox, Color.FromHex("#0E1822"));
            h.DrawRect(new UIBox2(P(0, 0), P(PocketFlappyModel.Width, 280)), Color.FromHex("#112837"));
            for (var i = 0; i < 64; i++) h.DrawCircle(P(((i * 71 + 817 - game.Distance * .12f) % PocketFlappyModel.Width + PocketFlappyModel.Width) % PocketFlappyModel.Width, (i * 47 + 11) % 260), (i % 3 == 0 ? 1.3f : .7f) * scale, Color.FromHex("#ABC8DB88"));
            foreach (var pipe in game.Pipes)
            {
                _gate.Draw(h, new UIBox2(P(pipe.X, -5), P(pipe.X + 42, pipe.Gap - 48)), scale);
                _gate.Draw(h, new UIBox2(P(pipe.X, pipe.Gap + 48), P(pipe.X + 42, 285)), scale);
                h.DrawRect(new UIBox2(P(pipe.X + 5, pipe.Gap - 54), P(pipe.X + 37, pipe.Gap - 50)), Color.FromHex("#D3C28D"));
                h.DrawRect(new UIBox2(P(pipe.X + 5, pipe.Gap + 50), P(pipe.X + 37, pipe.Gap + 54)), Color.FromHex("#D3C28D"));
            }
            h.DrawTextureRect(_ship, new UIBox2(P(PocketFlappyModel.BirdX - 13, game.Y - 13), P(PocketFlappyModel.BirdX + 13, game.Y + 13)));
            h.DrawRect(new UIBox2(P(0, 272), P(PocketFlappyModel.Width, 280)), Color.FromHex("#284D54"));
            if (!game.Started || game.GameOver || Paused)
            {
                var text = Loc.GetString(game.GameOver ? "pda-flappy-game-over" : !game.Started ? "pda-flappy-start" : "pda-game-pause");
                h.DrawRect(PixelSizeBox, Color.FromHex("#08131CA0"));
                var dimensions = h.GetDimensions(_font, text, UIScale);
                h.DrawString(_font, (PixelSize - dimensions) / 2, text, UIScale, Color.FromHex("#F0E3BD"));
            }
        }
    }
}

public sealed class PocketFlappyModel
{
    public const int Width = 800;
    public const int BirdX = 160;
    public sealed class Pipe(float x, float gap) { public float X = x; public readonly float Gap = gap; public bool Scored; }
    public readonly List<Pipe> Pipes = new();
    public float Y { get; private set; } = 140;
    public float Velocity { get; private set; }
    public int Score { get; private set; }
    public int Best { get; private set; }
    public bool GameOver { get; private set; }
    public bool Started { get; private set; }
    public float Distance { get; private set; }
    public void LoadBest(int best) => Best = Math.Max(Best, Math.Max(0, best));
    private readonly Random _random = new();
    private float _spawn = .6f;

    public void Restart()
    {
        Y = 140; Velocity = 0; Score = 0; GameOver = false; Started = false; Distance = 0; _spawn = .6f; Pipes.Clear();
    }
    public void Flap()
    {
        if (GameOver) return;
        Started = true;
        Velocity = -145;
    }
    public void Tick(float seconds)
    {
        if (!Started || GameOver) return;
        // Fixed substeps keep collision behaviour stable across normal frame rates and hitches.
        var remaining = Math.Clamp(seconds, 0, .25f);
        while (remaining > 0 && !GameOver)
        {
            var dt = Math.Min(remaining, 1f / 120);
            remaining -= dt;
            Velocity += 390 * dt;
            Distance += 150 * dt;
            Y += Velocity * dt;
            _spawn -= dt;
            if (_spawn <= 0) { Pipes.Add(new Pipe(Width + 20, _random.Next(85, 196))); _spawn += 1.4f; }
            foreach (var pipe in Pipes)
            {
                pipe.X -= 150 * dt;
                if (!pipe.Scored && pipe.X + 42 < BirdX - 9) { pipe.Scored = true; Score++; Best = Math.Max(Best, Score); }
                if (pipe.X < BirdX + 9 && pipe.X + 42 > BirdX - 9 && (Y - 9 < pipe.Gap - 48 || Y + 9 > pipe.Gap + 48)) GameOver = true;
            }
            Pipes.RemoveAll(pipe => pipe.X < -50);
            if (Y < 9 || Y > 263) GameOver = true;
        }
    }
}
