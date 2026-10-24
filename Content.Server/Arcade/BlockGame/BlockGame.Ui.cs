using Content.Shared.Arcade;
using System.Linq;
using Robust.Shared.Player;

namespace Content.Server.Arcade.BlockGame;

public sealed partial class BlockGame
{
    /// <summary>
    /// Whether the down button is pressed.
    /// Speeds up how quickly the active piece falls if true.
    /// </summary>
    private bool _softDropPressed = false;

    /// <summary>
    /// Prevents a held soft drop from immediately accelerating the piece spawned after a lock.
    /// </summary>
    private bool _softDropBlockedUntilRelease = false;

    private bool SoftDropActive => _softDropPressed && !_softDropBlockedUntilRelease;


    /// <summary>
    /// Handles user input.
    /// </summary>
    /// <param name="action">The action to current player has prompted.</param>
    public void ProcessInput(BlockGamePlayerAction action)
    {
        if (_running)
        {
            switch (action)
            {
                case BlockGamePlayerAction.StartLeft:
                    TryMoveHorizontal(-1);
                    break;
                case BlockGamePlayerAction.StartRight:
                    TryMoveHorizontal(1);
                    break;
                case BlockGamePlayerAction.Rotate:
                    TrySetRotation(Next(_currentRotation, false));
                    break;
                case BlockGamePlayerAction.CounterRotate:
                    TrySetRotation(Next(_currentRotation, true));
                    break;
                case BlockGamePlayerAction.SoftdropStart:
                    _softDropPressed = true;
                    if (_softDropBlockedUntilRelease)
                        break;

                    if (_accumulatedFieldFrameTime > Speed)
                        _accumulatedFieldFrameTime = Speed;
                    AddPoints(1);
                    InternalFieldTick();
                    break;
                case BlockGamePlayerAction.Harddrop:
                    PerformHarddrop();
                    break;
                case BlockGamePlayerAction.Hold:
                    HoldPiece();
                    break;
            }
        }

        switch (action)
        {
            case BlockGamePlayerAction.EndLeft:
                break;
            case BlockGamePlayerAction.EndRight:
                break;
            case BlockGamePlayerAction.SoftdropEnd:
                _softDropPressed = false;
                _softDropBlockedUntilRelease = false;
                break;
            case BlockGamePlayerAction.Pause:
                _running = false;
                SendMessage(new BlockGameMessages.BlockGameSetScreenMessage(BlockGameMessages.BlockGameScreen.Pause, Started));
                break;
            case BlockGamePlayerAction.Unpause:
                if (!_gameOver && Started)
                {
                    _running = true;
                    SendMessage(new BlockGameMessages.BlockGameSetScreenMessage(BlockGameMessages.BlockGameScreen.Game));
                }
                break;
            case BlockGamePlayerAction.ShowHighscores:
                _running = false;
                SendMessage(new BlockGameMessages.BlockGameSetScreenMessage(BlockGameMessages.BlockGameScreen.Highscores, Started));
                break;
        }
    }

    private bool TryMoveHorizontal(int offset, bool updateUi = true)
    {
        if (!CurrentPiece.Positions(_currentPiecePosition.AddToX(offset), _currentRotation)
                .All(MoveCheck))
            return false;

        _currentPiecePosition = _currentPiecePosition.AddToX(offset);

        if (updateUi)
            UpdateFieldUI();

        return true;
    }

    /// <summary>
    /// Handles sending a message to all players/spectators.
    /// </summary>
    /// <param name="message">The message to broadcase to all players/spectators.</param>
    private void SendMessage(BoundUserInterfaceMessage message)
    {
        _uiSystem.ServerSendUiMessage(_owner, BlockGameUiKey.Key, message);
    }

    /// <summary>
    /// Handles sending a message to a specific player/spectator.
    /// </summary>
    /// <param name="message">The message to send to a specific player/spectator.</param>
    /// <param name="actor">The target recipient.</param>
    private void SendMessage(BoundUserInterfaceMessage message, EntityUid actor)
    {
        _uiSystem.ServerSendUiMessage(_owner, BlockGameUiKey.Key, message, actor);
    }

    /// <summary>
    /// Handles sending the current state of the game to a player that has just opened the UI.
    /// </summary>
    /// <param name="actor">The target recipient.</param>
    public void UpdateNewPlayerUI(EntityUid actor)
    {
        if (_gameOver)
        {
            SendMessage(new BlockGameMessages.BlockGameGameOverScreenMessage(Points, _highScorePlacement?.LocalPlacement, _highScorePlacement?.GlobalPlacement), actor);
            return;
        }

        if (Paused)
            SendMessage(new BlockGameMessages.BlockGameSetScreenMessage(BlockGameMessages.BlockGameScreen.Pause, Started), actor);
        else
            SendMessage(new BlockGameMessages.BlockGameSetScreenMessage(BlockGameMessages.BlockGameScreen.Game, Started), actor);

        FullUpdate(actor);
    }

    /// <summary>
    /// Handles broadcasting the full player-visible game state to everyone who can see the game.
    /// </summary>
    private void FullUpdate()
    {
        UpdateFieldUI();
        SendHoldPieceUpdate();
        SendNextPieceUpdate();
        SendLevelUpdate();
        SendPointsUpdate();
        SendHighscoreUpdate();
    }

    /// <summary>
    /// Handles broadcasting the full player-visible game state to a specific player/spectator.
    /// </summary>
    /// <param name="session">The target recipient.</param>
    private void FullUpdate(EntityUid actor)
    {
        UpdateFieldUI(actor);
        SendNextPieceUpdate(actor);
        SendHoldPieceUpdate(actor);
        SendLevelUpdate(actor);
        SendPointsUpdate(actor);
        SendHighscoreUpdate(actor);
    }

