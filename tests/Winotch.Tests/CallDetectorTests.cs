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
    [InlineData("explorer", false)]
    [InlineData("chrome", false)]
    [InlineData("Code", false)]
    public void IsCallApp_RecognizesKnownApps(string processName, bool expected)
    {
        Assert.Equal(expected, CallDetector.IsCallApp(processName));
    }

    [Fact]
    public void DisplayName_PrettyPrints()
    {
        Assert.Equal("Teams", CallDetector.DisplayName("ms-teams"));
        Assert.Equal("Teams", CallDetector.DisplayName("MSTEAMS"));
        Assert.Equal("Phone Link", CallDetector.DisplayName("PhoneLink"));
        Assert.Equal("Zoom", CallDetector.DisplayName("zoom"));
        Assert.Equal("Skype", CallDetector.DisplayName("skypeapp"));
        Assert.Equal("Discord", CallDetector.DisplayName("discord"));
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