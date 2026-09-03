using System.Net.Http;
using System.Text.Json;

namespace Winotch;

/// <summary>A WMO weather code mapped to the emoji + label the widget shows
/// (mirrors the legacy webview app's mapWeather). Pure and unit-testable.</summary>
public sealed record WeatherCondition(string Emoji, string Label);

public static class WeatherCodes
{
    public static WeatherCondition Map(int code) => code switch
    {
        0 => new("☀️", "Clear"),
        1 => new("🌤️", "Mostly clear"),
        2 => new("⛅", "Partly cloudy"),
        3 => new("☁️", "Overcast"),
        45 or 48 => new("🌫️", "Fog"),
        >= 51 and <= 57 => new("🌦️", "Drizzle"),
        >= 61 and <= 67 => new("🌧️", "Rain"),
        >= 71 and <= 77 => new("❄️", "Snow"),
        >= 80 and <= 82 => new("🌦️", "Showers"),
        85 or 86 => new("🌨️", "Snow showers"),
        >= 95 => new("⛈️", "Thunderstorm"),
        _ => new("🌡️", "Unknown"),
    };
}

/// <summary>Thin open-meteo client: city search (geocoding) + current/daily
/// forecast. No API key, no telemetry — same free endpoint the old app used.</summary>
public sealed class WeatherService : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public void Dispose() => _http.Dispose();

    /// <summary>Resolve a city name to display name + coordinates.</summary>
    public async Task<(string Name, double Lat, double Lon)?> SearchAsync(string query)
    {
        var city = query.Trim();
        if (city.Length == 0) return null;
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://geocoding-api.open-meteo.com/v1/search?name=" +
            Uri.EscapeDataString(city) + "&count=1&language=en&format=json");
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var results = doc.RootElement.TryGetProperty("results", out var res) ? res : default;
        if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0) return null;
        var hit = results[0];
        if (!hit.TryGetProperty("latitude", out var lat) || !hit.TryGetProperty("longitude", out var lon))
        {
            return null;
        }
        var name = hit.TryGetProperty("name", out var n) ? n.GetString() : city;
        var region = hit.TryGetProperty("admin1", out var a1) ? a1.GetString() : null;
        if (!string.IsNullOrWhiteSpace(region)) name += ", " + region;
        return (name ?? city, lat.GetDouble(), lon.GetDouble());
    }

    /// <summary>Fetch current + today's min/max for a location.</summary>
    public async Task<WeatherSnapshot?> GetForecastAsync(string city, double lat, double lon)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.open-meteo.com/v1/forecast?latitude=" + lat.ToStringInvariant() +
            "&longitude=" + lon.ToStringInvariant() +
            "&current=temperature_2m,weather_code,apparent_temperature" +
            "&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1");
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var current = root.TryGetProperty("current", out var cur) ? cur : default;
        var daily = root.TryGetProperty("daily", out var d) ? d : default;

        double Num(JsonElement el, string prop, double fallback) =>
            el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetDouble()
                : fallback;

        var code = (int)Math.Round(Num(current, "weather_code", 0));
        var condition = WeatherCodes.Map(code);
        return new WeatherSnapshot(
            condition.Emoji, condition.Label,
            Num(current, "temperature_2m", 0),
            Num(current, "apparent_temperature", Num(current, "temperature_2m", 0)),
            Num(daily, "temperature_2m_max", 0),
            Num(daily, "temperature_2m_min", 0),
            city);
    }
}

internal static class DoubleExtensions
{
    public static string ToStringInvariant(this double value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}