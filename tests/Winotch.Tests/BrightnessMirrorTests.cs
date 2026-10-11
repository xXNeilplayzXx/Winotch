using Winotch.Engine;

namespace Winotch.Tests;

public class BrightnessMirrorTests
{
    [Fact]
    public void FirstRead_OnlySeedsSoNothingFiresAtLaunch()
    {
        var mirror = new BrightnessMirror();

        Assert.Null(mirror.Observe(60, null));
        // The very next identical poll is just as quiet.
        Assert.Null(mirror.Observe(60, null));

        Assert.Equal(new BrightnessReading(40, BrightnessSource.Monitor), mirror.Observe(40, null));
    }

    [Fact]
    public void InternalPanel_RaisesItsOwnReading()
    {
        var mirror = new BrightnessMirror();
        mirror.Observe(null, 80);

        var reading = mirror.Observe(null, 55);

        Assert.Equal(new BrightnessReading(55, BrightnessSource.Internal), reading);
    }

    [Fact]
    public void TheTwoReadersAreIndependent()
    {
        var mirror = new BrightnessMirror();
        mirror.Observe(70, 80);

        // A monitor change must not be mistaken for a panel change, and it must
        // not disturb the panel's remembered level.
        Assert.Equal(new BrightnessReading(65, BrightnessSource.Monitor), mirror.Observe(65, 80));
        Assert.Equal(new BrightnessReading(40, BrightnessSource.Internal), mirror.Observe(65, 40));
        Assert.Null(mirror.Observe(65, 40));
    }

    [Fact]
    public void BothMovingInOnePoll_PrefersThePanelTheBrightnessKeysDrive()
    {
        var mirror = new BrightnessMirror();
        mirror.Observe(70, 80);

        Assert.Equal(new BrightnessReading(50, BrightnessSource.Internal), mirror.Observe(20, 50));
    }

    [Fact]
    public void ASourceAppearingLaterIsNotAChange()
    {
        var mirror = new BrightnessMirror();
        // Launch on a desktop: the panel reader never answers.
        mirror.Observe(60, null);

        // A docked laptop — or a slow first handshake — starts answering. That
        // is an interface appearing, not the user dimming anything.
        Assert.Null(mirror.Observe(60, 90));
        Assert.Equal(new BrightnessReading(70, BrightnessSource.Internal), mirror.Observe(60, 70));
    }

    [Fact]
    public void AFailedRead_KeepsTheLastValue()
    {
        var mirror = new BrightnessMirror();
        mirror.Observe(60, null);

        // DDC/CI drops a beat (a mode switch, a display waking). The level is
        // unknown, not zero, so coming back at the same level is not a change.
        Assert.Null(mirror.Observe(null, null));
        Assert.Null(mirror.Observe(60, null));
        Assert.Equal(new BrightnessReading(45, BrightnessSource.Monitor), mirror.Observe(45, null));
    }

    [Fact]
    public void Source_NamesTheInterfaceThatAnswers()
    {
        var mirror = new BrightnessMirror();
        Assert.Equal(BrightnessSource.None, mirror.Source);

        mirror.Observe(60, null);
        Assert.Equal(BrightnessSource.Monitor, mirror.Source);

        // The panel is the one brightness keys move, so with both live it is the
        // honest answer.
        mirror.Observe(60, 80);
        Assert.Equal(BrightnessSource.Internal, mirror.Source);

        // A failed poll must not make the Settings line flap to "unavailable".
        mirror.Observe(null, null);
        Assert.Equal(BrightnessSource.Internal, mirror.Source);
    }

    [Fact]
    public void Source_SurvivesASourceThatStopsAnswering()
    {
        var mirror = new BrightnessMirror();
        mirror.Observe(60, null);

        // The panel reader is silent on this machine forever; the monitor's
        // repeated answer is still the monitor.
        mirror.Observe(62, null);
        Assert.Equal(BrightnessSource.Monitor, mirror.Source);
    }

    [Theory]
    [InlineData(BrightnessSource.Internal, "Mirroring this display's brightness (Windows backlight)")]
    [InlineData(BrightnessSource.Monitor, "Mirroring the monitor's brightness (DDC/CI)")]
    [InlineData(BrightnessSource.None, "This display doesn't report brightness, so the HUD can't appear yet")]
    public void Describe_NamesTheLiveInterface(BrightnessSource source, string expected) =>
        Assert.Equal(expected, BrightnessMirror.Describe(source, enabled: true));

    [Fact]
    public void Describe_FallsBackToThePlainSubtitleWhenTheHudIsOff() =>
        Assert.Equal("Mirror brightness changes onto the pill (your PC may show its own too)",
            BrightnessMirror.Describe(BrightnessSource.Internal, enabled: false));
}
