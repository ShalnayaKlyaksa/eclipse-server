using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.IoC;
using Robust.Shared.IoC.Exceptions;

namespace Content.Client._Eclipse.Engine;

/// <summary>
/// Reflection bridge to <c>Robust.Client.Eclipse.IEclipseEngineApi</c>.
/// Content must not reference Eclipse engine types directly (sandbox + stock-engine compatibility).
/// </summary>
internal static class EclipseEngineApiBridge
{
    private const string ApiTypeName = "Robust.Client.Eclipse.IEclipseEngineApi, Robust.Client";

    public static bool IsAvailable => ResolveApi() != null;

    public static string DefaultEngineVersion =>
        GetProperty<string>(ResolveApi(), nameof(DefaultEngineVersion)) ?? ContentEclipseEngineVersion.RequiredVersion;

    public static bool IsRunningFromInstalledEngine(string requiredVersion) =>
        Invoke<bool>(ResolveApi(), nameof(IsRunningFromInstalledEngine), requiredVersion);

    public static string? GetInstalledEngineVersion(string requiredVersion) =>
        Invoke<string?>(ResolveApi(), nameof(GetInstalledEngineVersion), requiredVersion);

    public static async Task<string?> FetchServerEngineVersionAsync(string host, int port, CancellationToken cancel = default)
    {
        var api = ResolveApi();
        if (api == null)
            return null;

        var task = Invoke<Task<string?>>(api, nameof(FetchServerEngineVersionAsync), host, port, cancel);
        return await task.ConfigureAwait(false);
    }

    public static async Task InstallEngineAsync(
        string requiredVersion,
        Action<EclipseEngineDownloadProgress>? onProgress,
        CancellationToken cancel = default)
    {
        var api = ResolveApi() ?? throw new InvalidOperationException("Eclipse engine API is not available.");

        var statusType = api.GetType().Assembly.GetType("Robust.Client.Eclipse.EclipseEngineDownloadStatus")
            ?? throw new InvalidOperationException("Eclipse engine download status type is not available.");

        var progressDelegate = onProgress == null
            ? null
            : ProgressForwarder.Create(statusType, onProgress);

        var task = (Task) Invoke(api, nameof(InstallEngineAsync), requiredVersion, progressDelegate, cancel)!;
        await task.ConfigureAwait(false);
    }

    public static void LaunchInstalledEngine(string requiredVersion, string contentRoot, IEnumerable<string> launchArgs)
    {
        var api = ResolveApi() ?? throw new InvalidOperationException("Eclipse engine API is not available.");
        Invoke(api, nameof(LaunchInstalledEngine), requiredVersion, contentRoot, launchArgs);
    }

    public static string GetLauncherContentRoot() =>
        GetProperty<string>(ResolveApi(), nameof(GetLauncherContentRoot)) ?? AppContext.BaseDirectory;

    private static object? ResolveApi()
    {
        var apiType = Type.GetType(ApiTypeName, throwOnError: false);
        if (apiType == null)
            return null;

        try
        {
            return IoCManager.ResolveType(apiType);
        }
        catch (UnregisteredTypeException)
        {
            return null;
        }
    }

    private static T? GetProperty<T>(object? target, string propertyName)
    {
        if (target == null)
            return default;

        return (T?) target.GetType().GetProperty(propertyName)?.GetValue(target);
    }

    private static T Invoke<T>(object? target, string methodName, params object?[] args)
    {
        return (T) Invoke(target, methodName, args)!;
    }

    private static object? Invoke(object? target, string methodName, params object?[] args)
    {
        if (target == null)
            throw new InvalidOperationException("Eclipse engine API is not available.");

        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        return method!.Invoke(target, args);
    }

    private static class ProgressForwarder
    {
        public static Delegate? Create(Type statusType, Action<EclipseEngineDownloadProgress> onProgress)
        {
            var forwarderType = typeof(Forwarder<>).MakeGenericType(statusType);
            var instance = Activator.CreateInstance(forwarderType, onProgress)!;
            var method = forwarderType.GetMethod(nameof(Forwarder<object>.Forward), BindingFlags.Instance | BindingFlags.Public)!;
            return Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(statusType), instance, method);
        }

        private sealed class Forwarder<TStatus>
        {
            private readonly Action<EclipseEngineDownloadProgress> _onProgress;

            public Forwarder(Action<EclipseEngineDownloadProgress> onProgress)
            {
                _onProgress = onProgress;
            }

            public void Forward(TStatus status)
            {
                var type = status!.GetType();
                _onProgress(new EclipseEngineDownloadProgress(
                    Phase: (string) type.GetProperty("Phase")!.GetValue(status)!,
                    DownloadedBytes: (long) type.GetProperty("DownloadedBytes")!.GetValue(status)!,
                    TotalBytes: (long?) type.GetProperty("TotalBytes")!.GetValue(status),
                    BytesPerSecond: (double) type.GetProperty("BytesPerSecond")!.GetValue(status)!,
                    EstimatedTimeRemaining: (TimeSpan?) type.GetProperty("EstimatedTimeRemaining")!.GetValue(status)));
            }
        }
    }
}

/// <summary>
/// Eclipse engine release tag used for install / handoff (not the launcher CDN engine version).
/// </summary>
internal static class ContentEclipseEngineVersion
{
    public const string RequiredVersion = "v0.0.2-eclipse";
}
