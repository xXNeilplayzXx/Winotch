using System.Diagnostics;

namespace Winotch;

/// <summary>A live call: the detected app plus when the call began (for the
/// ticking pill timer). Null means no call.</summary>
public sealed record CallState(bool OnCall, string? AppName, DateTimeOffset StartedAtUtc);

/// <summary>Detects desktop call apps by process name — the same approach the
/// legacy app used (known-app list; browser-based calls are not detectable).
/// The pill appears while a known app is running; mic state adds the nuance
/// for the status dot. Process scanning is injectable for tests.</summary>
public static class CallDetector
{
    /// <summary>Known desktop call apps, lowercase process names. Order matters
    /// only for which name wins when several are running (first match).</summary>
    public static readonly string[] CallApps =
    {
        "zoom", "ms-teams", "teams", "msteams", "discord", "slack", "skype",
        "skypeapp", "whatsapp", "phonelink", "telegram", "webex", "meeting",
        "gotomeeting", "ringcentral", "signal", "line",
    };

    public static bool IsCallApp(string processName) =>
        CallApps.Contains(processName.ToLowerInvariant());

    /// <summary>First running call app with evidence of an actual call, or
    /// null. "Evidence" is a hot mic/camera (the user is in a meeting) or a
    /// call-like main window title — just running Discord/Slack in the
    /// background no longer lights the green pill, which is what the green
    /// bubble was for.</summary>
    public static string? FindCallApp(bool audioVisualActive = false)
    {
        try
        {
            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        if (!IsCallApp(process.ProcessName)) continue;
                        if (audioVisualActive) return process.ProcessName;
                        if (WindowLooksLikeCall(process)) return process.ProcessName;
                    }
                    catch
                    {
                        // A dying process can fail to report its name.
                    }
                }
            }
        }
        catch
        {
            // Process enumeration can fail under heavy load or lockdown.
        }
        return null;
    }

    /// <summary>A main window title that suggests a live call: a call/meeting/
    /// ringing keyword, or an elapsed-timer pattern like "9:51". The bare app
    /// name (e.g. "Discord") is not evidence of a call.</summary>
    public static bool WindowLooksLikeCall(Process process)
    {
        try
        {
            var title = process.MainWindowTitle?.Trim();
            if (string.IsNullOrWhiteSpace(title)) return false;
            if (string.Equals(title, process.ProcessName, StringComparison.OrdinalIgnoreCase)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(title,
                @"\b(call|calling|in call|meeting|ringing|conference|voice)\b|\d{1,3}:\d{2}",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Friendly display name for the pill ("ms-teams" → "Teams").</summary>
    public static string DisplayName(string processName) => processName.ToLowerInvariant() switch
    {
        "ms-teams" or "msteams" => "Teams",
        "phonelink" => "Phone Link",
        "skypeapp" => "Skype",
        "gotomeeting" => "GoTo Meeting",
        "line" => "LINE",
        _ => Capitalize(processName),
    };

    private static string Capitalize(string name)
    {
        if (name.Length == 0) return name;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}