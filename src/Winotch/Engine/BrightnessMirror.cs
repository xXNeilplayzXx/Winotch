namespace Winotch.Engine;

/// <summary>Which interface a brightness reading came out of. Windows exposes
/// screen brightness twice and the two are unrelated: DDC/CI travels down the
/// display cable, while an internal panel answers ACPI through WMI. A machine
/// usually speaks only one of them.</summary>
public enum BrightnessSource { None, Monitor, Internal }

/// <summary>One reading worth showing: the level 0..100 and who reported it.</summary>
public readonly record struct BrightnessReading(int Percent, BrightnessSource Source);

/// <summary>Decides when a brightness *change* is worth a HUD.
///
/// Both interfaces are read every poll and each is compared against its own
/// last value: a machine can answer on one and stay silent on the other, and the
/// two levels are unrelated anyway (the panel in a laptop lid and the monitor on
/// the desk behind it are not at the same brightness).
///
/// A source that has never answered only *seeds* the mirror. Brightness
/// appearing for the first time is not a change — a monitor waking up, or a
/// handshake with a slow DDC/CI endpoint, must not pop a HUD nobody asked for.
/// A source that drops out keeps its last value, so one failed read doesn't make
/// the next identical reading look like a change either.
///
/// When both move in the same poll the internal panel wins: it is the one a
/// laptop's own brightness keys drive.
///
/// Pure on purpose. The seeding, the independence of the two readers and the
/// priority are the parts that were wrong before, and they can be tested here
/// without a monitor.</summary>
public sealed class BrightnessMirror
{
    private int? _monitor;
    private int? _internal;
    private bool _seeded;

    /// <summary>The interface the mirror last heard from, for the Settings
    /// window's diagnostic line. It holds its answer while a reader is failing,
    /// so a flaky poll can't make the line flap between sources.</summary>
    public BrightnessSource Source { get; private set; }

    /// <summary>Fold one poll into the mirror: a reading when a level actually
    /// moved, otherwise null (a quiet poll, or the first read ever).</summary>
    public BrightnessReading? Observe(int? monitor, int? internalPanel)
    {
        var source = internalPanel is not null ? BrightnessSource.Internal
            : monitor is not null ? BrightnessSource.Monitor
            : BrightnessSource.None;
        if (source != BrightnessSource.None) Source = source;

        if (!_seeded)
        {
            _seeded = true;
            _monitor = monitor;
            _internal = internalPanel;
            return null;
        }

        var internalMoved = internalPanel is not null && _internal is not null && internalPanel != _internal;
        var monitorMoved = monitor is not null && _monitor is not null && monitor != _monitor;
        if (internalPanel is not null) _internal = internalPanel;
        if (monitor is not null) _monitor = monitor;

        if (internalMoved) return new BrightnessReading(internalPanel!.Value, BrightnessSource.Internal);
        if (monitorMoved) return new BrightnessReading(monitor!.Value, BrightnessSource.Monitor);
        return null;
    }

    /// <summary>The line under the brightness toggle. It says which interface is
    /// live, or that this display simply doesn't report brightness — the
    /// difference between a bug and a machine that can't do it.</summary>
    public static string Describe(BrightnessSource source, bool enabled)
    {
        if (!enabled) return "Mirror brightness changes onto the pill (your PC may show its own too)";
        return source switch
        {
            BrightnessSource.Internal => "Mirroring this display's brightness (Windows backlight)",
            BrightnessSource.Monitor => "Mirroring the monitor's brightness (DDC/CI)",
            _ => "This display doesn't report brightness, so the HUD can't appear yet",
        };
    }
}
