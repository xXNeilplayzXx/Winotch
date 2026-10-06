using Winotch;
using Winotch.Engine;

namespace Winotch.Tests;

public class FadeTimelineTests
{
    [Fact]
    public void Opacity_FadesInHoldsThenFadesOut()
    {
        var timeline = new FadeTimeline(0.1, 1.0, 0.25);

        Assert.Equal(0, timeline.Opacity, 3);

        timeline.Tick(0.05);
        Assert.InRange(timeline.Opacity, 0.01, 0.99);

        timeline.Tick(0.05); // age 0.10 — fully in
        Assert.Equal(1, timeline.Opacity, 3);
        Assert.False(timeline.Expired);

        timeline.Tick(0.9); // age 1.00 — the end of the hold, still opaque
        Assert.Equal(1, timeline.Opacity, 3);
        Assert.False(timeline.Expired);

        timeline.Tick(0.125); // age 1.125 — mid fade-out
        Assert.InRange(timeline.Opacity, 0.01, 0.99);
        Assert.False(timeline.Expired);

        timeline.Tick(0.13); // past hold + fade-out
        Assert.Equal(0, timeline.Opacity, 3);
        Assert.True(timeline.Expired);
    }

    [Fact]
    public void Restart_PutsItBackOnScreen()
    {
        var timeline = new FadeTimeline(0.1, 0.5, 0.2);
        timeline.Tick(1);
        Assert.True(timeline.Expired);

        timeline.Restart();
        Assert.False(timeline.Expired);
        Assert.Equal(0, timeline.Age, 3);
    }

    [Fact]
    public void ZeroFadeOut_EndsInsteadOfDividingByZero()
    {
        var timeline = new FadeTimeline(0, 0.5, 0);
        Assert.Equal(1, timeline.Opacity, 3);

        timeline.Tick(0.6);
        Assert.Equal(0, timeline.Opacity, 3);
        Assert.True(timeline.Expired);
    }
}

public class PillFaceStackTests
{
    private static GlanceFace Glance(string label) => new(new GlanceSpec("📋", label, null, 3), 208);

    private static LevelFace Level(double level01) =>
        new(new Hud(new HudSpec(HudKind.Volume, level01)), 208);

    [Fact]
    public void Empty_LeavesTheRowInCharge()
    {
        var stack = new PillFaceStack();

        Assert.Null(stack.Live);
        Assert.False(stack.NeedsLiveUpdates);
        Assert.False(stack.Any);
    }

    [Fact]
    public void NewestTransientFaceWins()
    {
        var stack = new PillFaceStack();
        var level = Level(0.4);
        stack.Add(level);
        Assert.Same(level, stack.Live);

        // The pill always answers the last thing the user did — a level slider
        // must never lock out a chip that came later.
        var glance = Glance("Copied");
        stack.Add(glance);
        Assert.Same(glance, stack.Live);
    }

    [Fact]
    public void StickyDragModeOutranksANewerTransient()
    {
        var stack = new PillFaceStack();
        var drag = new DragFace(180);
        stack.Add(drag);
        stack.Add(Glance("Charging"));

        Assert.Same(drag, stack.Live);

        stack.Remove(PillFaceKind.Drag);
        Assert.Equal(PillFaceKind.Glance, stack.Live!.Kind);
    }

    [Fact]
    public void Add_ReplacesAFaceOfTheSameKind()
    {
        var stack = new PillFaceStack();
        var first = Glance("One");
        stack.Add(first);
        var second = Glance("Two");
        stack.Add(second);

        Assert.Equal(1, stack.Count);
        Assert.Same(second, stack.Live);
    }

    [Fact]
    public void Touch_LiftsARefreshedFaceBackToTheTop()
    {
        var stack = new PillFaceStack();
        var level = Level(0.2);
        stack.Add(level);
        stack.Add(Glance("Copied"));
        Assert.Equal(PillFaceKind.Glance, stack.Live!.Kind);

        // A volume keypress folds into the live slider rather than replacing it,
        // so the slider has to come back on top explicitly.
        stack.Touch(level);
        Assert.Same(level, stack.Live);
    }

    [Fact]
    public void Tick_DropsExpiredFacesAndFallsBackToTheNextOne()
    {
        var stack = new PillFaceStack();
        var level = Level(0.5);
        stack.Add(level);
        var glance = Glance("Copied");
        stack.Add(glance);

        // The slider (1.41s) dies well before the chip (3s): the pill falls back
        // to the chip rather than to the row.
        stack.Tick(2);
        Assert.Same(glance, stack.Live);
        Assert.Equal(1, stack.Count);

        stack.Tick(4);
        Assert.Null(stack.Live);
        Assert.Equal(0, stack.Count);
        Assert.False(stack.NeedsLiveUpdates);
    }

    [Fact]
    public void Clear_TakesEverythingOffThePill()
    {
        var stack = new PillFaceStack();
        stack.Add(new DragFace(180));
        stack.Add(Level(0.5));

        stack.Clear();
        Assert.Null(stack.Live);
        Assert.False(stack.Any);
    }

    [Fact]
    public void Find_ReturnsTheLiveFaceOfAType()
    {
        var stack = new PillFaceStack();
        Assert.Null(stack.Find<LevelFace>());

        var level = Level(0.5);
        stack.Add(level);
        Assert.Same(level, stack.Find<LevelFace>());
        Assert.Null(stack.Find<TransportFace>());
    }
}

public class PillFaceTests
{
    [Fact]
    public void DragFace_IsStickyAndNeverExpires()
    {
        var face = new DragFace(180);

        Assert.True(face.Sticky);
        Assert.Equal(180, face.Span, 3);

        face.Tick(60);
        Assert.False(face.Expired);
    }

