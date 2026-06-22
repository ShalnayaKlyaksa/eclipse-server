using System;

namespace Content.Client._Eclipse.Engine;

/// <summary>
/// Progress report for Eclipse engine download / extract.
/// Kept in content so the stock launcher engine can load Content.Client before handoff.
/// </summary>
public readonly record struct EclipseEngineDownloadProgress(
    string Phase,
    long DownloadedBytes,
    long? TotalBytes,
    double BytesPerSecond,
    TimeSpan? EstimatedTimeRemaining);
