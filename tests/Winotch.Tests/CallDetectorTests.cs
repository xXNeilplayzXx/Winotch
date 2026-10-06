using Winotch;

namespace Winotch.Tests;

public class CallDetectorTests
{
    [Theory]
    [InlineData("Zoom", true)]
    [InlineData("zoom", true)]
    [InlineData("ms-teams", true)]
    [InlineData("Discord", true)]
    [InlineData("phonelink", true)]
    // Canary/PTB builds ship under their own process names.
    [InlineData("DiscordCanary", true)]
    [InlineData("discordptb", true)]
    [InlineData("teams", true)]
    [InlineData("lync", true)]
    [InlineData("WebexMTA", true)]
    [InlineData("ciscowebexstart", true)]
    [InlineData("BlueJeans", true)]
    [InlineData("Amazon Chime", true)]
    [InlineData("Viber", true)]
    [InlineData("Zoiper", true)]
    [InlineData("3cxphone", true)]
    [InlineData("explorer", false)]
    [InlineData("chrome", false)]
    [InlineData("Code", false)]
    public void IsCallApp_RecognizesKnownApps(string processName, bool expected)
    {
        Assert.Equal(expected, CallDetector.IsCallApp(processName));
    }

    [Theory]
    [InlineData("chrome", true)]
    [InlineData("edge", false)]
    [InlineData("msedge", true)]
    [InlineData("firefox", true)]
    [InlineData("Brave", true)]
    [InlineData("discord", false)]
    public void IsBrowser_RecognizesBrowsers(string processName, bool expected)
    {
        Assert.Equal(expected, CallDetector.IsBrowser(processName));
    }

    [Fact]
    public void ProcessLists_AreLowercaseAndUnique()
    {
        Assert.All(CallDetector.CallApps, app => Assert.Equal(app.ToLowerInvariant(), app));
        Assert.All(CallDetector.Browsers, browser => Assert.Equal(browser.ToLowerInvariant(), browser));
        Assert.Equal(CallDetector.CallApps.Length, CallDetector.CallApps.Distinct().Count());
        Assert.Equal(CallDetector.Browsers.Length, CallDetector.Browsers.Distinct().Count());
    }

    [Fact]
    public void DisplayName_PrettyPrints()
    {
        Assert.Equal("Teams", CallDetector.DisplayName("ms-teams"));
        Assert.Equal("Teams", CallDetector.DisplayName("MSTEAMS"));
        Assert.Equal("Teams", CallDetector.DisplayName("teams"));
        Assert.Equal("Phone Link", CallDetector.DisplayName("PhoneLink"));
        Assert.Equal("Zoom", CallDetector.DisplayName("zoom"));
        Assert.Equal("Skype", CallDetector.DisplayName("skypeapp"));
        Assert.Equal("Discord", CallDetector.DisplayName("discord"));
        Assert.Equal("Discord", CallDetector.DisplayName("discordcanary"));
        Assert.Equal("Skype for Business", CallDetector.DisplayName("lync"));
        Assert.Equal("Webex", CallDetector.DisplayName("webexmta"));
        Assert.Equal("Chime", CallDetector.DisplayName("Amazon Chime"));
        Assert.Equal("3CX Phone", CallDetector.DisplayName("3cxphone"));
        Assert.Equal("Mattermost", CallDetector.DisplayName("mattermost-desktop"));
        Assert.Equal("WeChat", CallDetector.DisplayName("weixin"));
        Assert.Equal("LINE", CallDetector.DisplayName("line"));
    }

    [Theory]
    // A live call: a call-ish keyword or an elapsed timer in the title.
    [InlineData("Discord | #alice", "Discord", false)]
    [InlineData("Zoom Meeting", "zoom", true)]
    [InlineData("Meeting with Alice | Microsoft Teams", "ms-teams", true)]
    [InlineData("Huddle: #general | Acme - Slack", "slack", true)]
    [InlineData("Alice — 9:51", "discord", true)]
    [InlineData("Calling Bob…", "signal", true)]
    // Just running the app is not evidence of a call.
    [InlineData("Discord", "Discord", false)]
    [InlineData("Zoom Workplace", "zoom", false)]
    [InlineData("Chat | Microsoft Teams", "ms-teams", false)]
    [InlineData("Cisco Webex Meetings", "webexmta", false)]
    [InlineData("Calls | Microsoft Teams", "teams", false)]
    [InlineData("", "discord", false)]
    [InlineData(null, "discord", false)]
    [InlineData("   ", "discord", false)]
    public void WindowLooksLikeCall_NeedsEvidence(string? title, string processName, bool expected)
    {
        Assert.Equal(expected, CallDetector.WindowLooksLikeCall(title, processName));
    }

