namespace Winotch.Platform.Linux;

/// <summary>System stats service reading from /proc filesystem.</summary>
public sealed class LinuxSystemStatsService : ISystemStatsService
{
    private long _prevCpuIdle;
    private long _prevCpuTotal;
    private long _prevNetRx;
    private long _prevNetTx;
    private DateTime _prevNetTime = DateTime.MinValue;

    public SystemResourceSnapshot GetSnapshot()
    {
        var cpu = GetCpuUsage();
        var memory = GetMemoryUsage();
        var network = GetNetworkStats();

        return new SystemResourceSnapshot(
            CpuUsagePercent: cpu,
            MemoryUsedBytes: memory.used,
            MemoryTotalBytes: memory.total,
            NetworkReceivedBytes: network.rx,
            NetworkSentBytes: network.tx
        );
    }

    private double GetCpuUsage()
    {
        try
        {
            var statLine = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu "));
            if (statLine == null) return 0;

            var parts = statLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // cpu  user nice system idle iowait irq softirq steal guest guest_nice
            if (parts.Length < 5) return 0;

            var user = long.Parse(parts[1]);
            var nice = long.Parse(parts[2]);
            var system = long.Parse(parts[3]);
            var idle = long.Parse(parts[4]);
            var iowait = parts.Length > 5 ? long.Parse(parts[5]) : 0;
            var irq = parts.Length > 6 ? long.Parse(parts[6]) : 0;
            var softirq = parts.Length > 7 ? long.Parse(parts[7]) : 0;
            var steal = parts.Length > 8 ? long.Parse(parts[8]) : 0;

            var total = user + nice + system + idle + iowait + irq + softirq + steal;
            var totalDiff = total - _prevCpuTotal;
            var idleDiff = idle - _prevCpuIdle;

            _prevCpuIdle = idle;
            _prevCpuTotal = total;

            if (totalDiff == 0) return 0;
            return (1.0 - (double)idleDiff / totalDiff) * 100.0;
        }
        catch
        {
            return 0;
        }
    }

    private (long used, long total) GetMemoryUsage()
    {
        try
        {
            var lines = File.ReadAllLines("/proc/meminfo");
            long memTotal = 0, memFree = 0, buffers = 0, cached = 0, sReclaimable = 0;

            foreach (var line in lines)
            {
                if (line.StartsWith("MemTotal:"))
                    memTotal = ParseMemInfoValue(line);
                else if (line.StartsWith("MemFree:"))
                    memFree = ParseMemInfoValue(line);
                else if (line.StartsWith("Buffers:"))
                    buffers = ParseMemInfoValue(line);
                else if (line.StartsWith("Cached:"))
                    cached = ParseMemInfoValue(line);
                else if (line.StartsWith("SReclaimable:"))
                    sReclaimable = ParseMemInfoValue(line);
            }

            var used = memTotal - memFree - buffers - cached - sReclaimable;
            return (used * 1024, memTotal * 1024); // Convert from kB to bytes
        }
        catch
        {
            return (0, 0);
        }
    }

    private static long ParseMemInfoValue(string line)
    {
        // Format: "MemTotal:       16384000 kB"
        var parts = line.Split(':', 2);
        if (parts.Length < 2) return 0;
        var valuePart = parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return valuePart.Length > 0 ? long.Parse(valuePart[0]) : 0;
    }

    private (long rx, long tx) GetNetworkStats()
    {
        try
        {
            // Read aggregate network stats from all active interfaces (excluding lo)
            var lines = File.ReadAllLines("/proc/net/dev");
            long totalRx = 0, totalTx = 0;

            foreach (var line in lines)
            {
                if (!line.Contains(':')) continue;
                var parts = line.Split(':', 2);
                var iface = parts[0].Trim();
                if (iface == "lo") continue; // Skip loopback

                var stats = parts[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (stats.Length >= 10)
                {
                    totalRx += long.Parse(stats[0]);
                    totalTx += long.Parse(stats[8]);
                }
            }

            // Return cumulative bytes (caller can compute rate if needed)
            return (totalRx, totalTx);
        }
        catch
        {
            return (_prevNetRx, _prevNetTx);
        }
    }
}
