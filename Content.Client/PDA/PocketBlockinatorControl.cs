using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared.Arcade;
using Content.Shared.Input;
using Robust.Client.Input;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client.PDA;

public sealed class PocketBlockinatorControl : BoxContainer
{
    private const int BoardWidth = 10;
    private const int BoardHeight = 20;
    private const float HorizontalRepeatDelay = 0.22f;
    private const float HorizontalRepeatInterval = 0.09f;
    private const float SoftDropRefreshTime = 0.18f;

    private readonly PocketBlockinatorBoard _board = new(BoardWidth, BoardHeight, new Vector2(160, 320));
    private readonly PocketBlockinatorBoard _next = new(4, 4, new Vector2(64, 64));
    private readonly PocketBlockinatorBoard _hold = new(4, 4, new Vector2(64, 64));
    private readonly Button _leftButton;
    private readonly Button _rightButton;
    private readonly Button _rotateButton;
    private readonly Button _downButton;
    private readonly Button _dropButton;
    private readonly Button _holdButton;
    private readonly Label _scoreLabel;
    private readonly Label _levelLabel;
    private readonly Label _statusLabel;
    private readonly Button _newGameButton;

    [Dependency] private readonly IInputManager _inputManager = default!;
    [Dependency] private readonly IUserInterfaceManager _userInterface = default!;

    private readonly Random _random = new();
    private readonly BlockGameBlock.BlockGameBlockColor?[,] _field = new BlockGameBlock.BlockGameBlockColor?[BoardWidth, BoardHeight];
    private readonly List<PieceKind> _bag = new();

    private Piece _current;
    private Piece _nextPiece;
    private Piece? _heldPiece;
    private Vector2i _currentPosition;
    private Rotation _rotation;
    private bool _canHold;
    private bool _running;
    private bool _paused;
    private bool _wasFocusedInside;
    private bool _gameOver;
    private float _softDropTimeLeft;
    private int _horizontalDirection;
    private float _horizontalHoldTime;
    private bool _horizontalRepeatStarted;
    private float _fallAccumulator;
    private int _score;
    private int _level;
    private int _lines;

    public PocketBlockinatorControl()
    {
        IoCManager.InjectDependencies(this);

        Orientation = LayoutOrientation.Horizontal;
        HorizontalAlignment = HAlignment.Center;
        VerticalAlignment = VAlignment.Center;
        HorizontalExpand = true;
        VerticalExpand = true;
        Margin = new Thickness(8);
        CanKeyboardFocus = true;

        var mainColumn = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalAlignment = HAlignment.Center
        };

