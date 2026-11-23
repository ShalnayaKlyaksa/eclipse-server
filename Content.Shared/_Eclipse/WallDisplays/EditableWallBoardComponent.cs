using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.WallDisplays;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class EditableWallBoardComponent : Component
{
    [DataField] public int Width = 1;
    [DataField, AutoNetworkedField] public string Text = "";
    [DataField, AutoNetworkedField] public int Revision;
    [DataField, AutoNetworkedField] public int FontSize = 10;
    public const int MinFontSize = 6;
    public const int MaxFontSize = 24;
    public static bool ValidFontSize(int size) => size is >= MinFontSize and <= MaxFontSize && size % 2 == 0;
    public int CharacterLimit => Math.Clamp(Width, 1, 3) * 1024;
}

[Serializable, NetSerializable]
public enum EditableWallBoardUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class WallBoardState(string text, int revision, int limit, int fontSize = 10) : BoundUserInterfaceState
{
    public string Text = text;
    public int Revision = revision;
    public int Limit = limit;
    public int FontSize = fontSize;
}

[Serializable, NetSerializable]
public sealed class WallBoardSaveMessage(string text, int revision, int fontSize = 10) : BoundUserInterfaceMessage
{
    public string Text = text;
    public int Revision = revision;
    public int FontSize = fontSize;
}
