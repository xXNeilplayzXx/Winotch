using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Winotch;

/// <summary>A live call: the detected app plus when the call began (for the
/// ticking pill timer). Null means no call.</summary>
public sealed record CallState(bool OnCall, string? AppName, DateTimeOffset StartedAtUtc);

/// <summary>A process name and its main window title — the raw material call
/// detection works from. Titles are optional; many processes have none.</summary>
public readonly record struct ProcessWindow(string ProcessName, string? Title);

/// <summary>One detected call source. <see cref="Key"/> is the stable identity
/// used to notice when the source changes; <see cref="Display"/> is what the
/// pill shows ("Teams", "Google Meet").</summary>
public sealed record CallMatch(string ProcessName, string Key, string Display);

/// <summary>Finds the app you're on a call with. Desktop apps are matched by
/// process name and a call-style window title; browser-based calls (Google Meet,
/// web Teams/Discord/Slack, WhatsApp Web, Jitsi, Whereby, …) by a window-title
/// signature. A running app only lights the pill when there's evidence of an
/// actual call — a call-like title, or a hot mic/camera for a known desktop app.
/// Detection is split into a pure, testable core (<see cref="Match"/>) and the
/// process scan that feeds it.</summary>
public static class CallDetector
{
    /// <summary>Known desktop call apps, lowercase process names. Order matters
    /// only for which name wins when several are running with equal evidence.</summary>
    public static readonly string[] CallApps =
    {
        // Big meeting apps
        "zoom", "ms-teams", "teams", "msteams", "gotomeeting", "meeting",
        "webex", "webexmta", "ciscowebexstart", "bluejeans", "ringcentral",
        "amazon chime",
        // Chat apps with voice/video
        "discord", "discordcanary", "discordptb", "slack", "skype", "skypeapp",
        "lync", "whatsapp", "phonelink", "telegram", "signal", "viber", "line",
        "wechat", "weixin", "dingtalk", "zalo", "kakaotalk",
        // Community / open-source voice
        "jitsi", "whereby", "gather", "around", "jami", "mumble", "ts3client",
        "teamspeak", "mattermost", "mattermost-desktop", "element",
        "element-desktop", "rocket.chat", "zulip", "wire",
        // Softphones (SIP/VoIP)
        "microsip", "linphone", "zoiper", "3cxphone",
    };

