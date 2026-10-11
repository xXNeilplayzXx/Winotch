using System.Management;

namespace Winotch;

/// <summary>Brightness of the built-in panel, through WMI's
/// <c>root\wmi:WmiMonitorBrightness</c> class.
///
/// Windows exposes screen brightness twice and the two are not related.
/// dxva2's <c>GetMonitorBrightness</c> (see BrightnessService) speaks DDC/CI
/// over the display cable, which is how an external monitor answers, but a
/// laptop's internal panel hangs off the embedded controller behind ACPI and
/// normally says nothing at all on that path. The ACPI backlight interface is
/// what Windows' own brightness slider drives on a laptop, and WMI is the
/// supported way to read it. Reading only DDC/CI is why a laptop's own
/// brightness keys never produced a HUD.
///
/// Read-only, like BrightnessService: Winotch mirrors brightness, it never sets
/// it. Every failure is a null — "this machine can't tell us" — because a
/// desktop has no instances of the class at all — so the searcher is cached and
/// a failure backs off instead of running a query that throws every poll.</summary>
internal static class InternalBrightness
{
    private static readonly object Gate = new();

    /// <summary>How long to leave a failed WMI class alone. Without this a
    /// desktop would run a doomed query every poll for the life of the app.</summary>
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(30);

    private static ManagementObjectSearcher? _searcher;
    private static DateTime _retryAfter = DateTime.MinValue;

    /// <summary>Brightness as 0..100, or null when there is no internal panel to
    /// read (or WMI can't be reached right now).</summary>
    public static int? Level
    {
        get
        {
            lock (Gate)
            {
                if (DateTime.UtcNow < _retryAfter) return null;
                try
                {
                    _searcher ??= new ManagementObjectSearcher(
                        new ManagementScope(@"root\wmi"),
                        new ObjectQuery("SELECT CurrentBrightness, Active FROM WmiMonitorBrightness"));

                    int? inactive = null;
                    foreach (ManagementBaseObject instance in _searcher.Get())
                    {
                        using var monitor = (ManagementObject)instance;
                        if (Read(monitor["CurrentBrightness"]) is not { } level) continue;
                        // A machine with two panels, or a laptop with its lid shut
                        // and an external monitor attached, exposes more than one
                        // instance; the active one is the panel actually lit.
                        if (IsInactive(monitor["Active"])) inactive ??= level;
                        else return level;
                    }
                    return inactive;
                }
                catch
                {
                    // WMI missing, stopped, or going away with the session. Not a
                    // brightness change — just a machine that can't answer here.
                    _searcher = null;
                    _retryAfter = DateTime.UtcNow + FailureCooldown;
                    return null;
                }
            }
        }
    }

    /// <summary>Drop the cached WMI objects (shutdown).</summary>
    public static void Release()
    {
        lock (Gate)
        {
            _searcher?.Dispose();
            _searcher = null;
        }
    }

    /// <summary>True when the instance is explicitly marked inactive, so an
    /// absent or unreadable flag still counts as a usable panel rather than
    /// throwing the reading away.</summary>
    private static bool IsInactive(object? active) =>
        active switch
        {
            bool value => !value,
            string text when bool.TryParse(text, out var parsed) => !parsed,
            _ => false,
        };

    /// <summary>WMI hands the level back as whatever type the provider felt like
    /// — byte on most machines, a string on some — so accept the lot.</summary>
    private static int? Read(object? value) => value switch
    {
        byte b => b,
        sbyte sb => sb,
        ushort u when u <= 100 => u,
        short s when s is >= 0 and <= 100 => s,
        int i when i is >= 0 and <= 100 => i,
        uint ui when ui <= 100 => (int)ui,
        string text when int.TryParse(text, out var parsed) && parsed is >= 0 and <= 100 => parsed,
        _ => null,
    };
}
