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