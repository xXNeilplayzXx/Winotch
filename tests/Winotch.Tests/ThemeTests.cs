using Winotch;
using Winotch.Engine;

namespace Winotch.Tests;

public class ThemeTests
{
    private static FakeWindowsThemeSource Os(bool? light = null, string? accentHex = null) =>
        new()
        {
            Light = light,
            Accent = accentHex is null
                ? null
                : System.Windows.Media.ColorConverter.ConvertFromString(accentHex) is System.Windows.Media.Color c
                    ? c
                    : null,
        };

    private static ThemeService Service(FakeWindowsThemeSource os) => new(os);

    [Theory]
    [InlineData(ThemeMode.Dark, false)]
    [InlineData(ThemeMode.Light, true)]
    [InlineData(ThemeMode.Color, false)]
    public void ResolveLight_FixedModes(ThemeMode mode, bool expectedLight)
    {
        var service = Service(Os(light: true)); // System would be light; fixed modes ignore it
        var appearance = new AppearanceSettings { Theme = mode };
        Assert.Equal(expectedLight, service.ResolveLight(appearance));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)] // unreadable registry falls back to dark
    public void ResolveLight_SystemFollowsWindows(bool? windowsLight, bool expectedLight)
    {
        var service = Service(Os(light: windowsLight));
        var appearance = new AppearanceSettings { Theme = ThemeMode.System };
        Assert.Equal(expectedLight, service.ResolveLight(appearance));
    }

    [Fact]
    public void Refresh_SystemLight_UsesLightPalette()
    {
        var service = Service(Os(light: true));
        var theme = service.Refresh(new WinotchSettings
        {
            Appearance = new AppearanceSettings { Theme = ThemeMode.System },
        });
        Assert.True(theme.IsLight);
        Assert.True(theme.PanelTop.R > 200); // light panels
        Assert.True(theme.Text.R < 100); // dark text on light panels
    }

    [Fact]
    public void Refresh_SystemDark_UsesDarkPalette()
    {
        var service = Service(Os(light: false));
        var theme = service.Refresh(WinotchSettings.Defaults);
        Assert.False(theme.IsLight);
        Assert.True(theme.Text.R > 200);
        Assert.True(theme.PanelTop.R < 60);
    }

    [Fact]
    public void Refresh_FollowOsAccent_ReadsWindowsAccent()
    {
        var service = Service(Os(accentHex: "#FF8800"));
        var theme = service.Refresh(new WinotchSettings
        {
            Appearance = new AppearanceSettings { Accent = AccentMode.FollowOs },
        });
        Assert.Equal(new Col(0xFF, 0x88, 0x00), theme.Accent);
    }

    [Fact]
    public void Refresh_FollowOsAccent_UnreadableFallsBackToDefault()
    {
        var service = Service(Os()); // no accent readable
        var theme = service.Refresh(new WinotchSettings
        {
            Appearance = new AppearanceSettings { Accent = AccentMode.FollowOs },
        });
        Assert.Equal(Col.Rgba(0x50, 0xD6, 0xFF), theme.Accent);
    }

    [Fact]
    public void Refresh_CustomAccent_AppliesHex()
    {
        var service = Service(Os());
        var theme = service.Refresh(new WinotchSettings
        {
            Appearance = new AppearanceSettings { Accent = AccentMode.Custom, AccentHex = "#32D74B" },
        });
        Assert.Equal(new Col(0x32, 0xD7, 0x4B), theme.Accent);
    }

    [Fact]
    public void Refresh_DefaultAccent_UsesPerThemeDefault()
    {
        var service = Service(Os());
        var dark = service.Refresh(new WinotchSettings());
        Assert.Equal(new Col(0x50, 0xD6, 0xFF), dark.Accent);

        var light = service.Refresh(new WinotchSettings
        {
            Appearance = new AppearanceSettings { Theme = ThemeMode.Light },
        });
        Assert.Equal(new Col(0x00, 0x78, 0xD4), light.Accent);
    }

    [Theory]
    [InlineData("#ff8800", "#FF8800")]
    [InlineData("FF8800", "#FF8800")]
    [InlineData("garbage", "#50D6FF")] // invalid -> default
    [InlineData("#11223344", "#112233")] // 8-digit hex keeps RGB, drops alpha
    [InlineData(null, "#50D6FF")]
    public void ParseHex_Normalizes(string? input, string expected)
    {
        var parsed = ThemeService.ParseHex(input);
        Assert.Equal(expected, $"#{parsed.R:X2}{parsed.G:X2}{parsed.B:X2}");
    }

    [Fact]
    public void AppearanceNormalize_ClampsRanges()
    {
        var normalized = new AppearanceSettings
        {
            Opacity = 500,
            Blur = -3,
            Width = 20,
            Height = 999,
            Gradient = "unknown",
            AccentHex = "nope",
        }.Normalize();

        Assert.Equal(100, normalized.Opacity);
        Assert.Equal(0, normalized.Blur);
        Assert.Equal(150, normalized.Width);
        Assert.Equal(200, normalized.Height);
        Assert.Equal(GradientSkin.None, normalized.Gradient);
        Assert.Equal("#50D6FF", normalized.AccentHex);
    }

    [Fact]
    public void SizePresets_Closest_SelectsNearest()
    {
        // Standard is 220x45; neighbouring sizes map to the closest preset.
        Assert.Equal("compact", SizePresets.Closest(250, 40).Id);
        Assert.Equal("standard", SizePresets.Closest(220, 45).Id);
        Assert.Equal("wide", SizePresets.Closest(430, 48).Id);
        Assert.Equal("compact", SizePresets.Closest(330, 44).Id);
    }

    [Fact]
    public void GradientSkin_Unknown_IsNotKnown()
    {
        Assert.False(GradientSkin.IsKnown("neon"));
        Assert.True(GradientSkin.IsKnown(GradientSkin.Ocean));
        Assert.Empty(GradientSkin.Stops(GradientSkin.None));
        Assert.NotEmpty(GradientSkin.Stops(GradientSkin.Sunset));
    }

    [Fact]
    public void Settings_RoundTrip_PreservesAppearance()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winotch-test-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(path);
            settings.Update(s => s with
            {
                Appearance = new AppearanceSettings
                {
                    Theme = ThemeMode.System,
                    Accent = AccentMode.Custom,
                    AccentHex = "#FF8800",
                    Gradient = GradientSkin.Aurora,
                    Glass = true,
                    Opacity = 42,
                    Blur = 21,
                    CustomSize = true,
                    Width = 500,
                    Height = 60,
                },
            });

            var reloaded = new SettingsService(path);
            var appearance = reloaded.Current.Appearance;
            Assert.Equal(ThemeMode.System, appearance.Theme);
            Assert.Equal(AccentMode.Custom, appearance.Accent);
            Assert.Equal("#FF8800", appearance.AccentHex);
            Assert.Equal(GradientSkin.Aurora, appearance.Gradient);
            Assert.True(appearance.Glass);
            Assert.Equal(42, appearance.Opacity);
            Assert.Equal(21, appearance.Blur);
            Assert.True(appearance.CustomSize);
            Assert.Equal(500, appearance.Width);
            Assert.Equal(60, appearance.Height);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}