        var stats = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Center
        };

        _levelLabel = new Label { MinWidth = 90 };
        _scoreLabel = new Label { MinWidth = 90 };
        stats.AddChild(_levelLabel);
        stats.AddChild(_scoreLabel);
        mainColumn.AddChild(stats);
        mainColumn.AddChild(WrapBoard(_board, new Thickness(6)));
        mainColumn.AddChild(new Control { MinSize = new Vector2(1, 6) });

        var controlsTop = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Center
        };
        _leftButton = MakeControlButton("<", () => TryMoveHorizontal(-1));
        _rotateButton = MakeControlButton("R", () => TryRotate(false));
        _rightButton = MakeControlButton(">", () => TryMoveHorizontal(1));
        controlsTop.AddChild(_leftButton);
        controlsTop.AddChild(_rotateButton);
        controlsTop.AddChild(_rightButton);
        mainColumn.AddChild(controlsTop);

        var controlsBottom = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Center
        };
        _downButton = MakeControlButton("v", StepDown);
        _dropButton = MakeControlButton("!", HardDrop);
        _holdButton = MakeControlButton("H", HoldPiece);
        controlsBottom.AddChild(_downButton);
        controlsBottom.AddChild(_dropButton);
        controlsBottom.AddChild(_holdButton);
        mainColumn.AddChild(controlsBottom);

        var sideColumn = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            MinWidth = 96,
            Margin = new Thickness(10, 0, 0, 0)
        };

        sideColumn.AddChild(new Label { Text = Loc.GetString("blockgame-menu-label-next"), Align = Label.AlignMode.Center });
        sideColumn.AddChild(WrapBoard(_next, new Thickness(5)));
        sideColumn.AddChild(new Control { MinSize = new Vector2(1, 8) });
        sideColumn.AddChild(new Label { Text = Loc.GetString("blockgame-menu-label-hold"), Align = Label.AlignMode.Center });
        sideColumn.AddChild(WrapBoard(_hold, new Thickness(5)));
        sideColumn.AddChild(new Control { MinSize = new Vector2(1, 10) });

        _statusLabel = new Label
        {
            Text = Loc.GetString("blockgame-menu-button-new-game"),
            Align = Label.AlignMode.Center
        };
        sideColumn.AddChild(_statusLabel);

        _newGameButton = new Button
        {
            Text = Loc.GetString("blockgame-menu-button-new-game"),
            TextAlign = Label.AlignMode.Center,
            HorizontalExpand = true
        };
        _newGameButton.OnPressed += _ => StartNewGame();
        sideColumn.AddChild(_newGameButton);

        AddChild(mainColumn);
        AddChild(sideColumn);

        _inputManager.FirstChanceOnKeyEvent += OnFirstChanceKeyEvent;

        _nextPiece = RandomPiece();
        UpdateLabels();
        UpdateVisuals();
        UpdateControlButtons();
    }

    private Button MakeControlButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            MinSize = new Vector2(44, 28),
            TextAlign = Label.AlignMode.Center,
            Margin = new Thickness(2)
        };

        button.OnPressed += _ =>
        {
            action();
            UpdateControlButtons();
            GrabKeyboardFocus();
        };

        return button;
    }

    private static PanelContainer WrapBoard(Control board, Thickness margin)
    {
        var frame = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#202631"),
                BorderColor = Color.FromHex("#2b3544"),
                BorderThickness = new Thickness(2)
            },
            Margin = margin
        };

        board.Margin = new Thickness(6);
        frame.AddChild(board);
        return frame;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        GrabKeyboardFocus();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _inputManager.FirstChanceOnKeyEvent -= OnFirstChanceKeyEvent;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Handled)
            return;

        if (!_running || _paused)
            return;

        if (IsLeft(args.Function))
        {
            TryMoveHorizontal(-1);
            args.Handle();
        }
        else if (IsRight(args.Function))
        {
            TryMoveHorizontal(1);
            args.Handle();
        }
        else if (IsUp(args.Function))
        {
            TryRotate(false);
            args.Handle();
        }
        else if (args.Function == ContentKeyFunctions.Arcade3)
        {
            TryRotate(true);
            args.Handle();
        }
        else if (IsDown(args.Function))
        {
            RefreshSoftDrop();
            StepDown();
            args.Handle();
        }
        else if (args.Function == ContentKeyFunctions.Arcade2)
        {
            HoldPiece();
            args.Handle();
        }
        else if (args.Function == ContentKeyFunctions.Arcade1)
        {
            HardDrop();
            args.Handle();
        }
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (IsLeft(args.Function))
        {
            EndHorizontalInput(-1);
            args.Handle();
        }
        else if (IsRight(args.Function))
        {
            EndHorizontalInput(1);
            args.Handle();
        }
        else if (IsDown(args.Function))
        {
            _softDropTimeLeft = 0f;
            args.Handle();
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        UpdateFocusState();

        if (!_running || _paused)
            return;

        if (_softDropTimeLeft > 0f)
            _softDropTimeLeft = MathF.Max(0f, _softDropTimeLeft - args.DeltaSeconds);

        if (_horizontalDirection != 0)
            UpdateHorizontalRepeat(args.DeltaSeconds);

        _fallAccumulator += args.DeltaSeconds;
        var softDropping = _softDropTimeLeft > 0f;
        var fallSpeed = softDropping ? 0.045f : MathF.Max(0.12f, 0.75f - _level * 0.055f);
        while (_fallAccumulator >= fallSpeed)
        {
            StepDown();
            _fallAccumulator -= fallSpeed;
        }
    }

    private void StartNewGame()
    {
        Array.Clear(_field);
        _bag.Clear();
        _score = 0;
        _level = 0;
        _lines = 0;
        _heldPiece = null;
        _nextPiece = RandomPiece();
        _running = true;
        _paused = false;
        _gameOver = false;
        _statusLabel.Text = string.Empty;
        SpawnPiece();
        UpdateLabels();
        UpdateVisuals();
        UpdateControlButtons();
        GrabKeyboardFocus();
    }

    private void SpawnPiece()
    {
        _current = _nextPiece;
        _nextPiece = RandomPiece();
        _rotation = Rotation.North;
        _currentPosition = new Vector2i(4, 1);
        _canHold = true;
        _fallAccumulator = 0f;

        if (!CanPlace(_current, _currentPosition, _rotation))
            EndGame();
    }

    private void StepDown()
    {
        if (!_running || _paused)
            return;

        var nextPosition = _currentPosition.AddToY(1);
        if (CanPlace(_current, nextPosition, _rotation))
        {
            _currentPosition = nextPosition;
            if (_softDropTimeLeft > 0f)
                _score++;

            UpdateVisuals();
            return;
        }

        LockPiece();
    }

    private void HardDrop()
    {
        if (!_running || _paused)
            return;

        var dropped = 0;
        while (CanPlace(_current, _currentPosition.AddToY(1), _rotation))
        {
            _currentPosition = _currentPosition.AddToY(1);
            dropped++;
        }

        _score += dropped * 2;
        LockPiece();
    }

    private void LockPiece()
    {
        foreach (var pos in _current.Positions(_currentPosition, _rotation))
        {
            if (pos.X < 0 || pos.X >= BoardWidth || pos.Y < 0 || pos.Y >= BoardHeight)
                continue;

            _field[pos.X, pos.Y] = _current.Color;
        }

        ClearLines();
        SpawnPiece();
        UpdateLabels();
        UpdateVisuals();
    }

    private void ClearLines()
    {
        var cleared = 0;
        for (var y = BoardHeight - 1; y >= 0; y--)
        {
            var full = true;
            for (var x = 0; x < BoardWidth; x++)
            {
                if (_field[x, y] != null)
                    continue;

                full = false;
                break;
            }

            if (!full)
                continue;

            cleared++;
            for (var yy = y; yy > 0; yy--)
            {
                for (var x = 0; x < BoardWidth; x++)
                    _field[x, yy] = _field[x, yy - 1];
            }

            for (var x = 0; x < BoardWidth; x++)
                _field[x, 0] = null;

            y++;
        }

        if (cleared == 0)
            return;

        _lines += cleared;
        _level = _lines / 10;
        _score += cleared switch
        {
            1 => 100,
            2 => 300,
            3 => 500,
            _ => 800
        } * (_level + 1);
    }

    private void HoldPiece()
    {
        if (!_running || _paused || !_canHold)
            return;

        if (_heldPiece == null)
        {
            _heldPiece = _current;
            SpawnPiece();
        }
        else
        {
            (_current, _heldPiece) = (_heldPiece.Value, _current);
            _rotation = Rotation.North;
            _currentPosition = new Vector2i(4, 1);
            if (!CanPlace(_current, _currentPosition, _rotation))
                EndGame();
        }

        _canHold = false;
        UpdateVisuals();
    }

    private void TryRotate(bool inverted)
    {
        if (!_running || _paused || !_current.CanSpin)
            return;

        var nextRotation = NextRotation(_rotation, inverted);
        foreach (var offset in new[] { Vector2i.Zero, new Vector2i(-1, 0), new Vector2i(1, 0), new Vector2i(0, -1), new Vector2i(-2, 0), new Vector2i(2, 0) })
        {
            var nextPosition = _currentPosition + offset;
            if (!CanPlace(_current, nextPosition, nextRotation))
                continue;

            _currentPosition = nextPosition;
            _rotation = nextRotation;
            UpdateVisuals();
            return;
        }
    }

    private bool TryMoveHorizontal(int offset)
    {
        if (!_running || _paused)
            return false;

        var nextPosition = _currentPosition.AddToX(offset);
        if (!CanPlace(_current, nextPosition, _rotation))
            return false;

        _currentPosition = nextPosition;
        UpdateVisuals();
        return true;
    }

    private bool CanPlace(Piece piece, Vector2i position, Rotation rotation)
    {
        foreach (var pos in piece.Positions(position, rotation))
        {
            if (pos.X < 0 || pos.X >= BoardWidth || pos.Y >= BoardHeight)
                return false;

            if (pos.Y >= 0 && _field[pos.X, pos.Y] != null)
                return false;
        }

        return true;
    }

    private void EndGame()
    {
        _running = false;
        _paused = false;
        _gameOver = true;
        _statusLabel.Text = Loc.GetString("blockgame-menu-msg-game-over");
        UpdateControlButtons();
        UpdateVisuals();
    }

    private void UpdateControlButtons()
    {
        var canPlay = _running && !_paused;
        _leftButton.Disabled = !canPlay;
        _rightButton.Disabled = !canPlay;
        _rotateButton.Disabled = !canPlay;
        _downButton.Disabled = !canPlay;
        _dropButton.Disabled = !canPlay;
        _holdButton.Disabled = !canPlay || !_canHold;
    }

    private void UpdateLabels()
    {
        _levelLabel.Text = Loc.GetString("blockgame-menu-label-level", ("level", _level + 1));
        _scoreLabel.Text = Loc.GetString("blockgame-menu-label-points", ("points", _score));
    }

    private void UpdateVisuals()
    {
        var blocks = new List<BlockGameBlock>();
        for (var y = 0; y < BoardHeight; y++)
        {
            for (var x = 0; x < BoardWidth; x++)
            {
                if (_field[x, y] is { } color)
                    blocks.Add(new BlockGameBlock(new Vector2i(x, y), color));
            }
        }

        if (_running && !_gameOver)
        {
            var ghostPosition = _currentPosition;
            while (CanPlace(_current, ghostPosition.AddToY(1), _rotation))
                ghostPosition = ghostPosition.AddToY(1);

            blocks.AddRange(_current.Blocks(ghostPosition, _rotation, true));
            blocks.AddRange(_current.Blocks(_currentPosition, _rotation));
        }

        _board.SetBlocks(blocks);
        _next.SetBlocks(_nextPiece.BlocksForPreview());
        _hold.SetBlocks(_heldPiece?.BlocksForPreview() ?? Array.Empty<BlockGameBlock>());
    }

    private void UpdateFocusState()
    {
        var focusedInside = IsFocusedInsideThis();
        if (focusedInside == _wasFocusedInside)
            return;

        _wasFocusedInside = focusedInside;

        if (focusedInside)
            ResumeGame();
        else
            PauseGame();
    }

    private void OnFirstChanceKeyEvent(KeyEventArgs args, KeyEventType type)
    {
        if (!IsGameKey(args.Key))
            return;

        if (args.Handled)
            return;

        if (!IsFocusedInsideThis())
        {
            PauseGame();
            return;
        }

        if (!_running)
        {
            args.Handle();
            return;
        }

        if (_paused)
            ResumeGame();

        if (type == KeyEventType.Down || type == KeyEventType.Repeat)
            HandleGameKeyDown(args.Key);
        else if (type == KeyEventType.Up)
            HandleGameKeyUp(args.Key);

        args.Handle();
    }

    private void HandleGameKeyDown(Keyboard.Key key)
    {
        if (key == Keyboard.Key.Left)
            TryMoveHorizontal(-1);
        else if (key == Keyboard.Key.Right)
            TryMoveHorizontal(1);
        else if (key == Keyboard.Key.Up)
            TryRotate(false);
        else if (key == Keyboard.Key.Z)
            TryRotate(true);
        else if (key == Keyboard.Key.Down)
        {
            RefreshSoftDrop();
            StepDown();
        }
        else if (key == Keyboard.Key.C)
            HoldPiece();
        else if (key == Keyboard.Key.Space)
            HardDrop();
    }

    private void HandleGameKeyUp(Keyboard.Key key)
    {
        if (key == Keyboard.Key.Down)
            _softDropTimeLeft = 0f;
    }

    private void PauseGame()
    {
        if (!_running || _gameOver || _paused)
            return;

        _paused = true;
        _softDropTimeLeft = 0f;
        _horizontalDirection = 0;
        _horizontalHoldTime = 0f;
        _horizontalRepeatStarted = false;
        _statusLabel.Text = Loc.GetString("blockgame-menu-button-pause");
        UpdateControlButtons();
    }

    private void ResumeGame()
    {
        if (!_running || _gameOver || !_paused)
            return;

        _paused = false;
        _statusLabel.Text = string.Empty;
        UpdateControlButtons();
    }

    private void RefreshSoftDrop()
    {
        _softDropTimeLeft = SoftDropRefreshTime;
    }

    private bool IsFocusedInsideThis()
    {
        var focused = _userInterface.KeyboardFocused;
        while (focused != null)
        {
            if (focused == this)
                return true;

            focused = focused.Parent;
        }

        return false;
    }

    private static bool IsGameFunction(BoundKeyFunction function)
    {
        return IsLeft(function)
               || IsRight(function)
               || IsUp(function)
               || IsDown(function)
               || function == ContentKeyFunctions.Arcade1
               || function == ContentKeyFunctions.Arcade2
               || function == ContentKeyFunctions.Arcade3;
    }

    private static bool IsGameKey(Keyboard.Key key)
    {
        return key == Keyboard.Key.Left
               || key == Keyboard.Key.Right
               || key == Keyboard.Key.Up
               || key == Keyboard.Key.Down
               || key == Keyboard.Key.Space
               || key == Keyboard.Key.C
               || key == Keyboard.Key.Z;
    }

    private static bool IsLeft(BoundKeyFunction function)
    {
        return function == ContentKeyFunctions.ArcadeLeft || function == EngineKeyFunctions.MoveLeft;
    }

    private static bool IsRight(BoundKeyFunction function)
    {
        return function == ContentKeyFunctions.ArcadeRight || function == EngineKeyFunctions.MoveRight;
    }

    private static bool IsUp(BoundKeyFunction function)
    {
        return function == ContentKeyFunctions.ArcadeUp || function == EngineKeyFunctions.MoveUp;
    }

    private static bool IsDown(BoundKeyFunction function)
    {
        return function == ContentKeyFunctions.ArcadeDown || function == EngineKeyFunctions.MoveDown;
    }

    private Piece RandomPiece()
    {
        if (_bag.Count == 0)
            _bag.AddRange(Enum.GetValues<PieceKind>().OrderBy(_ => _random.Next()));

        var kind = _bag[^1];
        _bag.RemoveAt(_bag.Count - 1);
        return Piece.FromKind(kind);
    }

    private void StartHorizontalInput(int direction)
    {
        _horizontalDirection = direction;
        _horizontalHoldTime = 0f;
        _horizontalRepeatStarted = false;
        TryMoveHorizontal(direction);
    }

    private void EndHorizontalInput(int direction)
    {
        if (_horizontalDirection != direction)
            return;

        _horizontalDirection = 0;
        _horizontalHoldTime = 0f;
        _horizontalRepeatStarted = false;
    }

    private void UpdateHorizontalRepeat(float delta)
    {
        _horizontalHoldTime += delta;

        if (!_horizontalRepeatStarted)
        {
            if (_horizontalHoldTime < HorizontalRepeatDelay)
                return;

            TryMoveHorizontal(_horizontalDirection);
            _horizontalHoldTime = 0f;
            _horizontalRepeatStarted = true;
            return;
        }

        if (_horizontalHoldTime < HorizontalRepeatInterval)
            return;

        TryMoveHorizontal(_horizontalDirection);
        _horizontalHoldTime = 0f;
    }

    private static Rotation NextRotation(Rotation rotation, bool inverted)
    {
        return rotation switch
        {
            Rotation.North => inverted ? Rotation.West : Rotation.East,
            Rotation.East => inverted ? Rotation.North : Rotation.South,
            Rotation.South => inverted ? Rotation.East : Rotation.West,
            Rotation.West => inverted ? Rotation.South : Rotation.North,
            _ => Rotation.North
        };
    }

    private enum PieceKind
    {
        I,
        L,
        J,
        S,
        Z,
        T,
        O
    }

    private enum Rotation
    {
        North,
        East,
        South,
        West
    }

    private readonly struct Piece
    {
        private readonly Vector2i[] _offsets;

        public readonly BlockGameBlock.BlockGameBlockColor Color;
        public readonly bool CanSpin;

        private Piece(Vector2i[] offsets, BlockGameBlock.BlockGameBlockColor color, bool canSpin = true)
        {
            _offsets = offsets;
            Color = color;
            CanSpin = canSpin;
        }

        public IEnumerable<Vector2i> Positions(Vector2i center, Rotation rotation)
        {
            foreach (var offset in RotatedOffsets(rotation))
                yield return center + offset;
        }

        public IEnumerable<BlockGameBlock> Blocks(Vector2i center, Rotation rotation, bool ghost = false)
        {
            var color = ghost ? BlockGameBlock.ToGhostBlockColor(Color) : Color;
            foreach (var position in Positions(center, rotation))
                yield return new BlockGameBlock(position, color);
        }

        public BlockGameBlock[] BlocksForPreview()
        {
            var minX = _offsets.Min(offset => offset.X);
            var minY = _offsets.Min(offset => offset.Y);
            return Blocks(new Vector2i(-minX, -minY), Rotation.North).ToArray();
        }

        private Vector2i[] RotatedOffsets(Rotation rotation)
        {
            if (_offsets == null)
                return Array.Empty<Vector2i>();

            var result = (Vector2i[]) _offsets.Clone();
            var count = rotation switch
            {
                Rotation.North => 0,
                Rotation.East => 1,
                Rotation.South => 2,
                Rotation.West => 3,
                _ => 0
            };

            for (var i = 0; i < count; i++)
            {
                for (var j = 0; j < result.Length; j++)
                    result[j] = result[j].Rotate90DegreesAsOffset();
            }

            return result;
        }

        public static Piece FromKind(PieceKind kind)
        {
            return kind switch
            {
                PieceKind.I => new Piece(new[] { new Vector2i(0, -1), new Vector2i(0, 0), new Vector2i(0, 1), new Vector2i(0, 2) }, BlockGameBlock.BlockGameBlockColor.LightBlue),
                PieceKind.L => new Piece(new[] { new Vector2i(0, -1), new Vector2i(0, 0), new Vector2i(0, 1), new Vector2i(1, 1) }, BlockGameBlock.BlockGameBlockColor.Orange),
                PieceKind.J => new Piece(new[] { new Vector2i(0, -1), new Vector2i(0, 0), new Vector2i(-1, 1), new Vector2i(0, 1) }, BlockGameBlock.BlockGameBlockColor.Blue),
                PieceKind.S => new Piece(new[] { new Vector2i(0, -1), new Vector2i(1, -1), new Vector2i(-1, 0), new Vector2i(0, 0) }, BlockGameBlock.BlockGameBlockColor.Green),
                PieceKind.Z => new Piece(new[] { new Vector2i(-1, -1), new Vector2i(0, -1), new Vector2i(0, 0), new Vector2i(1, 0) }, BlockGameBlock.BlockGameBlockColor.Red),
                PieceKind.T => new Piece(new[] { new Vector2i(0, -1), new Vector2i(-1, 0), new Vector2i(0, 0), new Vector2i(1, 0) }, BlockGameBlock.BlockGameBlockColor.Purple),
                PieceKind.O => new Piece(new[] { new Vector2i(0, -1), new Vector2i(1, -1), new Vector2i(0, 0), new Vector2i(1, 0) }, BlockGameBlock.BlockGameBlockColor.Yellow, false),
                _ => new Piece(new[] { Vector2i.Zero }, BlockGameBlock.BlockGameBlockColor.Red)
            };
        }
    }
}