    /// <summary>
    /// Handles broadcasting the current location of all of the blocks in the playfield + the active piece to all spectators.
    /// </summary>
    public void UpdateFieldUI()
    {
        if (!Started)
            return;

        var computedField = ComputeField();
        SendMessage(new BlockGameMessages.BlockGameVisualUpdateMessage(computedField.ToArray(), BlockGameMessages.BlockGameVisualType.GameField));
    }

    /// <summary>
    /// Handles broadcasting the current location of all of the blocks in the playfield + the active piece to a specific player/spectator.
    /// </summary>
    public void UpdateFieldUI(EntityUid actor)
    {
        if (!Started)
            return;

        var computedField = ComputeField();
        SendMessage(new BlockGameMessages.BlockGameVisualUpdateMessage(computedField.ToArray(), BlockGameMessages.BlockGameVisualType.GameField), actor);
    }

    /// <summary>
    /// Generates the set of blocks to send to viewers.
    /// </summary>
    public List<BlockGameBlock> ComputeField()
    {
        var result = new List<BlockGameBlock>();
        result.AddRange(_field);
        result.AddRange(CurrentPiece.Blocks(_currentPiecePosition, _currentRotation));

        var dropGhostPosition = _currentPiecePosition;
        while (CurrentPiece.Positions(dropGhostPosition.AddToY(1), _currentRotation)
                .All(DropCheck))
        {
            dropGhostPosition = dropGhostPosition.AddToY(1);
        }

        if (dropGhostPosition != _currentPiecePosition)
        {
            var blox = CurrentPiece.Blocks(dropGhostPosition, _currentRotation);
            for (var i = 0; i < blox.Length; i++)
            {
                result.Add(new BlockGameBlock(blox[i].Position, BlockGameBlock.ToGhostBlockColor(blox[i].GameBlockColor)));
            }
        }
        return result;
    }

    /// <summary>
    /// Broadcasts the state of the next queued piece to all viewers.
    /// </summary>
    private void SendNextPieceUpdate()
    {
        SendMessage(new BlockGameMessages.BlockGameVisualUpdateMessage(NextPiece.BlocksForPreview(), BlockGameMessages.BlockGameVisualType.NextBlock));
    }

    /// <summary>
    /// Broadcasts the state of the next queued piece to a specific viewer.
    /// </summary>
    private void SendNextPieceUpdate(EntityUid actor)
    {
        SendMessage(new BlockGameMessages.BlockGameVisualUpdateMessage(NextPiece.BlocksForPreview(), BlockGameMessages.BlockGameVisualType.NextBlock), actor);
    }

    /// <summary>
    /// Broadcasts the state of the currently held piece to all viewers.
    /// </summary>
    private void SendHoldPieceUpdate()
    {
        if (HeldPiece.HasValue)
            SendMessage(new BlockGameMessages.BlockGameVisualUpdateMessage(HeldPiece.Value.BlocksForPreview(), BlockGameMessages.BlockGameVisualType.HoldBlock));
        else
            SendMessage(new BlockGameMessages.BlockGameVisualUpdateMessage(Array.Empty<BlockGameBlock>(), BlockGameMessages.BlockGameVisualType.HoldBlock));
    }

    /// <summary>
    /// Broadcasts the state of the currently held piece to a specific viewer.
    /// </summary>
    private void SendHoldPieceUpdate(EntityUid actor)
    {
        if (HeldPiece.HasValue)
            SendMessage(new BlockGameMessages.BlockGameVisualUpdateMessage(HeldPiece.Value.BlocksForPreview(), BlockGameMessages.BlockGameVisualType.HoldBlock), actor);
        else
            SendMessage(new BlockGameMessages.BlockGameVisualUpdateMessage(Array.Empty<BlockGameBlock>(), BlockGameMessages.BlockGameVisualType.HoldBlock), actor);
    }

    /// <summary>
    /// Broadcasts the current game level to all viewers.
    /// </summary>
    private void SendLevelUpdate()
    {
        SendMessage(new BlockGameMessages.BlockGameLevelUpdateMessage(Level));
    }

    /// <summary>
    /// Broadcasts the current game level to a specific viewer.
    /// </summary>
    private void SendLevelUpdate(EntityUid actor)
    {
        SendMessage(new BlockGameMessages.BlockGameLevelUpdateMessage(Level), actor);
    }

    /// <summary>
    /// Broadcasts the current game score to all viewers.
    /// </summary>
    private void SendPointsUpdate()
    {
        SendMessage(new BlockGameMessages.BlockGameScoreUpdateMessage(Points));
    }

    /// <summary>
    /// Broadcasts the current game score to a specific viewer.
    /// </summary>
    private void SendPointsUpdate(EntityUid actor)
    {
        SendMessage(new BlockGameMessages.BlockGameScoreUpdateMessage(Points), actor);
    }

    /// <summary>
    /// Broadcasts the current game high score positions to all viewers.
    /// </summary>
    private void SendHighscoreUpdate()
    {
        SendMessage(new BlockGameMessages.BlockGameHighScoreUpdateMessage(_arcadeSystem.GetLocalHighscores(), _arcadeSystem.GetGlobalHighscores()));
    }

    /// <summary>
    /// Broadcasts the current game high score positions to a specific viewer.
    /// </summary>
    private void SendHighscoreUpdate(EntityUid actor)
    {
        SendMessage(new BlockGameMessages.BlockGameHighScoreUpdateMessage(_arcadeSystem.GetLocalHighscores(), _arcadeSystem.GetGlobalHighscores()), actor);
    }
}
