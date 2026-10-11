using Winotch.Engine;

namespace Winotch;

/// <summary>What an intercepted key should do.</summary>
public enum HudKeyAction { None, VolumeUp, VolumeDown, VolumeMute }

/// <summary>The keyboard part of the volume/brightness HUD: which virtual keys
/// are ours, how far one press moves the level, and whether Winotch is allowed
/// to claim the key at all. Pure, so the policy can be tested without a
/// keyboard hook. Brightness has no virtual key — the brightness HUD mirrors
/// the level instead of claiming keys (see BrightnessService for a monitor on
/// the cable and InternalBrightness for a laptop's own panel).</summary>
public static class HudKeys
{
    public const int VolumeMuteVirtualKey = 0xAD;
    public const int VolumeDownVirtualKey = 0xAE;
    public const int VolumeUpVirtualKey = 0xAF;

    /// <summary>One press moves the level by this much — Windows 11's own step
    /// (50 steps from silence to full).</summary>
    public const float VolumeStep = 0.02f;

    public static HudKeyAction Classify(int virtualKey) => virtualKey switch
    {
        VolumeUpVirtualKey => HudKeyAction.VolumeUp,
        VolumeDownVirtualKey => HudKeyAction.VolumeDown,
        VolumeMuteVirtualKey => HudKeyAction.VolumeMute,
        _ => HudKeyAction.None,
    };

    /// <summary>Whether Winotch should swallow this key and apply it itself.
    /// Every condition matters: a swallowed key we can't act on would just go
    /// dead — no Windows OSD, no volume change — so a hidden, paused or
    /// HUD-less Winotch has to let the keys through. "Awake" is the host's
    /// view of that: running, not paused, notch on screen.</summary>
    public static bool TakesOver(int virtualKey, bool hudEnabled, bool awake, bool volumeAvailable) =>
        hudEnabled && awake && volumeAvailable && Classify(virtualKey) != HudKeyAction.None;

    /// <summary>The level after one press, clamped to the range.</summary>
    public static float Step(float level01, HudKeyAction action) => action switch
    {
        HudKeyAction.VolumeUp => Math.Clamp(level01 + VolumeStep, 0f, 1f),
        HudKeyAction.VolumeDown => Math.Clamp(level01 - VolumeStep, 0f, 1f),
        _ => Math.Clamp(level01, 0f, 1f),
    };

    /// <summary>Windows' own volume keys unmute as they move the level, so
    /// ours do too — otherwise pressing volume-up while muted looks broken.</summary>
    public static bool Unmutes(HudKeyAction action) =>
        action is HudKeyAction.VolumeUp or HudKeyAction.VolumeDown;

    /// <summary>Percent text for the HUD: "Muted" beats a number, because a
    /// muted endpoint still reports its old level.</summary>
    public static string Label(HudKind kind, double level01, bool muted) => muted
        ? "Muted"
        : $"{(int)Math.Round(Math.Clamp(level01, 0, 1) * 100)}%";
}