internal sealed class PocketBlockinatorBoard : Control
{
    private const int CellPadding = 1;

    private readonly int _columns;
    private readonly int _rows;
    private readonly BlockGameBlock.BlockGameBlockColor?[,] _cells;

    public PocketBlockinatorBoard(int columns, int rows, Vector2 minSize)
    {
        _columns = columns;
        _rows = rows;
        _cells = new BlockGameBlock.BlockGameBlockColor?[columns, rows];
        MinSize = minSize;
        MouseFilter = MouseFilterMode.Pass;
    }

    public void SetBlocks(IEnumerable<BlockGameBlock> blocks)
    {
        Array.Clear(_cells);

        foreach (var block in blocks)
        {
            var pos = block.Position;
            if (pos.X < 0 || pos.X >= _columns || pos.Y < 0 || pos.Y >= _rows)
                continue;

            _cells[pos.X, pos.Y] = block.GameBlockColor;
        }

        InvalidateArrange();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var cellSize = MathF.Floor(MathF.Min(PixelWidth / _columns, PixelHeight / _rows));
        if (cellSize <= 0)
            return;

        var boardSize = new Vector2(cellSize * _columns, cellSize * _rows);
        var origin = (PixelSize - boardSize) / 2f;

        handle.DrawRect(UIBox2.FromDimensions(origin, boardSize), Color.FromHex("#101722"));
        DrawGrid(handle, origin, cellSize);

        for (var y = 0; y < _rows; y++)
        {
            for (var x = 0; x < _columns; x++)
            {
                if (_cells[x, y] is { } color)
                    DrawBlock(handle, origin, cellSize, x, y, color);
            }
        }
    }

