#if WINDOWS
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Winotch;

/// <summary>Live CPU / memory / network sampler. CPU uses GetSystemTimes
/// (kernel idle vs. total deltas), memory comes from GlobalMemoryStatusEx,
/// and network is the summed IPv4 traffic delta across up interfaces — all
/// pure .NET / P/Invoke, no performance counters, nothing installed.</summary>
public sealed class SystemStatsSampler
{
    private long _lastIdle, _lastTotal;
    private long _lastRx, _lastTx;
    private bool _hasBase;

    /// <summary>Read a fresh sample. CPU/network need a previous sample to
    /// compute a rate; the first call returns 0 for those.</summary>
    public SystemStatsSnapshot Sample()
    {
        GetSystemTimes(out var idle, out var kernel, out var user);
        var idleTicks = ToTicks(idle);
        var totalTicks = ToTicks(kernel) + ToTicks(user);

        (long Rx, long Tx) net = NetTotals();

        double cpu = 0;
        if (_hasBase && totalTicks > _lastTotal)
        {
            var idleDelta = idleTicks - _lastIdle;
            var totalDelta = totalTicks - _lastTotal;
            cpu = Math.Clamp(100.0 * (1.0 - (double)idleDelta / totalDelta), 0, 100);
        }
        _lastIdle = idleTicks;
        _lastTotal = totalTicks;
        _hasBase = true;

        double up = 0, down = 0;
        if (_lastRx != 0)
        {
            down = Math.Max(0, (net.Rx - _lastRx) / 1024.0);
            up = Math.Max(0, (net.Tx - _lastTx) / 1024.0);
        }
        _lastRx = net.Rx;
        _lastTx = net.Tx;

        return new SystemStatsSnapshot(cpu, MemoryPercent(), up, down);
    }

    private static (long Rx, long Tx) NetTotals()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                var type = nic.NetworkInterfaceType;
                if (type == NetworkInterfaceType.Loopback || type == NetworkInterfaceType.Tunnel) continue;
                var stats = nic.GetIPv4Statistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }
        }
        catch
        {
            // Some adapters can throw transiently; keep last totals.
        }
        return (rx, tx);
    }

    private static double MemoryPercent()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status)) return 0;
        return Math.Clamp(status.MemoryLoad, 0, 100);
    }

    private static long ToTicks(FileTime ft) => ((long)ft.High << 32) | ft.Low;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential)]
    private class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);
}
#endif