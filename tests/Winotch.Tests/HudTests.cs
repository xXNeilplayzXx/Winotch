using Winotch;
using Winotch.Engine;

namespace Winotch.Tests;

public class HudKeyTests
{
    [Theory]
    [InlineData(HudKeys.VolumeUpVirtualKey, HudKeyAction.VolumeUp)]
    [InlineData(HudKeys.VolumeDownVirtualKey, HudKeyAction.VolumeDown)]
    [InlineData(HudKeys.VolumeMuteVirtualKey, HudKeyAction.VolumeMute)]
    // Anything else must pass straight through to Windows.
    [InlineData(0x41, HudKeyAction.None)] // A
    [InlineData(0xB3, HudKeyAction.None)] // play / pause — media keys stay put
    [InlineData(0x7B, HudKeyAction.None)] // F12
    public void Classify_ClaimsOnlyTheVolumeKeys(int virtualKey, HudKeyAction expected) =>
        Assert.Equal(expected, HudKeys.Classify(virtualKey));

    [Fact]
    public void TakesOver_NeedsTheHudAwakeAndAnEndpointToActOn()
    {
        const int up = HudKeys.VolumeUpVirtualKey;

        Assert.True(HudKeys.TakesOver(up, hudEnabled: true, awake: true, volumeAvailable: true));
        // A swallowed key we can't apply would leave the user with no volume
        // control at all, so every condition has to hold.
        Assert.False(HudKeys.TakesOver(up, hudEnabled: false, awake: true, volumeAvailable: true));
        Assert.False(HudKeys.TakesOver(up, hudEnabled: true, awake: false, volumeAvailable: true));
        Assert.False(HudKeys.TakesOver(up, hudEnabled: true, awake: true, volumeAvailable: false));
    }

    [Fact]
    public void TakesOver_LeavesEveryOtherKeyAlone()
    {
        Assert.False(HudKeys.TakesOver(0x41, hudEnabled: true, awake: true, volumeAvailable: true));
    }

    [Fact]
    public void Step_MovesByTheWindowsStepAndStopsAtTheEnds()
    {
        Assert.Equal(0.42, HudKeys.Step(0.40f, HudKeyAction.VolumeUp), 3);
        Assert.Equal(0.38, HudKeys.Step(0.40f, HudKeyAction.VolumeDown), 3);
        Assert.Equal(0.0, HudKeys.Step(0.01f, HudKeyAction.VolumeDown), 3);
        Assert.Equal(1.0, HudKeys.Step(0.99f, HudKeyAction.VolumeUp), 3);
        // The mute key is a toggle, not a step: the level stays where it was.
        Assert.Equal(0.4f, HudKeys.Step(0.40f, HudKeyAction.VolumeMute));
        Assert.Equal(0.4f, HudKeys.Step(0.40f, HudKeyAction.None));
    }

    [Fact]
    public void Unmutes_ForTheLevelKeysOnly()
    {
        Assert.True(HudKeys.Unmutes(HudKeyAction.VolumeUp));
        Assert.True(HudKeys.Unmutes(HudKeyAction.VolumeDown));
        Assert.False(HudKeys.Unmutes(HudKeyAction.VolumeMute));
        Assert.False(HudKeys.Unmutes(HudKeyAction.None));
    }

    [Theory]
    [InlineData(HudKind.Volume, 0.42, false, "42%")]
    [InlineData(HudKind.Volume, 0.426, false, "43%")]
    [InlineData(HudKind.Volume, 0.0, false, "0%")]
    [InlineData(HudKind.Volume, 1.0, false, "100%")]
    [InlineData(HudKind.Brightness, 0.7, false, "70%")]
    // A muted endpoint still reports the level it had, so the readout says so.
    [InlineData(HudKind.Volume, 0.42, true, "Muted")]
    public void Label_ReadsOutTheLevelOrMuted(HudKind kind, double level, bool muted, string expected) =>
        Assert.Equal(expected, HudKeys.Label(kind, level, muted));
}

public class HudTimelineTests
{
    [Fact]
    public void Hud_FadesInHoldsThenExpires()
    {
        var hud = new Hud(new HudSpec(HudKind.Volume, 0.5));

        Assert.Equal(0, hud.Opacity, 3);

        hud.Tick(0.05);
        Assert.True(hud.Opacity is > 0 and < 1);

        hud.Tick(0.2);
        Assert.Equal(1, hud.Opacity, 3);
        Assert.False(hud.Expired);

        hud.Tick(2.0);
        Assert.True(hud.Expired);
        Assert.Equal(0, hud.Opacity, 3);
    }

    [Fact]
    public void Hud_RefreshSlidesTheBarAndRestartsTheClock()
    {
        var hud = new Hud(new HudSpec(HudKind.Volume, 0.2));

        hud.Refresh(new HudSpec(HudKind.Volume, 0.6));
        Assert.Equal(0.2, hud.Fill, 3);

        // A short tick eases the bar part of the way, so a keypress slides it
        // instead of jumping — and the refresh put the HUD back on screen.
        hud.Tick(0.02);
        Assert.True(hud.Fill is > 0.2 and < 0.6);

        hud.Tick(0.2);
        Assert.Equal(1, hud.Opacity, 3);

        for (var i = 0; i < 80; i++) hud.Tick(0.02);
        Assert.Equal(0.6, hud.Fill, 3);
    }

    [Fact]
    public void Hud_StartsAtTheReadingItWasGiven()
    {
        var hud = new Hud(new HudSpec(HudKind.Brightness, 0.35));

        Assert.Equal(0.35, hud.Fill, 3);
        Assert.Equal(0.35, hud.Level01, 3);
        Assert.False(hud.Muted);
        Assert.Equal(35, hud.Percent);
    }

    [Fact]
    public void HudFeatures_DefaultOn()
    {
        var features = new FeatureSettings();

        Assert.True(features.VolumeHudEnabled);
        Assert.True(features.BrightnessHudEnabled);
        Assert.True(features.MediaHudEnabled);
    }
}