    private void DrawGrid(DrawingHandleScreen handle, Vector2 origin, float cellSize)
    {
        var lineColor = Color.FromHex("#2a3642").WithAlpha(0.7f);
        for (var x = 0; x <= _columns; x++)
        {
            var lineX = origin.X + x * cellSize;
            handle.DrawLine(new Vector2(lineX, origin.Y), new Vector2(lineX, origin.Y + _rows * cellSize), lineColor);
        }

        for (var y = 0; y <= _rows; y++)
        {
            var lineY = origin.Y + y * cellSize;
            handle.DrawLine(new Vector2(origin.X, lineY), new Vector2(origin.X + _columns * cellSize, lineY), lineColor);
        }
    }

    private static void DrawBlock(DrawingHandleScreen handle, Vector2 origin, float cellSize, int x, int y, BlockGameBlock.BlockGameBlockColor blockColor)
    {
        var ghost = IsGhost(blockColor);
        var color = GetBlockColor(blockColor);
        var rect = UIBox2.FromDimensions(
            origin + new Vector2(x * cellSize + CellPadding, y * cellSize + CellPadding),
            new Vector2(cellSize - CellPadding * 2));

        if (ghost)
        {
            handle.DrawRect(rect, color.WithAlpha(0.12f));
            handle.DrawRect(rect, color.WithAlpha(0.55f), false);
            return;
        }

        handle.DrawRect(rect, Color.FromHex("#05070b"));
        handle.DrawRect(Deflate(rect, 1), color);
        handle.DrawRect(new UIBox2(rect.Left + 2, rect.Top + 2, rect.Right - 2, rect.Top + MathF.Max(3, cellSize * 0.32f)), Color.White.WithAlpha(0.22f));
        handle.DrawRect(Deflate(rect, 1), Color.Black.WithAlpha(0.45f), false);
        handle.DrawRect(Deflate(rect, 3), Color.White.WithAlpha(0.12f), false);
    }