    /// <summary>Browsers whose tab titles are checked for web-call signatures.
    /// Lowercase process names.</summary>
    public static readonly string[] Browsers =
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "chromium",
        "librewolf", "waterfox", "thorium", "yandex",
    };

    public static bool IsCallApp(string processName) =>
        CallApps.Contains(processName.ToLowerInvariant());

    public static bool IsBrowser(string processName) =>
        Browsers.Contains(processName.ToLowerInvariant());

    /// <summary>First running call source with evidence of an actual call, or
    /// null. Reads every top-level window title in one pass, matches the ones
    /// belonging to call apps and browsers, then defers to <see cref="Match"/>.
    /// Never throws: process enumeration and window titles can fail mid-scan.</summary>
    public static CallMatch? FindCallApp(bool audioVisualActive = false)
    {
        // Only call apps and browsers matter, so collect those first — that
        // also keeps the window-title pass below off every other app's windows.
        var candidates = new List<(string Name, uint Pid)>();
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        var name = process.ProcessName;
                        if (!IsCallApp(name) && !IsBrowser(name)) continue;
                        candidates.Add((name, (uint)process.Id));
                    }
                    catch
                    {
                        // A dying process can fail to report its name or id.
                    }
                }
            }
        }
        catch
        {
            // Process enumeration can fail under heavy load or lockdown.
        }

        var titles = WindowTitles(candidates);
        var windows = new List<ProcessWindow>(candidates.Count);
        foreach (var (name, pid) in candidates)
        {
            windows.Add(new ProcessWindow(name, titles.TryGetValue(pid, out var title) ? title : null));
        }
        return Match(windows, audioVisualActive);
    }

    /// <summary>Title of the first visible top-level window belonging to each
    /// candidate process, keyed by PID. One enumeration serves everyone, and
    /// only candidates are asked for text: reading Process.MainWindowTitle per
    /// process would sweep the desktop once per chrome.exe helper, and a single
    /// hung window belonging to an unrelated app could stall the poll.</summary>
    private static Dictionary<uint, string> WindowTitles(List<(string Name, uint Pid)> candidates)
    {
        var wanted = new HashSet<uint>(candidates.Count);
        foreach (var (_, pid) in candidates) wanted.Add(pid);

        var titles = new Dictionary<uint, string>();
        if (wanted.Count == 0) return titles;
        try
        {
            EnumWindows((window, _) =>
            {
                if (!IsWindowVisible(window)) return true;
                GetWindowThreadProcessId(window, out var pid);
                if (pid == 0 || !wanted.Contains(pid) || titles.ContainsKey(pid)) return true;
                var length = GetWindowTextLength(window);
                if (length <= 0) return true;
                var buffer = new StringBuilder(length + 1);
                if (GetWindowText(window, buffer, buffer.Capacity) > 0) titles[pid] = buffer.ToString();
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // No desktop, or the window station is locked — no titles.
        }
        return titles;
    }

    /// <summary>The pure core, injectable for tests. Titles win over a hot
    /// mic — a browser or desktop window that names the call is the strongest
    /// evidence — and only then does a hot mic/camera light a merely-running
    /// desktop app. Browsers are excluded from that fallback: Chrome is always
    /// running, so a hot mic alone would match it constantly.</summary>
    public static CallMatch? Match(IEnumerable<ProcessWindow> processes, bool audioVisualActive = false)
    {
        var list = processes as IList<ProcessWindow> ?? processes.ToList();

        foreach (var window in list)
        {
            if (IsCallApp(window.ProcessName))
            {
                if (WindowLooksLikeCall(window.Title, window.ProcessName)) return Native(window.ProcessName);
            }
            else if (IsBrowser(window.ProcessName) && MatchWeb(window.ProcessName, window.Title) is { } web)
            {
                return web;
            }
        }

        if (!audioVisualActive) return null;

        foreach (var window in list)
        {
            if (IsCallApp(window.ProcessName)) return Native(window.ProcessName);
        }
        return null;
    }

    private static CallMatch Native(string processName) =>
        new(processName, processName.ToLowerInvariant(), DisplayName(processName));

    /// <summary>A browser window title that identifies a live web call, or null.
    /// Deliberately conservative: the service must be named *and* the title must
    /// show in-call evidence — a call keyword or a meeting code — so an idle
    /// "Google Meet" or "Chat | Microsoft Teams" tab doesn't light the pill.
    /// Meeting-only products (Whereby, Jitsi) skip that extra requirement.</summary>
    public static CallMatch? MatchWeb(string processName, string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var text = title.ToLowerInvariant();
        foreach (var site in WebSites)
        {
            if (!site.Signatures.Any(text.Contains)) continue;
            if (site.RequiresCode && !MeetingCode.IsMatch(title)) continue;
            if (!site.MeetingOnly && !site.RequiresCode && !CallWords.IsMatch(title) && !MeetingCode.IsMatch(title)) continue;
            return new CallMatch(processName, site.Key, site.Display);
        }
        return null;
    }

    /// <summary>A main window title that suggests a live call: a call/meeting
    /// keyword, or an elapsed-timer pattern like "9:51". The bare app name
    /// (e.g. "Discord") is not evidence of a call.</summary>
    public static bool WindowLooksLikeCall(Process process) =>
        WindowLooksLikeCall(TitleOf(process), process.ProcessName);

    /// <summary>Title-only twin of <see cref="WindowLooksLikeCall(Process)"/>,
    /// so the rule can be tested without a real process.</summary>
    public static bool WindowLooksLikeCall(string? title, string processName)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var trimmed = title.Trim();
        if (string.Equals(trimmed, processName, StringComparison.OrdinalIgnoreCase)) return false;
        return CallWords.IsMatch(trimmed) || Timer.IsMatch(trimmed);
    }

    /// <summary>Friendly display name for the pill ("ms-teams" → "Teams").</summary>
    public static string DisplayName(string processName) => processName.ToLowerInvariant() switch
    {
        "ms-teams" or "msteams" or "teams" => "Teams",
        "phonelink" => "Phone Link",
        "skypeapp" => "Skype",
        "lync" => "Skype for Business",
        "gotomeeting" => "GoTo Meeting",
        "ringcentral" => "RingCentral",
        "discordcanary" or "discordptb" => "Discord",
        "webex" or "webexmta" or "ciscowebexstart" => "Webex",
        "bluejeans" => "BlueJeans",
        "amazon chime" => "Chime",
        "wechat" or "weixin" => "WeChat",
        "kakaotalk" => "KakaoTalk",
        "dingtalk" => "DingTalk",
        "mattermost" or "mattermost-desktop" => "Mattermost",
        "element" or "element-desktop" => "Element",
        "rocket.chat" => "Rocket.Chat",
        "ts3client" or "teamspeak" => "TeamSpeak",
        "microsip" => "MicroSIP",
        "linphone" => "Linphone",
        "3cxphone" => "3CX Phone",
        "line" => "LINE",
        _ => Capitalize(processName),
    };

    /// <summary>Web-call services checked against browser tab titles, in order
    /// (first match wins — so Zoom precedes Meet, whose page name is a
    /// substring of "Zoom Meeting"). <c>MeetingOnly</c> marks products that
    /// exist purely for meetings, so the name alone is enough evidence;
    /// <c>RequiresCode</c> marks services whose in-call titles always carry a
    /// meeting code (so the idle "Google Meet" home page can't match).</summary>
    private static readonly WebSite[] WebSites =
    {
        new("jitsi", "Jitsi Meet", new[] { "jitsi" }, MeetingOnly: true),
        new("teams", "Teams", new[] { "microsoft teams", "teams | " }),
        new("zoom", "Zoom", new[] { "zoom" }),
        new("meet", "Google Meet", new[] { "google meet", "meet - ", "meet \u2013 " }, RequiresCode: true),
        new("slack", "Slack", new[] { "slack" }),
        new("discord", "Discord", new[] { "discord" }),
        new("whatsapp", "WhatsApp", new[] { "whatsapp" }),
        new("webex", "Webex", new[] { "webex" }),
        new("bluejeans", "BlueJeans", new[] { "bluejeans" }),
        new("google voice", "Google Voice", new[] { "google voice" }),
        new("messenger", "Messenger", new[] { "messenger" }),
        new("telegram", "Telegram", new[] { "telegram" }),
        new("amazon chime", "Chime", new[] { "amazon chime", "chime meeting" }),
        new("whereby", "Whereby", new[] { "whereby" }, MeetingOnly: true),
    };

    private sealed record WebSite(
        string Key,
        string Display,
        string[] Signatures,
        bool MeetingOnly = false,
        bool RequiresCode = false);

    /// <summary>Words that show a window is *in* a call rather than just the app
    /// being open. "Meetings"/"Calls" are deliberately absent: they are tab
    /// names in idle Teams and Webex windows, not calls.</summary>
    private static readonly Regex CallWords = new(
        @"\b(call|calling|call with|meeting|webinar|huddle|ringing|conference|voice connected|voice)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>An elapsed timer such as "9:51" — a strong in-call signal in a
    /// native window (Discord and Zoom both put one in the title).</summary>
    private static readonly Regex Timer = new(
        @"\d{1,3}:\d{2}",
        RegexOptions.Compiled);

    /// <summary>Meet/Jitsi style meeting code, e.g. "abc-defg-hij".</summary>
    private static readonly Regex MeetingCode = new(
        @"\b[a-z]{3}-[a-z]{4}-[a-z]{3}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? TitleOf(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch
        {
            return null;
        }
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW", ExactSpelling = true)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW", ExactSpelling = true)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private static string Capitalize(string name)
    {
        if (name.Length == 0) return name;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
