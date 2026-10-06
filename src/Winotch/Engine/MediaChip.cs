namespace Winotch.Engine;

/// <summary>Rules for the collapsed pill's now-playing chip: when it belongs on
/// the pill, and when a track that stopped keeps it around. Pure, so the pause
/// grace and the scroll decision can be tested without a media session.</summary>
public static class MediaChip
{
    /// <summary>How long a stopped track keeps its chip, so the paused state is
    /// visible for a moment before the pill goes back to the clock. Without it
    /// the chip would vanish the instant you hit pause.</summary>
    public const double PauseGraceSeconds = 20;

    /// <summary>Whether the chip shows right now. <paramref name="pausedAt"/> is
    /// when playback last stopped (null if nothing has stopped).</summary>
    public static bool Visible(bool enabled, bool hasTrack, bool playing,
        DateTimeOffset? pausedAt, DateTimeOffset now) =>
        enabled && hasTrack && (playing || WithinPauseGrace(pausedAt, now));

    /// <summary>Whether a stop is recent enough for the chip to linger.</summary>
    public static bool WithinPauseGrace(DateTimeOffset? pausedAt, DateTimeOffset now) =>
        pausedAt is { } at && (now - at).TotalSeconds <= PauseGraceSeconds;

    /// <summary>Whether a title has to scroll to be readable. Only a playing
    /// track scrolls: a paused one sits still so the pill can stop repainting,
    /// and a title that fits is left alone.</summary>
    public static bool ShouldScroll(bool playing, double titleWidth, double budget) =>
        playing && titleWidth > budget;
}
