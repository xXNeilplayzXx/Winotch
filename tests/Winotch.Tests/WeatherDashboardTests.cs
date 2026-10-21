using System.Globalization;
using Winotch;

namespace Winotch.Tests;

public class WeatherDashboardTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
    private static WeatherSnapshot Current => new("☀️", "Clear", 20, 20, 24, 15, "London");

    [Fact]
    public void FindsRainWithin30MinutesAndFormatsTheCountdown()
    {
        var times = new[] { Now.ToUnixTimeSeconds() - 900, Now.ToUnixTimeSeconds() + 900 };
        var snapshot = WeatherDashboardStatus.Create(Current, times, [0, 70], [0, 0.2], Now);

        Assert.Equal("Rain starting in 15 min", snapshot.RainStatus);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(times[1]), snapshot.RainStartsAtUtc);
    }

    [Fact]
    public void ReportsRainAlreadyInProgressWithoutPredictingAnotherStart()
    {
        var times = new[] { Now.ToUnixTimeSeconds() - 300, Now.ToUnixTimeSeconds() + 900 };
        var snapshot = WeatherDashboardStatus.Create(Current, times, [90, 80], [0.3, 0.4], Now);

        Assert.Equal("Rain now", snapshot.RainStatus);
        Assert.Null(snapshot.RainStartsAtUtc);
        Assert.Null(WeatherDashboardStatus.FindRainStart(times, [90, 80], [0.3, 0.4], Now.ToUnixTimeSeconds()));
    }

    [Fact]
    public void DistinguishesUnavailableForecastFromDryWeather()
    {
        var unavailable = WeatherDashboardStatus.Create(Current, null, null, null, Now);
        var dry = WeatherDashboardStatus.Create(Current,
            [Now.ToUnixTimeSeconds() + 900], [0], [0], Now);

        Assert.Equal("Rain forecast unavailable", unavailable.RainStatus);
        Assert.Equal("No rain expected soon", dry.RainStatus);
    }

    [Theory]
    [InlineData(0, false, "0°C")]
    [InlineData(20, false, "20°C")]
    [InlineData(20, true, "68°F")]
    [InlineData(-40, true, "-40°F")]
    public void FormatsCelsiusAndFahrenheit(double celsius, bool fahrenheit, string expected)
    {
        Assert.Equal(expected, TemperatureFormatter.Format(celsius, fahrenheit));
    }

    [Theory]
    [InlineData("en-US", true)]
    [InlineData("en-BS", true)]
    [InlineData("en-GB", false)]
    [InlineData("fr-FR", false)]
    public void AutoUnitUsesRegionalConvention(string culture, bool expected)
    {
        Assert.Equal(expected, TemperatureFormatter.IsFahrenheitRegion(CultureInfo.GetCultureInfo(culture)));
    }

    [Theory]
    [InlineData(WeatherTemperatureUnit.Auto, false)]
    [InlineData(WeatherTemperatureUnit.Celsius, false)]
    [InlineData(WeatherTemperatureUnit.Fahrenheit, true)]
    public void UnitPreferenceHonorsOverrides(WeatherTemperatureUnit preference, bool expected)
    {
        if (preference == WeatherTemperatureUnit.Auto)
            Assert.Equal(TemperatureFormatter.IsFahrenheitRegion(CultureInfo.CurrentCulture),
                TemperatureFormatter.UsesFahrenheit(preference));
        else
            Assert.Equal(expected, TemperatureFormatter.UsesFahrenheit(preference));
    }
}
