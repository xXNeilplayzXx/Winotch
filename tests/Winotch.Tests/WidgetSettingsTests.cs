using Winotch;

namespace Winotch.Tests;

public class WidgetSettingsTests
{
    [Fact]
    public void Defaults_EnableAllWidgetsInOrder()
    {
        var widgets = new WidgetSettings();
        Assert.Equal(WidgetSettings.Known, widgets.Enabled);
    }

    [Fact]
    public void Normalize_DropsUnknownIdsAndDuplicates()
    {
        var widgets = new WidgetSettings
        {
            Enabled = new List<string> { "clock", "bogus", "clock", "weather" },
        };
        Assert.Equal(new[] { "clock", "weather" }, widgets.Normalize().Enabled);
    }

    [Fact]
    public void Normalize_EmptyEnabledFallsBackToDefaults()
    {
        var widgets = new WidgetSettings { Enabled = new List<string>() };
        Assert.Equal(WidgetSettings.Known, widgets.Normalize().Enabled);
    }

    [Fact]
    public void Normalize_FillsMissingSubconfigs()
    {
        var widgets = new WidgetSettings
        {
            Notes = null!,
            Weather = null!,
            LauncherApps = null!,
            SystemMetrics = null!,
        };
        var normalized = widgets.Normalize();
        Assert.Equal("", normalized.Notes);
        Assert.False(normalized.Weather.HasLocation);
        Assert.Empty(normalized.LauncherApps);
        Assert.True(normalized.SystemMetrics.Cpu);
    }

    [Fact]
    public void Settings_Normalize_WiresWidgets()
    {
        var settings = new WinotchSettings { Widgets = null! };
        Assert.NotNull(settings.Normalize().Widgets);
        Assert.Equal(WidgetSettings.Known, settings.Normalize().Widgets.Enabled);
    }

    [Fact]
    public void SizeFor_FallsBackToStandard_WithoutExplicitChoice()
    {
        var widgets = new WidgetSettings();
        Assert.Equal(WidgetScale.Standard, widgets.SizeFor("clock"));
        Assert.Equal(WidgetScale.Standard, widgets.SizeFor("bogus"));
    }

    [Fact]
    public void WithSize_SetsAndClearsPerWidgetSizes()
    {
        var widgets = new WidgetSettings();
        var large = widgets.WithSize("clock", WidgetScale.Large);
        Assert.Equal(WidgetScale.Large, large.SizeFor("clock"));
        Assert.Equal(WidgetScale.Standard, large.SizeFor("weather"));

        var reset = large.WithSize("clock", WidgetScale.Standard);
        Assert.Equal(WidgetScale.Standard, reset.SizeFor("clock"));
    }

    [Fact]
    public void Normalize_MigratesLegacySingleScaleIntoPerWidgetSizes()
    {
        var widgets = new WidgetSettings
        {
            Enabled = new List<string> { "clock", "weather" },
            Scale = WidgetScale.Large,
        };
        var normalized = widgets.Normalize();
        Assert.Equal(WidgetScale.Large, normalized.SizeFor("clock"));
        Assert.Equal(WidgetScale.Large, normalized.SizeFor("weather"));
        // The legacy single-scale value is cleared once migrated.
        Assert.Equal(WidgetScale.Standard, normalized.Scale);
    }

    [Fact]
    public void Normalize_DropsUnknownSizeIds()
    {
        var widgets = new WidgetSettings
        {
            Sizes = new Dictionary<string, WidgetScale>
            {
                ["clock"] = WidgetScale.Large,
                ["bogus"] = WidgetScale.Compact,
            },
        };
        var normalized = widgets.Normalize();
        Assert.Equal(WidgetScale.Large, normalized.SizeFor("clock"));
        Assert.Equal(WidgetScale.Standard, normalized.SizeFor("bogus"));
    }

    [Fact]
    public void Settings_RoundTrip_PreservesPerWidgetSizes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winotch-widget-sizes-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new SettingsService(path);
            settings.Update(s => s with
            {
                Widgets = s.Widgets.WithSize("clock", WidgetScale.Large)
                    .WithSize("weather", WidgetScale.Compact),
            });

            var reloaded = new SettingsService(path);
            Assert.Equal(WidgetScale.Large, reloaded.Current.Widgets.SizeFor("clock"));
            Assert.Equal(WidgetScale.Compact, reloaded.Current.Widgets.SizeFor("weather"));
            Assert.Equal(WidgetScale.Standard, reloaded.Current.Widgets.SizeFor("media"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

public class WeatherCodeMappingTests
{
    [Theory]
    [InlineData(0, "☀️", "Clear")]
    [InlineData(1, "🌤️", "Mostly clear")]
    [InlineData(2, "⛅", "Partly cloudy")]
    [InlineData(3, "☁️", "Overcast")]
    [InlineData(45, "🌫️", "Fog")]
    [InlineData(55, "🌦️", "Drizzle")]
    [InlineData(61, "🌧️", "Rain")]
    [InlineData(71, "❄️", "Snow")]
    [InlineData(80, "🌦️", "Showers")]
    [InlineData(86, "🌨️", "Snow showers")]
    [InlineData(95, "⛈️", "Thunderstorm")]
    [InlineData(999, "⛈️", "Thunderstorm")]
    [InlineData(90, "🌡️", "Unknown")]
    public void MapsWmoCodes(int code, string emoji, string label)
    {
        var condition = WeatherCodes.Map(code);
        Assert.Equal(emoji, condition.Emoji);
        Assert.Equal(label, condition.Label);
    }
}