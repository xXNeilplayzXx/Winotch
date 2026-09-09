namespace Winotch.Platform;

/// <summary>Snapshot of system resource usage (raw values from the OS).
/// Converted to <see cref="SystemStatsSnapshot"/> (app model) by the manager.</summary>
public sealed record SystemResourceSnapshot(
    double CpuUsagePercent,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    long NetworkReceivedBytes,
    long NetworkSentBytes);

/// <summary>Abstracts system stats (CPU, memory, network).
/// Windows: GetSystemTimes, GlobalMemoryStatusEx, NetworkInterface
/// Linux: /proc/stat, /proc/meminfo, /proc/net/dev</summary>
public interface ISystemStatsService
{
    SystemResourceSnapshot GetSnapshot();
}
