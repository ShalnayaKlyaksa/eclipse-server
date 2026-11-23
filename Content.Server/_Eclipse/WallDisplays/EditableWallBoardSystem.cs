using System.Linq;
using Content.Shared._Eclipse.WallDisplays;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;

namespace Content.Server._Eclipse.WallDisplays;

public sealed partial class EditableWallBoardSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ISharedAdminLogManager _logs = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<EditableWallBoardComponent, BeforeActivatableUIOpenEvent>((uid, comp, _) => UpdateUi(uid, comp));
        SubscribeLocalEvent<EditableWallBoardComponent, WallBoardSaveMessage>(OnSave);
        SubscribeLocalEvent<EditableWallBoardComponent, ExaminedEvent>((uid, comp, args) => args.PushMarkup(Loc.GetString("wall-board-examine")));
    }

    private void OnSave(EntityUid uid, EditableWallBoardComponent board, WallBoardSaveMessage message)
    {
        if (!EditableWallBoardUiKey.Key.Equals(message.UiKey)) return;
        if (!TrySetText(uid, board, message.Text, message.Revision, message.FontSize))
            _popup.PopupEntity(Loc.GetString("wall-board-save-rejected"), uid, message.Actor);
        else
            _logs.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(message.Actor):player} edited {ToPrettyString(uid):entity}: {board.Text}");
        UpdateUi(uid, board);
    }

    public bool TrySetText(EntityUid uid, EditableWallBoardComponent board, string? text, int revision, int? fontSize = null)
    {
        var size = fontSize ?? board.FontSize;
        if (text == null || text.Length > board.CharacterLimit || revision != board.Revision ||
            !EditableWallBoardComponent.ValidFontSize(size) ||
            text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) return false;
        board.Text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
        board.FontSize = size;
        board.Revision++;
        Dirty(uid, board);
        return true;
    }

    private void UpdateUi(EntityUid uid, EditableWallBoardComponent board) =>
        _ui.SetUiState(uid, EditableWallBoardUiKey.Key, new WallBoardState(board.Text, board.Revision, board.CharacterLimit, board.FontSize));
}