    private static UIBox2 Deflate(UIBox2 box, float amount)
    {
        return new UIBox2(box.Left + amount, box.Top + amount, box.Right - amount, box.Bottom - amount);
    }

    private static bool IsGhost(BlockGameBlock.BlockGameBlockColor color)
    {
        return color is BlockGameBlock.BlockGameBlockColor.GhostRed
            or BlockGameBlock.BlockGameBlockColor.GhostOrange
            or BlockGameBlock.BlockGameBlockColor.GhostYellow
            or BlockGameBlock.BlockGameBlockColor.GhostGreen
            or BlockGameBlock.BlockGameBlockColor.GhostBlue
            or BlockGameBlock.BlockGameBlockColor.GhostLightBlue
            or BlockGameBlock.BlockGameBlockColor.GhostPurple;
    }

    private static Color GetBlockColor(BlockGameBlock.BlockGameBlockColor color)
    {
        return color switch
        {
            BlockGameBlock.BlockGameBlockColor.Red or BlockGameBlock.BlockGameBlockColor.GhostRed => Color.FromHex("#e94f5d"),
            BlockGameBlock.BlockGameBlockColor.Orange or BlockGameBlock.BlockGameBlockColor.GhostOrange => Color.FromHex("#f6a03a"),
            BlockGameBlock.BlockGameBlockColor.Yellow or BlockGameBlock.BlockGameBlockColor.GhostYellow => Color.FromHex("#f3cf4c"),
            BlockGameBlock.BlockGameBlockColor.Green or BlockGameBlock.BlockGameBlockColor.GhostGreen => Color.FromHex("#56bf6d"),
            BlockGameBlock.BlockGameBlockColor.Blue or BlockGameBlock.BlockGameBlockColor.GhostBlue => Color.FromHex("#5f82e8"),
            BlockGameBlock.BlockGameBlockColor.LightBlue or BlockGameBlock.BlockGameBlockColor.GhostLightBlue => Color.FromHex("#55c4df"),
            BlockGameBlock.BlockGameBlockColor.Purple or BlockGameBlock.BlockGameBlockColor.GhostPurple => Color.FromHex("#b46ad9"),
            _ => Color.FromHex("#a0a0a0")
        };
    }
}
