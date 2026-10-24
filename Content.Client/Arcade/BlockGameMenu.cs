using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Content.Client.Arcade.UI;
using Content.Client.Resources;
using Content.Shared.Arcade;
using Content.Shared.Input;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client.Arcade
{
    public sealed class BlockGameMenu : DefaultWindow
    {
        private static readonly Color OverlayBackgroundColor = new(74, 74, 81, 180);
        private static readonly Color OverlayShadowColor = new(0, 0, 0, 83);

        private readonly PanelContainer _mainPanel;

        private readonly BoxContainer _gameRootContainer;
        private BlockGameGridControl _gameGrid = default!;
        private BlockGamePreviewControl _nextBlockGrid = default!;
        private BlockGamePreviewControl _holdBlockGrid = default!;
        private readonly Label _pointsLabel;
        private readonly Label _levelLabel;
        private readonly Button _pauseButton;

        private readonly PanelContainer _menuRootContainer;
        private readonly Button _unpauseButton;
        private readonly Control _unpauseButtonMargin;
        private readonly Button _newGameButton;
        private readonly Button _scoreBoardButton;

        private readonly PanelContainer _gameOverRootContainer;
        private readonly Label _finalScoreLabel;
        private readonly Button _finalNewGameButton;

        private readonly PanelContainer _highscoresRootContainer;
        private readonly Label _localHighscoresLabel;
        private readonly Label _globalHighscoresLabel;
        private readonly Button _highscoreBackButton;

        private bool _isPlayer = false;
        private bool _gameOver = false;
        private bool _leftHeld;
        private bool _rightHeld;
        private bool _softDropHeld;
        private int _horizontalDirection;
        private float _horizontalHoldTime;
        private bool _horizontalRepeatStarted;

        private const float HorizontalRepeatDelay = 0.22f;
        private const float HorizontalRepeatInterval = 0.09f;

        public event Action<BlockGamePlayerAction>? OnAction;

        public BlockGameMenu()
        {
            Title = Loc.GetString("blockgame-menu-title");

            MinSize = SetSize = new Vector2(430, 515);

            var resourceCache = IoCManager.Resolve<IResourceCache>();
            var backgroundTexture = resourceCache.GetTexture("/Textures/Interface/Nano/button.svg.96dpi.png");

            _mainPanel = new PanelContainer();

            #region Game Menu
            // building the game container
            _gameRootContainer = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                Margin = new Thickness(8)
            };

            _levelLabel = new Label
            {
                Align = Label.AlignMode.Center
            };

            _pointsLabel = new Label
            {
                Align = Label.AlignMode.Center
            };

            var gameBox = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalAlignment = HAlignment.Center
            };
            gameBox.AddChild(SetupGameGrid(backgroundTexture));
            gameBox.AddChild(new Control
            {
                MinSize = new Vector2(12, 1)
            });

            var sideBar = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                MinSize = new Vector2(112, 1)
            };
            sideBar.AddChild(_levelLabel);
            sideBar.AddChild(new Control { MinSize = new Vector2(1, 4) });
            sideBar.AddChild(_pointsLabel);
            sideBar.AddChild(new Control { MinSize = new Vector2(1, 14) });
            sideBar.AddChild(SetupNextBox(backgroundTexture));
            sideBar.AddChild(new Control { MinSize = new Vector2(1, 12) });
            sideBar.AddChild(SetupHoldBox(backgroundTexture));
            gameBox.AddChild(sideBar);

            _gameRootContainer.AddChild(gameBox);

            _gameRootContainer.AddChild(new Control
            {
                MinSize = new Vector2(1, 10)
            });

            _pauseButton = new Button
            {
                Text = Loc.GetString("blockgame-menu-button-pause"),
                TextAlign = Label.AlignMode.Center
            };
            _pauseButton.OnPressed += (e) => TryPause();
            _gameRootContainer.AddChild(_pauseButton);
            #endregion

            _mainPanel.AddChild(_gameRootContainer);

            #region Pause Menu
            var pauseRootBack = new StyleBoxTexture
            {
                Texture = backgroundTexture,
                Modulate = OverlayShadowColor
            };
            pauseRootBack.SetPatchMargin(StyleBox.Margin.All, 10);
            _menuRootContainer = new PanelContainer
            {
                PanelOverride = pauseRootBack,
                VerticalAlignment = VAlignment.Center,
                HorizontalAlignment = HAlignment.Center
            };

            var pauseInnerBack = new StyleBoxTexture
            {
                Texture = backgroundTexture,
                Modulate = OverlayBackgroundColor
            };
            pauseInnerBack.SetPatchMargin(StyleBox.Margin.All, 10);
            var pauseMenuInnerPanel = new PanelContainer
            {
                PanelOverride = pauseInnerBack,
                VerticalAlignment = VAlignment.Center,
                HorizontalAlignment = HAlignment.Center
            };

            _menuRootContainer.AddChild(pauseMenuInnerPanel);

            var pauseMenuContainer = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center
            };

            _newGameButton = new Button
            {
                Text = Loc.GetString("blockgame-menu-button-new-game"),
                TextAlign = Label.AlignMode.Center
            };
            _newGameButton.OnPressed += (e) =>
            {
                OnAction?.Invoke(BlockGamePlayerAction.NewGame);
            };
            pauseMenuContainer.AddChild(_newGameButton);
            pauseMenuContainer.AddChild(new Control { MinSize = new Vector2(1, 10) });

            _scoreBoardButton = new Button
            {
                Text = Loc.GetString("blockgame-menu-button-scoreboard"),
                TextAlign = Label.AlignMode.Center
            };
            _scoreBoardButton.OnPressed += (e) =>
            {
                OnAction?.Invoke(BlockGamePlayerAction.ShowHighscores);
            };
            pauseMenuContainer.AddChild(_scoreBoardButton);
            _unpauseButtonMargin = new Control { MinSize = new Vector2(1, 10), Visible = false };
            pauseMenuContainer.AddChild(_unpauseButtonMargin);

            _unpauseButton = new Button
            {
                Text = Loc.GetString("blockgame-menu-button-unpause"),
                TextAlign = Label.AlignMode.Center,
                Visible = false
            };
            _unpauseButton.OnPressed += (e) =>
            {
                OnAction?.Invoke(BlockGamePlayerAction.Unpause);
            };
            pauseMenuContainer.AddChild(_unpauseButton);

            pauseMenuInnerPanel.AddChild(pauseMenuContainer);
            #endregion

            #region Gameover Screen
            var gameOverRootBack = new StyleBoxTexture
            {
                Texture = backgroundTexture,
                Modulate = OverlayShadowColor
            };
            gameOverRootBack.SetPatchMargin(StyleBox.Margin.All, 10);
            _gameOverRootContainer = new PanelContainer
            {
                PanelOverride = gameOverRootBack,
                VerticalAlignment = VAlignment.Center,
                HorizontalAlignment = HAlignment.Center
            };

            var gameOverInnerBack = new StyleBoxTexture
            {
                Texture = backgroundTexture,
                Modulate = OverlayBackgroundColor
            };
            gameOverInnerBack.SetPatchMargin(StyleBox.Margin.All, 10);
            var gameOverMenuInnerPanel = new PanelContainer
            {
                PanelOverride = gameOverInnerBack,
                VerticalAlignment = VAlignment.Center,
                HorizontalAlignment = HAlignment.Center
            };

            _gameOverRootContainer.AddChild(gameOverMenuInnerPanel);

            var gameOverMenuContainer = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center
            };

            gameOverMenuContainer.AddChild(new Label { Text = Loc.GetString("blockgame-menu-msg-game-over"), Align = Label.AlignMode.Center });
            gameOverMenuContainer.AddChild(new Control { MinSize = new Vector2(1, 10) });


            _finalScoreLabel = new Label { Align = Label.AlignMode.Center };
            gameOverMenuContainer.AddChild(_finalScoreLabel);
            gameOverMenuContainer.AddChild(new Control { MinSize = new Vector2(1, 10) });

            _finalNewGameButton = new Button
            {
                Text = Loc.GetString("blockgame-menu-button-new-game"),
                TextAlign = Label.AlignMode.Center
            };
            _finalNewGameButton.OnPressed += (e) =>
            {
                OnAction?.Invoke(BlockGamePlayerAction.NewGame);
            };
            gameOverMenuContainer.AddChild(_finalNewGameButton);

            gameOverMenuInnerPanel.AddChild(gameOverMenuContainer);
            #endregion

            #region High Score Screen
            var rootBack = new StyleBoxTexture
            {
                Texture = backgroundTexture,
                Modulate = OverlayShadowColor
            };
            rootBack.SetPatchMargin(StyleBox.Margin.All, 10);
            _highscoresRootContainer = new PanelContainer
            {
                PanelOverride = rootBack,
                VerticalAlignment = VAlignment.Center,
                HorizontalAlignment = HAlignment.Center
            };

            var c = new Color(OverlayBackgroundColor.R, OverlayBackgroundColor.G, OverlayBackgroundColor.B, 220);
            var innerBack = new StyleBoxTexture
            {
                Texture = backgroundTexture,
                Modulate = c
            };
            innerBack.SetPatchMargin(StyleBox.Margin.All, 10);
            var menuInnerPanel = new PanelContainer
            {
                PanelOverride = innerBack,
                VerticalAlignment = VAlignment.Center,
                HorizontalAlignment = HAlignment.Center
            };

            _highscoresRootContainer.AddChild(menuInnerPanel);

            var menuContainer = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center
            };

            menuContainer.AddChild(new Label { Text = Loc.GetString("blockgame-menu-label-highscores") });
            menuContainer.AddChild(new Control { MinSize = new Vector2(1, 10) });

            var highScoreBox = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal
            };

            _localHighscoresLabel = new Label
            {
                Align = Label.AlignMode.Center
            };
            highScoreBox.AddChild(_localHighscoresLabel);
            highScoreBox.AddChild(new Control { MinSize = new Vector2(40, 1) });
            _globalHighscoresLabel = new Label
            {
                Align = Label.AlignMode.Center
            };
            highScoreBox.AddChild(_globalHighscoresLabel);
            menuContainer.AddChild(highScoreBox);
            menuContainer.AddChild(new Control { MinSize = new Vector2(1, 10) });
            _highscoreBackButton = new Button
            {
                Text = Loc.GetString("blockgame-menu-button-back"),
                TextAlign = Label.AlignMode.Center
            };
            _highscoreBackButton.OnPressed += (e) =>
            {
                OnAction?.Invoke(BlockGamePlayerAction.Pause);
            };
            menuContainer.AddChild(_highscoreBackButton);

            menuInnerPanel.AddChild(menuContainer);
            #endregion

            ContentsContainer.AddChild(_mainPanel);

            CanKeyboardFocus = true;
        }

        public void SetUsability(bool isPlayer)
        {
            _isPlayer = isPlayer;
            UpdateUsability();
        }

        private void UpdateUsability()
        {
            _pauseButton.Disabled = !_isPlayer;
            _newGameButton.Disabled = !_isPlayer;
            _scoreBoardButton.Disabled = !_isPlayer;
            _unpauseButton.Disabled = !_isPlayer;
            _finalNewGameButton.Disabled = !_isPlayer;
            _highscoreBackButton.Disabled = !_isPlayer;
        }

        private Control SetupGameGrid(Texture panelTex)
        {
            _gameGrid = new BlockGameGridControl();

            var back = new StyleBoxTexture
            {
                Texture = panelTex,
                Modulate = Color.FromHex("#202631"),
            };
            back.SetPatchMargin(StyleBox.Margin.All, 10);

            var gamePanel = new PanelContainer
            {
                PanelOverride = back,
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Top
            };
            var backgroundPanel = new PanelContainer
            {
                PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#101722") },
                Margin = new Thickness(6)
            };
            backgroundPanel.AddChild(_gameGrid);
            gamePanel.AddChild(backgroundPanel);
            return gamePanel;
        }

        private Control SetupNextBox(Texture panelTex)
        {
            var previewBack = new StyleBoxTexture
            {
                Texture = panelTex,
                Modulate = Color.FromHex("#202631")
            };
            previewBack.SetPatchMargin(StyleBox.Margin.All, 10);

            var box = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                HorizontalExpand = true
            };
            box.AddChild(new Label { Text = Loc.GetString("blockgame-menu-label-next"), Align = Label.AlignMode.Center });
            box.AddChild(new Control { MinSize = new Vector2(1, 4) });

            var nextBlockPanel = new PanelContainer
            {
                PanelOverride = previewBack,
                MinSize = new Vector2(100, 100),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Top
            };
            var nextCenterContainer = new CenterContainer();
            _nextBlockGrid = new BlockGamePreviewControl();
            nextCenterContainer.AddChild(_nextBlockGrid);
            nextBlockPanel.AddChild(nextCenterContainer);
            box.AddChild(nextBlockPanel);

            return box;
        }

        private Control SetupHoldBox(Texture panelTex)
        {
            var previewBack = new StyleBoxTexture
            {
                Texture = panelTex,
                Modulate = Color.FromHex("#202631")
            };
            previewBack.SetPatchMargin(StyleBox.Margin.All, 10);

            var box = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                HorizontalExpand = true
            };
            box.AddChild(new Label { Text = Loc.GetString("blockgame-menu-label-hold"), Align = Label.AlignMode.Center });
            box.AddChild(new Control { MinSize = new Vector2(1, 4) });

            var holdBlockPanel = new PanelContainer
            {
                PanelOverride = previewBack,
                MinSize = new Vector2(100, 100),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Top
            };
            var holdCenterContainer = new CenterContainer();
            _holdBlockGrid = new BlockGamePreviewControl();
            holdCenterContainer.AddChild(_holdBlockGrid);
            holdBlockPanel.AddChild(holdCenterContainer);
            box.AddChild(holdBlockPanel);

            return box;
        }

        protected override void KeyboardFocusExited()
        {
            if (!IsOpen)
                return;
            if (_gameOver)
                return;
            TryPause();
        }

        private void TryPause()
        {
            ResetInputState();
            OnAction?.Invoke(BlockGamePlayerAction.Pause);
        }

        public void SetStarted()
        {
            _gameOver = false;
            _unpauseButton.Visible = true;
            _unpauseButtonMargin.Visible = true;
        }

        public void SetScreen(BlockGameMessages.BlockGameScreen screen)
        {
            if (_gameOver)
                return;

            switch (screen)
            {
                case BlockGameMessages.BlockGameScreen.Game:
                    GrabKeyboardFocus();
                    CloseMenus();
                    _pauseButton.Disabled = !_isPlayer;
                    break;
                case BlockGameMessages.BlockGameScreen.Pause:
                    //ReleaseKeyboardFocus();
                    ResetInputState();
                    CloseMenus();
                    _mainPanel.AddChild(_menuRootContainer);
                    _pauseButton.Disabled = true;
                    break;
                case BlockGameMessages.BlockGameScreen.Gameover:
                    _gameOver = true;
                    ResetInputState();
                    _pauseButton.Disabled = true;
                    //ReleaseKeyboardFocus();
                    CloseMenus();
                    _mainPanel.AddChild(_gameOverRootContainer);
                    break;
                case BlockGameMessages.BlockGameScreen.Highscores:
                    //ReleaseKeyboardFocus();
                    CloseMenus();
                    _mainPanel.AddChild(_highscoresRootContainer);
                    break;
            }
        }

        private void CloseMenus()
        {
            if (_mainPanel.Children.Contains(_menuRootContainer))
                _mainPanel.RemoveChild(_menuRootContainer);
            if (_mainPanel.Children.Contains(_gameOverRootContainer))
                _mainPanel.RemoveChild(_gameOverRootContainer);
            if (_mainPanel.Children.Contains(_highscoresRootContainer))
                _mainPanel.RemoveChild(_highscoresRootContainer);
        }

        public void SetGameoverInfo(int amount, int? localPlacement, int? globalPlacement)
        {
            var globalPlacementText = globalPlacement == null ? "-" : $"#{globalPlacement}";
            var localPlacementText = localPlacement == null ? "-" : $"#{localPlacement}";
            _finalScoreLabel.Text =
                Loc.GetString("blockgame-menu-gameover-info",
                    ("global", globalPlacementText),
                    ("local", localPlacementText),
                    ("points", amount));
        }

        public void UpdatePoints(int points)
        {
            _pointsLabel.Text = Loc.GetString("blockgame-menu-label-points", ("points", points));
        }

        public void UpdateLevel(int level)
        {
            _levelLabel.Text = Loc.GetString("blockgame-menu-label-level", ("level", level + 1));
        }

        public void UpdateHighscores(List<BlockGameMessages.HighScoreEntry> localHighscores,
            List<BlockGameMessages.HighScoreEntry> globalHighscores)
        {
            var localHighscoreText = new StringBuilder(Loc.GetString("blockgame-menu-text-station") + "\n");
            var globalHighscoreText = new StringBuilder(Loc.GetString("blockgame-menu-text-nanotrasen") + "\n");

            for (var i = 0; i < 5; i++)
            {
                localHighscoreText.AppendLine(localHighscores.Count > i
                    ? $"#{i + 1}: {localHighscores[i].Name} - {localHighscores[i].Score}"
                    : $"#{i + 1}: ??? - 0");

                globalHighscoreText.AppendLine(globalHighscores.Count > i
                    ? $"#{i + 1}: {globalHighscores[i].Name} - {globalHighscores[i].Score}"
                    : $"#{i + 1}: ??? - 0");
            }

            _localHighscoresLabel.Text = localHighscoreText.ToString();
            _globalHighscoresLabel.Text = globalHighscoreText.ToString();
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);

            if (!_isPlayer || args.Handled)
                return;

            if (args.Function == ContentKeyFunctions.ArcadeLeft)
            {
                StartHorizontalInput(-1);
                args.Handle();
            }
            else if (args.Function == ContentKeyFunctions.ArcadeRight)
            {
                StartHorizontalInput(1);
                args.Handle();
            }
            else if (args.Function == ContentKeyFunctions.ArcadeUp)
                OnAction?.Invoke(BlockGamePlayerAction.Rotate);
            else if (args.Function == ContentKeyFunctions.Arcade3)
                OnAction?.Invoke(BlockGamePlayerAction.CounterRotate);
            else if (args.Function == ContentKeyFunctions.ArcadeDown)
            {
                if (!_softDropHeld)
                {
                    _softDropHeld = true;
                    OnAction?.Invoke(BlockGamePlayerAction.SoftdropStart);
                }

                args.Handle();
            }
            else if (args.Function == ContentKeyFunctions.Arcade2)
                OnAction?.Invoke(BlockGamePlayerAction.Hold);
            else if (args.Function == ContentKeyFunctions.Arcade1)
                OnAction?.Invoke(BlockGamePlayerAction.Harddrop);
        }

        protected override void KeyBindUp(GUIBoundKeyEventArgs args)
        {
            base.KeyBindUp(args);

            if (!_isPlayer || args.Handled)
                return;

            if (args.Function == ContentKeyFunctions.ArcadeLeft)
            {
                EndHorizontalInput(-1);
                args.Handle();
            }
            else if (args.Function == ContentKeyFunctions.ArcadeRight)
            {
                EndHorizontalInput(1);
                args.Handle();
            }
            else if (args.Function == ContentKeyFunctions.ArcadeDown)
            {
                _softDropHeld = false;
                OnAction?.Invoke(BlockGamePlayerAction.SoftdropEnd);
                args.Handle();
            }
        }

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);

            if (!_isPlayer || _horizontalDirection == 0)
                return;

            _horizontalHoldTime += args.DeltaSeconds;

            if (!_horizontalRepeatStarted)
            {
                if (_horizontalHoldTime < HorizontalRepeatDelay)
                    return;

                SendHorizontalStep(_horizontalDirection);
                _horizontalHoldTime = 0f;
                _horizontalRepeatStarted = true;
                return;
            }

            if (_horizontalHoldTime < HorizontalRepeatInterval)
                return;

            SendHorizontalStep(_horizontalDirection);
            _horizontalHoldTime = 0f;
        }

        private void StartHorizontalInput(int direction)
        {
            if (direction < 0)
            {
                if (_leftHeld && _horizontalDirection == direction)
                    return;

                _leftHeld = true;
            }
            else
            {
                if (_rightHeld && _horizontalDirection == direction)
                    return;

                _rightHeld = true;
            }

            _horizontalDirection = direction;
            _horizontalHoldTime = 0f;
            _horizontalRepeatStarted = false;
            SendHorizontalStep(direction);
        }

        private void EndHorizontalInput(int direction)
        {
            if (direction < 0)
                _leftHeld = false;
            else
                _rightHeld = false;

            if (_horizontalDirection != direction)
                return;

            if (_leftHeld)
                _horizontalDirection = -1;
            else if (_rightHeld)
                _horizontalDirection = 1;
            else
                _horizontalDirection = 0;

            _horizontalHoldTime = 0f;
            _horizontalRepeatStarted = false;
        }

        private void SendHorizontalStep(int direction)
        {
            OnAction?.Invoke(direction < 0
                ? BlockGamePlayerAction.StartLeft
                : BlockGamePlayerAction.StartRight);
        }

        private void ResetInputState()
        {
            _leftHeld = false;
            _rightHeld = false;
            _softDropHeld = false;
            _horizontalDirection = 0;
            _horizontalHoldTime = 0f;
            _horizontalRepeatStarted = false;
        }

        public void UpdateNextBlock(BlockGameBlock[] blocks)
        {
            _nextBlockGrid.SetBlocks(blocks);
        }

        public void UpdateHeldBlock(BlockGameBlock[] blocks)
        {
            _holdBlockGrid.SetBlocks(blocks);
        }

        public void UpdateBlocks(BlockGameBlock[] blocks)
        {
            _gameGrid.SetBlocks(blocks);
        }
    }

    internal abstract class BlockGameBoardControl : Control
    {
        protected const int CellPadding = 1;

        private readonly int _columns;
        private readonly int _rows;
        private readonly BlockGameBlock.BlockGameBlockColor?[,] _cells;

        protected BlockGameBoardControl(int columns, int rows, Vector2 minSize)
        {
            _columns = columns;
            _rows = rows;
            _cells = new BlockGameBlock.BlockGameBlockColor?[columns, rows];
            MinSize = minSize;
            MouseFilter = MouseFilterMode.Pass;
        }

        public void SetBlocks(BlockGameBlock[] blocks)
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
            var boardBox = UIBox2.FromDimensions(origin, boardSize);

            handle.DrawRect(boardBox, Color.FromHex("#101722"));

            DrawGrid(handle, origin, cellSize);

            for (var y = 0; y < _rows; y++)
            {
                for (var x = 0; x < _columns; x++)
                {
                    var color = _cells[x, y];
                    if (color == null)
                        continue;

                    DrawBlock(handle, origin, cellSize, x, y, color.Value);
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

        private static void DrawBlock(
            DrawingHandleScreen handle,
            Vector2 origin,
            float cellSize,
            int x,
            int y,
            BlockGameBlock.BlockGameBlockColor blockColor)
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

    internal sealed class BlockGameGridControl : BlockGameBoardControl
    {
        public BlockGameGridControl() : base(10, 20, new Vector2(210, 420))
        {
        }
    }

    internal sealed class BlockGamePreviewControl : BlockGameBoardControl
    {
        public BlockGamePreviewControl() : base(4, 4, new Vector2(76, 76))
        {
        }
    }
}
