using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Client._Eclipse.Engine;

/// <summary>
/// Downloads and launches the Eclipse engine without referencing Eclipse-only Robust.Client types.
/// This allows the stock SS14 launcher engine to load content, bootstrap, and hand off.
/// </summary>
public static class ContentEclipseEngineBootstrap
{
    public const string RequiredVersion = "v0.0.2-eclipse";

    private const string GitHubRepo = "ShalnayaKlyaksa/Eclipse_Client";
    private const string ReleaseAssetName = "Eclipse.Engine.zip";
    private static readonly Regex RobustAssemblyVersionRegex = new(@"^\d+\.\d+", RegexOptions.CultureInvariant);

    public static string DefaultEngineVersion => RequiredVersion;

    public static string NormalizeReleaseVersion(string? serverReportedVersion)
    {
        if (string.IsNullOrWhiteSpace(serverReportedVersion))
            return RequiredVersion;

        var version = serverReportedVersion.Trim();

        if (version.Contains("eclipse", StringComparison.OrdinalIgnoreCase))
            return version;

        // Servers still report the stock Robust assembly version (e.g. 277.0.0).
        if (RobustAssemblyVersionRegex.IsMatch(version))
            return RequiredVersion;

        return version;
    }

    public static bool IsRunningFromInstalledEngine(string requiredVersion)
    {
        var engineDir = GetEngineInstallDirectory(requiredVersion);
        return IsInstalledEngineValid(engineDir, GetVersionFile(engineDir), requiredVersion)
            && IsRunningFrom(engineDir);
    }

    public static string? GetInstalledEngineVersion(string requiredVersion)
    {
        var engineDir = GetEngineInstallDirectory(requiredVersion);
        var versionFile = GetVersionFile(engineDir);

        if (!IsInstalledEngineValid(engineDir, versionFile, requiredVersion))
            return null;

        return File.ReadAllText(versionFile).Trim();
    }

    public static async Task<string?> FetchServerEngineVersionAsync(string host, int port, CancellationToken cancel = default)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EclipseEngineBootstrap/1.0");
        client.Timeout = TimeSpan.FromSeconds(15);

