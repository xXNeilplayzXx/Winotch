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

    [Theory]
    [InlineData(CallChipContent.AppAndTimer, "Discord · 2:05")]
    [InlineData(CallChipContent.TimerAndApp, "2:05 · Discord")]
    [InlineData(CallChipContent.AppName, "Discord")]
    [InlineData(CallChipContent.Timer, "2:05")]
    public void CallChipLayout_ComposesTheSelectedPriority(CallChipContent mode, string expected)
    {
        Assert.Equal(expected, CallChipLayout.Text(mode, "Discord", "2:05"));
    }

    [Fact]
    public void CallChipLayout_KeepsTimerWhenAppNameIsUnavailable()
    {
        Assert.Equal("2:05", CallChipLayout.Text(CallChipContent.AppName, " ", "2:05"));
        Assert.Equal("2:05", CallChipLayout.Text(CallChipContent.AppAndTimer, null, "2:05"));
    }

    [Fact]
    public void CallChipPriority_NormalizesInvalidValuesAndRoundTrips()
    {
        Assert.Equal(CallChipContent.AppAndTimer,
            new FeatureSettings { CallChipPriority = (CallChipContent)int.MaxValue }.Normalize().CallChipPriority);

        var path = Path.Combine(Path.GetTempPath(), $"winotch-call-chip-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(path);
            settings.Update(s => s with
            {
                Features = s.Features with { CallChipPriority = CallChipContent.TimerAndApp },
            });
            Assert.Equal(CallChipContent.TimerAndApp, new SettingsService(path).Current.Features.CallChipPriority);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void QuietDuringFocus_DefaultsOffAndRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winotch-focus-quiet-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(path);
            Assert.False(settings.Current.Features.QuietDuringFocus);

            settings.Update(s => s with
            {
                Features = s.Features with { QuietDuringFocus = true },
            });

            Assert.True(new SettingsService(path).Current.Features.QuietDuringFocus);
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
                    OpenSettingsSections = new List<string> { "Appearance", "size", "pill", "alerts", "nonsense", "  " },
                },
            });

            var sections = new SettingsService(path).Current.General.OpenSettingsSections;
            Assert.Equal(new[] { "appearance", "pill", "notifications" }, sections);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SettingsSections_MigratesOldOpenSectionIds()
    {
        var settings = new GeneralSettings
        {
            OpenSettingsSections = new List<string> { "general", "size", "display", "widgets", "alerts" },
        }.Normalize();

        Assert.Equal(new[] { "basics", "pill", "displays", "widgets", "notifications" }, settings.OpenSettingsSections);
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
    public void DefaultMiniWidget_NormalizesAndRoundTrips()
    {
        Assert.Equal(IdleMiniWidget.Battery, new AppearanceSettings().ResolveDefaultMiniWidget(true));
        Assert.Equal(IdleMiniWidget.None, new AppearanceSettings().ResolveDefaultMiniWidget(false));
        Assert.Equal(IdleMiniWidget.Battery,
            new AppearanceSettings { DefaultMiniWidget = (IdleMiniWidget)99 }
                .Normalize().ResolveDefaultMiniWidget(true));
        Assert.Equal(IdleMiniWidget.Weather,
            new AppearanceSettings { DefaultMiniWidget = IdleMiniWidget.Weather }
                .ResolveDefaultMiniWidget(true));

        var path = Path.Combine(Path.GetTempPath(), $"winotch-mini-widget-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(path);
            settings.Update(s => s with
            {
                Appearance = s.Appearance with { DefaultMiniWidget = IdleMiniWidget.Weather },
            });
            Assert.Equal(IdleMiniWidget.Weather,
                new SettingsService(path).Current.Appearance.ResolveDefaultMiniWidget(true));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
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