    [Theory]
    // In-call web titles.
    [InlineData("Meet - abc-defg-hij - Google Chrome", "Google Meet")]
    [InlineData("Google Meet - abc-defg-hij", "Google Meet")]
    [InlineData("Zoom Meeting - Google Chrome", "Zoom")]
    [InlineData("Call | Microsoft Teams", "Teams")]
    [InlineData("Meeting with Alice | Microsoft Teams - Google Chrome", "Teams")]
    [InlineData("Slack | huddle - Google Chrome", "Slack")]
    [InlineData("Discord | Voice Connected", "Discord")]
    [InlineData("Call with Bob - Google Voice", "Google Voice")]
    [InlineData("Jitsi Meet - Google Chrome", "Jitsi Meet")]
    [InlineData("Whereby", "Whereby")]
    [InlineData("Voice call | WhatsApp", "WhatsApp")]
    public void MatchWeb_FindsLiveWebCalls(string title, string expected)
    {
        var match = CallDetector.MatchWeb("chrome", title);
        Assert.Equal(expected, match?.Display);
        Assert.Equal("chrome", match?.ProcessName);
    }

    [Theory]
    // Idle tabs and lookalike titles must not light the pill.
    [InlineData("Google Meet - Google Chrome")]
    [InlineData("Chat | Microsoft Teams - Google Chrome")]
    [InlineData("Discord | #general")]
    [InlineData("WhatsApp Web")]
    [InlineData("YouTube - Google Chrome")]
    [InlineData("Meeting notes - Google Docs")]
    [InlineData("How to plan a meeting - Google Search")]
    [InlineData("Netflix")]
    [InlineData("")]
    [InlineData(null)]
    public void MatchWeb_IgnoresIdleTabs(string? title)
    {
        Assert.Null(CallDetector.MatchWeb("chrome", title));
    }

    [Fact]
    public void Match_PrefersATitledWindow_OverAHotMic()
    {
        // A Meet call in the browser beats Discord merely being open, even
        // though the mic is hot and Discord appears first in the scan.
        var processes = new[]
        {
            new ProcessWindow("Discord", "Discord"),
            new ProcessWindow("chrome", "Meet - abc-defg-hij - Google Chrome"),
        };

        var match = CallDetector.Match(processes, audioVisualActive: true);

        Assert.Equal("Google Meet", match?.Display);
        Assert.Equal("meet", match?.Key);
    }

    [Fact]
    public void Match_UsesHotMic_ForARunningDesktopApp()
    {
        var processes = new[] { new ProcessWindow("discord", "Discord") };

        Assert.Null(CallDetector.Match(processes, audioVisualActive: false));
        Assert.Equal("Discord", CallDetector.Match(processes, audioVisualActive: true)?.Display);
    }

    [Fact]
    public void Match_NeverUsesHotMicAlone_ForABrowser()
    {
        // Chrome is always running, so a hot mic on its own must not look like
        // a call — otherwise the pill would claim you're on a call all day.
        var processes = new[] { new ProcessWindow("chrome", "Google Chrome") };

        Assert.Null(CallDetector.Match(processes, audioVisualActive: true));
    }

    [Fact]
    public void Match_FindsANativeAppByItsTitle_WithoutMicEvidence()
    {
        var processes = new[] { new ProcessWindow("zoom", "Zoom Meeting") };

        var match = CallDetector.Match(processes, audioVisualActive: false);

        Assert.Equal("Zoom", match?.Display);
        Assert.Equal("zoom", match?.Key);
    }

    [Fact]
    public void Match_ReturnsNull_WhenNothingIsRunning()
    {
        Assert.Null(CallDetector.Match(Array.Empty<ProcessWindow>(), audioVisualActive: true));
    }

    [Fact]
    public void FindCallApp_NeverThrows_AgainstTheRealProcessList()
    {
        // End-to-end smoke test of the window-title scan: whatever is or isn't
        // running (and even where there's no desktop to enumerate), detection
        // must answer, not crash the poll that drives the pill.
        var exception = Record.Exception(() => CallDetector.FindCallApp(audioVisualActive: true));

        Assert.Null(exception);
    }

    [Fact]
    public void CallState_CarriesStartTime_ForTickingTimer()
    {
        var start = new DateTimeOffset(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);
        var state = new CallState(true, "Teams", start);
        Assert.True(state.OnCall);
        Assert.Equal("Teams", state.AppName);
        Assert.Equal(start, state.StartedAtUtc);
    }
}