        var uri = new Uri($"http://{host}:{port}/info");
        using var response = await client.GetAsync(uri, cancel);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonObject>(cancel);
        return json?["build"]?["engine_version"]?.GetValue<string>();
    }

    public static async Task InstallEngineAsync(
        string requiredVersion,
        Action<EclipseEngineDownloadProgress>? onProgress,
        CancellationToken cancel = default)
    {
        var engineDir = GetEngineInstallDirectory(requiredVersion);
        var versionFile = GetVersionFile(engineDir);

        Directory.CreateDirectory(engineDir);

        var tempZip = Path.Combine(Path.GetTempPath(), $"eclipse-engine-{requiredVersion}.zip");
        try
        {
            using var apiClient = CreateApiClient();
            using var downloadClient = CreateDownloadClient();
            var downloadUrl = await ResolveDownloadUrlAsync(apiClient, downloadClient, requiredVersion, cancel);

            await DownloadEngineAsync(downloadClient, downloadUrl, tempZip, onProgress, cancel);
            ClearEngineDirectory(engineDir, versionFile);
            await ExtractEngineAsync(tempZip, engineDir, onProgress, cancel);
            await File.WriteAllTextAsync(versionFile, requiredVersion, Encoding.UTF8, cancel);

            onProgress?.Invoke(new EclipseEngineDownloadProgress("Ready", 0, null, 0, null));
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }

    public static string GetLauncherContentRoot() => AppContext.BaseDirectory;

    public static void LaunchInstalledEngine(string requiredVersion, string contentRoot, IEnumerable<string> launchArgs)
    {
        var engineDir = GetEngineInstallDirectory(requiredVersion);
        if (!IsInstalledEngineValid(engineDir, GetVersionFile(engineDir), requiredVersion))
            throw new InvalidOperationException($"Installed engine {requiredVersion} is missing or invalid.");

        string executable;
        string workingDirectory;
        var useContentRootEnv = false;

        if (HasBundledGameContent(engineDir))
        {
            executable = GetLauncherExecutable(engineDir);
            workingDirectory = engineDir;
        }
        else
        {
            var launcherDir = Path.Combine(engineDir, "launcher");
            SyncContentLauncherFiles(launcherDir, contentRoot, engineDir);
            executable = GetLauncherExecutable(launcherDir);
            workingDirectory = launcherDir;
            useContentRootEnv = true;
        }

        if (!File.Exists(executable))
            throw new InvalidOperationException($"Eclipse launcher executable not found: {executable}");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = (string) entry.Key!;
            startInfo.Environment[key] = entry.Value?.ToString() ?? string.Empty;
        }

        if (useContentRootEnv)
        {
            startInfo.Environment["ECLIPSE_CONTENT_ROOT"] = GetGameContentRoot(contentRoot);
            startInfo.Environment["ECLIPSE_ENGINE_ROOT"] = engineDir;
        }

        foreach (var arg in launchArgs)
            startInfo.ArgumentList.Add(arg);

        if (Process.Start(startInfo) == null)
            throw new InvalidOperationException($"Failed to start Eclipse client: {executable}");
    }

    private static string GetEngineInstallDirectory(string requiredVersion)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Eclipse", "engine", requiredVersion);
    }

    private static string GetVersionFile(string engineDir) => Path.Combine(engineDir, "version.txt");

    private static bool IsInstalledEngineValid(string engineDir, string versionFile, string requiredVersion)
    {
        if (!File.Exists(versionFile))
            return false;

        var installedVersion = File.ReadAllText(versionFile).Trim();
        if (!string.Equals(installedVersion, requiredVersion, StringComparison.Ordinal))
            return false;

        return File.Exists(GetEngineExecutable(engineDir));
    }

    private static string GetEngineExecutable(string engineDir)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(engineDir, "Robust.Client.exe");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine(engineDir, "Robust.Client");

        return Path.Combine(engineDir, "Robust.Client");
    }

    private static bool IsRunningFrom(string engineDir)
    {
        var currentDir = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var targetDir = Path.GetFullPath(engineDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(currentDir, targetDir, StringComparison.OrdinalIgnoreCase))
            return true;

        var launcherDir = Path.Combine(targetDir, "launcher");
        launcherDir = Path.GetFullPath(launcherDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(currentDir, launcherDir, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateApiClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EclipseEngineBootstrap/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static HttpClient CreateDownloadClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EclipseEngineBootstrap/1.0");
        return client;
    }

    private static async Task<string> ResolveDownloadUrlAsync(
        HttpClient apiClient,
        HttpClient downloadClient,
        string releaseTag,
        CancellationToken cancel)
    {
        var overrideUrl = Environment.GetEnvironmentVariable("ECLIPSE_ENGINE_DOWNLOAD_URL");
        if (!string.IsNullOrWhiteSpace(overrideUrl))
            return overrideUrl;

        var directUrl = $"https://github.com/{GitHubRepo}/releases/download/{releaseTag}/{ReleaseAssetName}";
        if (await UrlExistsAsync(downloadClient, directUrl, cancel))
            return directUrl;

        var apiUrl = await ResolveDownloadUrlFromGitHubApiAsync(apiClient, releaseTag, cancel);
        if (apiUrl != null)
            return apiUrl;

        throw new InvalidOperationException(
            $"Could not find {ReleaseAssetName} for release '{releaseTag}' in {GitHubRepo}. " +
            $"Expected URL: {directUrl}. " +
            $"Create a GitHub Release with tag '{releaseTag}' and upload {ReleaseAssetName}.");
    }

    private static async Task<bool> UrlExistsAsync(HttpClient client, string url, CancellationToken cancel)
    {
        using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
        using var headResponse = await client.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (headResponse.IsSuccessStatusCode)
            return true;

        if (headResponse.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed))
            return false;

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
        getRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
        using var getResponse = await client.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cancel);
        return getResponse.IsSuccessStatusCode || getResponse.StatusCode == HttpStatusCode.PartialContent;
    }

    private static async Task<string?> ResolveDownloadUrlFromGitHubApiAsync(
        HttpClient client,
        string releaseTag,
        CancellationToken cancel)
    {
        var endpoints = new[]
        {
            $"https://api.github.com/repos/{GitHubRepo}/releases/tags/{releaseTag}",
            $"https://api.github.com/repos/{GitHubRepo}/releases/latest",
            $"https://api.github.com/repos/{GitHubRepo}/releases",
        };

        foreach (var endpoint in endpoints)
        {
            try
            {
                if (endpoint.EndsWith("/releases", StringComparison.Ordinal))
                {
                    var releases = await client.GetFromJsonAsync<JsonArray>(endpoint, cancel);
                    if (releases == null)
                        continue;

                    foreach (var releaseNode in releases)
                    {
                        if (releaseNode is not JsonObject release)
                            continue;

                        if (!ReleaseMatchesTag(release, releaseTag))
                            continue;

                        var assetUrl = FindAssetUrl(release);
                        if (assetUrl != null)
                            return assetUrl;
                    }

                    continue;
                }

                var single = await client.GetFromJsonAsync<JsonObject>(endpoint, cancel);
                if (single == null)
                    continue;

                if (!endpoint.Contains("/latest", StringComparison.Ordinal) && !ReleaseMatchesTag(single, releaseTag))
                    continue;

                var url = FindAssetUrl(single);
                if (url != null)
                    return url;
            }
            catch
            {
                // Try the next endpoint.
            }
        }

        return null;
    }

    private static bool ReleaseMatchesTag(JsonObject release, string releaseTag)
    {
        var tag = release["tag_name"]?.GetValue<string>();
        if (string.Equals(tag, releaseTag, StringComparison.OrdinalIgnoreCase))
            return true;

        var name = release["name"]?.GetValue<string>();
        return string.Equals(name, releaseTag, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindAssetUrl(JsonObject release)
    {
        if (release["assets"] is not JsonArray assets)
            return null;

        foreach (var assetNode in assets)
        {
            if (assetNode is not JsonObject asset)
                continue;

            var name = asset["name"]?.GetValue<string>();
            if (!string.Equals(name, ReleaseAssetName, StringComparison.OrdinalIgnoreCase))
                continue;

            return asset["browser_download_url"]?.GetValue<string>();
        }

        return null;
    }

    private static async Task DownloadEngineAsync(
        HttpClient client,
        string downloadUrl,
        string tempZip,
        Action<EclipseEngineDownloadProgress>? onProgress,
        CancellationToken cancel)
    {
        using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Engine download failed ({(int) response.StatusCode} {response.ReasonPhrase}): {downloadUrl}");
        }

        var totalBytes = response.Content.Headers.ContentLength;
        var tracker = new DownloadProgressTracker();
        tracker.Report(onProgress, "Downloading engine", 0, totalBytes);

        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        await using var file = File.Create(tempZip);

        var buffer = new byte[1024 * 128];
        long downloaded = 0;
        int read;

        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancel)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), cancel);
            downloaded += read;
            tracker.Report(onProgress, "Downloading engine", downloaded, totalBytes);
        }
    }

    private static async Task ExtractEngineAsync(
        string tempZip,
        string engineDir,
        Action<EclipseEngineDownloadProgress>? onProgress,
        CancellationToken cancel)
    {
        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(tempZip);
            var entries = archive.Entries;
            var total = Math.Max(entries.Count, 1);

            for (var i = 0; i < entries.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();

                var entry = entries[i];
                var destination = Path.Combine(engineDir, entry.FullName);

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destination);
                }
                else
                {
                    var parent = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);

                    entry.ExtractToFile(destination, overwrite: true);
                }

                onProgress?.Invoke(new EclipseEngineDownloadProgress(
                    "Extracting engine",
                    i + 1,
                    total,
                    0,
                    null));
            }
        }, cancel);
    }

    private static void ClearEngineDirectory(string engineDir, string versionFile)
    {
        if (!Directory.Exists(engineDir))
            return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(engineDir))
        {
            if (entry == versionFile)
                continue;

            if (Directory.Exists(entry))
                Directory.Delete(entry, recursive: true);
            else
                File.Delete(entry);
        }
    }

    private static string GetLauncherExecutable(string engineDir)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(engineDir, "Content.Client.exe");

        return Path.Combine(engineDir, "Content.Client");
    }

    private static bool HasBundledGameContent(string engineDir)
    {
        return Directory.Exists(Path.Combine(engineDir, "Resources", "Prototypes"));
    }

    private static string GetGameContentRoot(string contentRoot)
    {
        return Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));
    }

    private static void SyncContentLauncherFiles(string launcherDir, string contentRoot, string engineDir)
    {
        if (Directory.Exists(launcherDir))
            Directory.Delete(launcherDir, recursive: true);

        Directory.CreateDirectory(launcherDir);
        CopyDirectory(contentRoot, launcherDir, skipRobustFiles: true);
        OverlayEngineFiles(launcherDir, engineDir);
        OverlayLocalDevEngineFiles(launcherDir, contentRoot);
    }

    private static void OverlayLocalDevEngineFiles(string launcherDir, string contentRoot)
    {
        var gameRoot = Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));
        if (!File.Exists(Path.Combine(gameRoot, "RobustToolbox", "Robust.Client", "Robust.Client.csproj")))
            return;

        var devEngineDir = Path.Combine(gameRoot, "RobustToolbox", "bin", "Client");
        if (!Directory.Exists(devEngineDir))
            return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(devEngineDir))
        {
            var name = Path.GetFileName(entry);
            if (name.StartsWith("Content.", StringComparison.OrdinalIgnoreCase))
                continue;

            var destination = Path.Combine(launcherDir, name);
            if (Directory.Exists(entry))
                CopyDirectory(entry, destination, skipRobustFiles: false);
            else if (ShouldOverlayEngineFile(name))
                File.Copy(entry, destination, overwrite: true);
        }
    }

    private static void OverlayEngineFiles(string launcherDir, string engineDir)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(engineDir))
        {
            var name = Path.GetFileName(entry);
            if (ShouldSkipEngineOverlay(name))
                continue;

            var destination = Path.Combine(launcherDir, name);
            if (Directory.Exists(entry))
            {
                CopyDirectory(entry, destination, skipRobustFiles: false);
                continue;
            }

            if (ShouldOverlayEngineFile(name))
                File.Copy(entry, destination, overwrite: true);
        }
    }

    private static bool ShouldSkipEngineOverlay(string name)
    {
        return string.Equals(name, "launcher", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "version.txt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Resources", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Content.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldOverlayEngineFile(string name)
    {
        return name.StartsWith("Robust.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "runtimes", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir, bool skipRobustFiles)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDir))
        {
            var name = Path.GetFileName(entry);
            if (skipRobustFiles && name.StartsWith("Robust.", StringComparison.OrdinalIgnoreCase))
                continue;

            var destination = Path.Combine(destinationDir, name);
            if (Directory.Exists(entry))
                CopyDirectory(entry, destination, skipRobustFiles);
            else
                File.Copy(entry, destination, overwrite: true);
        }
    }

    private sealed class DownloadProgressTracker
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private long _lastBytes;
        private TimeSpan _lastSample = TimeSpan.Zero;
        private double _smoothedSpeed;

        public void Report(Action<EclipseEngineDownloadProgress>? onProgress, string phase, long downloaded, long? total)
        {
            if (onProgress == null)
                return;

            var now = _stopwatch.Elapsed;
            var dt = (now - _lastSample).TotalSeconds;

            if (dt >= 0.25 && downloaded >= _lastBytes)
            {
                var instant = (downloaded - _lastBytes) / dt;
                _smoothedSpeed = _smoothedSpeed <= 0
                    ? instant
                    : _smoothedSpeed * 0.75 + instant * 0.25;

                _lastBytes = downloaded;
                _lastSample = now;
            }

            TimeSpan? eta = null;
            if (total is > 0 && _smoothedSpeed > 1)
                eta = TimeSpan.FromSeconds((total.Value - downloaded) / _smoothedSpeed);

            onProgress(new EclipseEngineDownloadProgress(
                phase,
                downloaded,
                total,
                _smoothedSpeed,
                eta));
        }
    }
}