    [Fact]
    public void LevelFace_ExpiresWithItsHud()
    {
        var face = new LevelFace(new Hud(new HudSpec(HudKind.Brightness, 0.6)), 208);

        face.Tick(1);
        Assert.False(face.Expired);

        face.Tick(1);
        Assert.True(face.Expired);
    }

    [Fact]
    public void TransportFace_ExpiresAfterItsShortHold()
    {
        var face = new TransportFace(
            new TransportSpec(MediaKeyAction.Next, "\uE893", "Next track"), 208);

        Assert.Equal(208, face.Span, 3);
        Assert.False(face.Sticky);

        face.Tick(1.0); // still inside the hold
        Assert.Equal(1, face.Opacity, 3);
        Assert.False(face.Expired);

        face.Tick(0.5);
        Assert.Equal(0, face.Opacity, 3);
        Assert.True(face.Expired);
    }

    [Fact]
    public void GlanceFace_ClampsItsFillAndKnowsWhenItHasOne()
    {
        var charging = new GlanceFace(
            new GlanceSpec("⚡", "Charger connected", "82% and charging", 3.6, 0.82), 208);
        Assert.True(charging.HasFill);
        Assert.Equal(0.82, charging.Fill, 3);

        var over = new GlanceFace(new GlanceSpec("⚡", "x", null, 3.6, 4), 208);
        Assert.Equal(1, over.Fill, 3);

        var plain = new GlanceFace(new GlanceSpec("🌧️", "Rain soon"), 208);
        Assert.False(plain.HasFill);
        Assert.Equal(0, plain.Fill, 3);
    }

    [Fact]
    public void GlanceFace_KeepsEvenAZeroSecondSpecOnScreenLongEnoughToRead()
    {
        var face = new GlanceFace(new GlanceSpec("x", "y", null, 0), 208);

        face.Tick(0.5);
        Assert.False(face.Expired);
    }
}

public class MediaKeyTests
{
    [Theory]
    [InlineData(MediaKeys.PlayPauseVirtualKey, MediaKeyAction.PlayPause)]
    [InlineData(MediaKeys.NextTrackVirtualKey, MediaKeyAction.Next)]
    [InlineData(MediaKeys.PrevTrackVirtualKey, MediaKeyAction.Prev)]
    // The volume keys belong to the level HUD, not here.
    [InlineData(0xAF, MediaKeyAction.None)]
    [InlineData(0x41, MediaKeyAction.None)] // A
    public void Classify_KnowsOnlyTheMediaKeys(int virtualKey, MediaKeyAction expected) =>
        Assert.Equal(expected, MediaKeys.Classify(virtualKey));

    [Fact]
    public void VirtualKeys_KeepWindowsOrderWhereNextComesFirst()
    {
        // VK_MEDIA_NEXT_TRACK is 0xB0 and VK_MEDIA_PREV_TRACK is 0xB1 — next
        // before previous, the reverse of what the names suggest.
        Assert.Equal(0xB0, MediaKeys.NextTrackVirtualKey);
        Assert.Equal(0xB1, MediaKeys.PrevTrackVirtualKey);
        Assert.Equal(0xB3, MediaKeys.PlayPauseVirtualKey);
    }

    [Fact]
    public void TheTwoKeySets_DoNotOverlap()
    {
        // If a media key were also a volume key, the hook would either swallow a
        // key it shouldn't or mirror one twice.
        foreach (var key in new[] { MediaKeys.PlayPauseVirtualKey, MediaKeys.NextTrackVirtualKey, MediaKeys.PrevTrackVirtualKey })
        {
            Assert.Equal(HudKeyAction.None, HudKeys.Classify(key));
        }
        foreach (var key in new[] { HudKeys.VolumeUpVirtualKey, HudKeys.VolumeDownVirtualKey, HudKeys.VolumeMuteVirtualKey })
        {
            Assert.Equal(MediaKeyAction.None, MediaKeys.Classify(key));
        }
    }

    [Fact]
    public void Observes_NeedsTheSettingAwakeAndAMediaKey()
    {
        Assert.True(MediaKeys.Observes(MediaKeys.PlayPauseVirtualKey, enabled: true, awake: true));
        Assert.False(MediaKeys.Observes(MediaKeys.PlayPauseVirtualKey, enabled: false, awake: true));
        Assert.False(MediaKeys.Observes(MediaKeys.PlayPauseVirtualKey, enabled: true, awake: false));
        Assert.False(MediaKeys.Observes(0x41, enabled: true, awake: true));
        Assert.False(MediaKeys.Observes(0, enabled: true, awake: true));
    }

    [Fact]
    public void Glyph_SaysWhatThePressWillDo()
    {
        Assert.Equal("\uE768", MediaKeys.Glyph(MediaKeyAction.PlayPause, playing: false));
        Assert.Equal("\uE769", MediaKeys.Glyph(MediaKeyAction.PlayPause, playing: true));
        Assert.Equal("\uE893", MediaKeys.Glyph(MediaKeyAction.Next, playing: true));
        Assert.Equal("\uE892", MediaKeys.Glyph(MediaKeyAction.Prev, playing: false));
    }

    [Fact]
    public void Label_NamesTheActionRatherThanAClaimedState()
    {
        Assert.Equal("Play / pause", MediaKeys.Label(MediaKeyAction.PlayPause));
        Assert.Equal("Next track", MediaKeys.Label(MediaKeyAction.Next));
        Assert.Equal("Previous track", MediaKeys.Label(MediaKeyAction.Prev));
        Assert.Equal("", MediaKeys.Label(MediaKeyAction.None));
    }
}
