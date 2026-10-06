using Winotch;

namespace Winotch.Tests;

public class PillSettingsTests
{
    [Fact]
    public void Settings_RoundTrip_PreservesPillSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winotch-pill-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(path);
            settings.Update(s => s with
            {
                General = s.General with { OpenDirection = ExpandDirection.Horizontal },
                Appearance = new AppearanceSettings
                {
                    CustomSize = true,
                    Width = 500,
                    Height = 60,
                    SizePreset = "wide",
                    IdleContent = IdleContentMode.Custom,
                    CustomIdleText = "Lunch at 1",
                    VerticalText = true,
                    OffsetX = 120,
                    OffsetY = 40,
                },
            });

            var reloaded = new SettingsService(path);
            Assert.Equal(ExpandDirection.Horizontal, reloaded.Current.General.OpenDirection);
            var appearance = reloaded.Current.Appearance;
            Assert.True(appearance.CustomSize);
            Assert.Equal(500, appearance.Width);
            Assert.Equal(60, appearance.Height);
            Assert.Equal("wide", appearance.SizePreset);
            Assert.Equal(IdleContentMode.Custom, appearance.IdleContent);
            Assert.Equal("Lunch at 1", appearance.CustomIdleText);
            Assert.True(appearance.VerticalText);
            Assert.Equal(120, appearance.OffsetX);
            Assert.Equal(40, appearance.OffsetY);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SettingsSections_RoundTrip_KeepsOnlyKnownIds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winotch-sections-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(path);
            settings.Update(s => s with
            {
                General = s.General with
                {
                    // A hand-edited file could name anything: unknown ids,
                    // duplicates and blanks are dropped rather than trusted.
                    OpenSettingsSections = new List<string> { "Appearance", "pill", "pill", "nonsense", "  " },
                },
            });

            var sections = new SettingsService(path).Current.General.OpenSettingsSections;
            Assert.Equal(new[] { "appearance", "pill" }, sections);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SettingsSections_DefaultToNoneOpen()
    {
        // Every section ships folded, so a first run isn't a wall of rows.
        Assert.Empty(new GeneralSettings().OpenSettingsSections);
        Assert.Empty(new WinotchSettings().Normalize().General.OpenSettingsSections);
        // A null list out of a hand-edited file must not throw on load.
        var nulled = new WinotchSettings
        {
            General = new GeneralSettings { OpenSettingsSections = null! },
        }.Normalize();
        Assert.Empty(nulled.General.OpenSettingsSections);
    }

    [Fact]
    public void SizePresets_Find_KnownAndUnknown()
    {
        Assert.NotNull(SizePresets.Find("compact"));
        Assert.Equal("Wide", SizePresets.Find("wide")!.Value.Label);
        Assert.Null(SizePresets.Find("huge"));
        Assert.Null(SizePresets.Find(null));
    }

    [Fact]
    public void IdleContent_Normalize_KeepsUnknownSafe()
    {
        // Normalize must not touch valid enum values and defaults to Auto.
        var normalized = new AppearanceSettings
        {
            IdleContent = IdleContentMode.DotOnly,
            CustomIdleText = null!,
        }.Normalize();
        Assert.Equal(IdleContentMode.DotOnly, normalized.IdleContent);
        Assert.Equal("", normalized.CustomIdleText);
    }

    [Fact]
    public void Settings_RoundTrip_PreservesBlurToggleAndFont()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winotch-blurfont-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(path);
            settings.Update(s => s with
            {
                Appearance = new AppearanceSettings
                {
                    Glass = true,
                    BlurEnabled = false,
                    Font = FontChoice.Bahnschrift,
                },
            });

            var reloaded = new SettingsService(path);
            Assert.True(reloaded.Current.Appearance.Glass);
            Assert.False(reloaded.Current.Appearance.BlurEnabled);
            Assert.Equal(FontChoice.Bahnschrift, reloaded.Current.Appearance.Font);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FontChoice_Normalize_DefaultsUnknownToAptos()
    {
        var defaults = new AppearanceSettings().Normalize();
        Assert.Equal(FontChoice.Aptos, defaults.Font);
        Assert.True(defaults.BlurEnabled);

        var normalized = new AppearanceSettings { Font = "comic-sans" }.Normalize();
        Assert.Equal(FontChoice.Aptos, normalized.Font);

        Assert.Equal("Segoe UI Variable Text", FontChoice.FamilyOf(FontChoice.SegoeUiVariable));
        Assert.False(FontChoice.IsKnown(null));
        Assert.False(FontChoice.IsKnown("comic-sans"));
        Assert.True(FontChoice.IsKnown("aptos"));
    }
}