using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Content.Client._Eclipse.Engine.UI;
using Robust.Client;
using Robust.Client.UserInterface;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Log;

namespace Content.Client._Eclipse.Engine;

/// <summary>
/// Checks the Eclipse engine version before connecting and downloads / relaunches when needed.
/// </summary>
public sealed class EclipseEngineConnectService
{
    private static readonly Regex IPv6Regex = new(@"\[(.*:.*:.*)](?::(\d+))?");

    [Dependency] private readonly IGameController _gameController = default!;
    [Dependency] private readonly IUserInterfaceManager _userInterfaceManager = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    private ISawmill _sawmill = default!;
    private EclipseEngineDownloadDialog? _dialog;

    public void Initialize()
    {
        _sawmill = _logManager.GetSawmill("eclipse.engine");
    }

    /// <summary>
    /// Returns <see langword="true"/> if the caller should proceed with <paramref name="connect"/>.
    /// Returns <see langword="false"/> if the client is shutting down or handing off to a new engine process.
    /// </summary>
    public async Task<bool> TryConnectAsync(string host, ushort port, Action connect)
    {
        if (!EclipseEngineApiBridge.IsAvailable)
        {
            _sawmill.Warning("Eclipse engine bootstrap API is missing from Robust.Client; connecting on stock engine");
            connect();
            return true;
        }

        var requiredVersion = ContentEclipseEngineVersion.RequiredVersion;
        _sawmill.Info(
            "Eclipse server {Host}:{Port} requires engine {Version}; running from installed engine: {Installed}",
            host,
            port,
            requiredVersion,
            EclipseEngineApiBridge.IsRunningFromInstalledEngine(requiredVersion));

        if (EclipseEngineApiBridge.IsRunningFromInstalledEngine(requiredVersion))
        {
            connect();
            return true;
        }

        if (EclipseEngineApiBridge.GetInstalledEngineVersion(requiredVersion) != null)
        {
            RelaunchEngine(requiredVersion, host, port);
            return false;
        }

        try
        {
            await DownloadEngineAsync(requiredVersion);
            RelaunchEngine(requiredVersion, host, port);
            return false;
        }
        catch (OperationCanceledException)
        {
            _sawmill.Info("Engine download cancelled by user");
            return false;
        }
        catch (Exception ex)
        {
            _sawmill.Error("Engine download failed: {Error}", ex.Message);
            _userInterfaceManager.Popup(
                Loc.GetString("eclipse-engine-download-failed", ("reason", ex.Message)),
                Loc.GetString("eclipse-engine-download-window-title"));
            return false;
        }
        finally
        {
            CloseDialog();
        }
    }

    public Task<bool> TryConnectLaunchStateAsync(IGameController gameController, Action connect)
    {
        var address = gameController.LaunchState.ConnectAddress;
        if (string.IsNullOrWhiteSpace(address))
            return Task.FromResult(true);

        if (!TryParseConnectAddress(address, out var host, out var port))
        {
            _sawmill.Warning("Could not parse launch connect address: {Address}", address);
            return Task.FromResult(true);
        }

        return TryConnectAsync(host, port, connect);
    }

    private static bool TryParseConnectAddress(string address, out string host, out ushort port)
    {
        host = string.Empty;
        port = 1212;

        var work = address.Trim();
        var schemeIndex = work.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0)
            work = work[(schemeIndex + 3)..];

        var slashIndex = work.IndexOf('/');
        if (slashIndex >= 0)
            work = work[..slashIndex];

        var match6 = IPv6Regex.Match(work);
        if (match6 != Match.Empty)
        {
            host = match6.Groups[1].Value;
            if (match6.Groups[2].Success && !ushort.TryParse(match6.Groups[2].Value, out port))
                return false;

            return !string.IsNullOrEmpty(host);
        }

        var split = work.Split(':');
        if (split.Length > 2)
            return false;

        host = work;
        if (split.Length == 2)
        {
            host = split[0];
            if (!ushort.TryParse(split[1], out port))
                return false;
        }

        return !string.IsNullOrEmpty(host);
    }

    private async Task DownloadEngineAsync(string requiredVersion)
    {
        _dialog = new EclipseEngineDownloadDialog();
        _userInterfaceManager.WindowRoot.AddChild(_dialog);
        _dialog.OpenCentered();

        var cancel = _dialog.BeginOperation();
        Action<EclipseEngineDownloadProgress> onProgress = status =>
        {
            _userInterfaceManager.DeferAction(() => _dialog?.UpdateProgress(status));
        };

        await EclipseEngineApiBridge.InstallEngineAsync(requiredVersion, onProgress, cancel);
    }

    private void RelaunchEngine(string requiredVersion, string host, ushort port)
    {
        var args = BuildLaunchArguments(host, port);
        EclipseEngineApiBridge.LaunchInstalledEngine(
            requiredVersion,
            EclipseEngineApiBridge.GetLauncherContentRoot(),
            args);
        _gameController.Shutdown("Eclipse engine handoff");
    }

    private static IEnumerable<string> BuildLaunchArguments(string host, ushort port)
    {
        yield return "--connect";
        yield return "--launcher";
        yield return "--connect-address";
        yield return $"{host}:{port}";
        yield return "--ss14-address";
        yield return $"ss14://{host}:{port}/";
    }

    private void CloseDialog()
    {
        if (_dialog == null)
            return;

        _dialog.Close();
        _dialog.Dispose();
        _dialog = null;
    }
}
