#if WINDOWS
namespace Winotch.Platform.Windows;

/// <summary>Windows system stats service using Win32 P/Invoke.</summary>
public sealed class WindowsSystemStatsService : ISystemStatsService
{
    private readonly SystemStatsSampler _sampler = new();

    public SystemResourceSnapshot GetSnapshot()
    {
        var stats = _sampler.Sample();
        return new SystemResourceSnapshot(
            CpuUsagePercent: stats.CpuPercent,
            MemoryUsedBytes: (long)(stats.MemoryPercent / 100.0 * GetTotalMemory()),
            MemoryTotalBytes: GetTotalMemory(),
            NetworkReceivedBytes: 0, // Would need to track deltas
            NetworkSentBytes: 0);
    }

    private static long GetTotalMemory()
    {
        // Use GC or P/Invoke to get total memory
        return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }
}

#endif
