namespace Winotch;

/// <summary>What a media key asks for.</summary>
public enum MediaKeyAction { None, PlayPause, Next, Prev }

/// <summary>The media keys. Unlike the volume keys these are never claimed:
/// Windows and the app that owns the session keep handling them exactly as
/// before, and Winotch only mirrors the press onto the pill. Nothing here can
/// break playback — the worst case is a chip that does not appear.</summary>
public static class MediaKeys
{
    // Note the order: Windows' own VK_MEDIA_NEXT_TRACK is 0xB0 and
    // VK_MEDIA_PREV_TRACK is 0xB1 — the reverse of what the names suggest.
    public const int NextTrackVirtualKey = 0xB0;
    public const int PrevTrackVirtualKey = 0xB1;
    public const int PlayPauseVirtualKey = 0xB3;

    public static MediaKeyAction Classify(int virtualKey) => virtualKey switch
    {
        PlayPauseVirtualKey => MediaKeyAction.PlayPause,
        NextTrackVirtualKey => MediaKeyAction.Next,
        PrevTrackVirtualKey => MediaKeyAction.Prev,
        _ => MediaKeyAction.None,
    };

    /// <summary>Whether the transport chip should appear for this key. This only
    /// gates the chip — the key itself is never swallowed either way.</summary>
    public static bool Observes(int virtualKey, bool enabled, bool awake) =>
        enabled && awake && virtualKey != 0 && Classify(virtualKey) != MediaKeyAction.None;

    /// <summary>The glyph for the key that was pressed, in the same "what this
    /// press will do" sense as the Now playing card's own buttons: a play mark
    /// while nothing is playing, a pause mark while something is.</summary>
    public static string Glyph(MediaKeyAction action, bool playing) => action switch
    {
        MediaKeyAction.PlayPause => playing ? "\uE769" : "\uE768",
        MediaKeyAction.Next => "\uE893",
        MediaKeyAction.Prev => "\uE892",
        _ => "\uE768",
    };

    /// <summary>The chip's caption — the action, never a claim about playback
    /// state (which arrives with the next poll).</summary>
    public static string Label(MediaKeyAction action) => action switch
    {
        MediaKeyAction.PlayPause => "Play / pause",
        MediaKeyAction.Next => "Next track",
        MediaKeyAction.Prev => "Previous track",
        _ => "",
    };
}
