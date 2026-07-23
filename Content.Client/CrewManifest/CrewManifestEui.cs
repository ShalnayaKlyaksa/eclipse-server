using Content.Client.Eui;
using Content.Shared.CrewManifest;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client.CrewManifest;

[UsedImplicitly]
public sealed class CrewManifestEui : BaseEui
{
    /// <summary>
    /// When set (by the lobby's manifest tab), manifest state is delivered here and rendered inline instead of
    /// opening the popup window. Cleared by the lobby as soon as its manifest tab is left, so in-game manifest
    /// requests keep their normal window. Static because the client creates a fresh EUI per request.
    /// </summary>
    public static Action<string, CrewManifestEntries?>? LobbyManifestSink;

    private readonly CrewManifestUi _window;

    public CrewManifestEui()
    {
        _window = new();

        _window.OnClose += () =>
        {
            SendMessage(new CloseEuiMessage());
        };
    }

    private bool Embedded => LobbyManifestSink != null;

    public override void Opened()
    {
        base.Opened();

        if (!Embedded)
            _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();

        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        base.HandleState(state);

        if (state is not CrewManifestEuiState cast)
        {
            return;
        }

        if (LobbyManifestSink is { } sink)
        {
            sink(cast.StationName, cast.Entries);
            return;
        }

        _window.Populate(cast.StationName, cast.Entries);
    }
}
