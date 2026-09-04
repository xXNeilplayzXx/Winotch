using System.Net;
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

/// <summary>Thin weather client: city search (geocoding) + current/daily
/// forecast. The primary provider is open-meteo (no key, no telemetry);
/// city and reverse lookups fall back to Nominatim (OpenStreetMap) when
/// open-meteo's geocoding host is unreachable, blocked, or draws a blank.</summary>
public sealed class WeatherService : IDisposable
{
    private const string OpenMeteoSearch = "https://geocoding-api.open-meteo.com/v1/search";
    private const string OpenMeteoForecast = "https://api.open-meteo.com/v1/forecast";
    private const string NominatimSearch = "https://nominatim.openstreetmap.org/search";
    private const string NominatimReverse = "https://nominatim.openstreetmap.org/reverse";

    private readonly HttpClient _http = CreateClient();

    private static HttpClient CreateClient()
    {
        // open-meteo asks callers to identify themselves with a User-Agent,
        // and some networks proxy/compress API responses — send one and
        // decode gzip/deflate so middleboxes can't corrupt the JSON.
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Winotch/" + UpdateService.CurrentVersionText);
        return client;
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Resolve a city name to display name + coordinates. Tries
    /// open-meteo first, then falls back to Nominatim on any failure or a
    /// blank result, so a blocked or unreachable geocoding host can't break
    /// city lookup.</summary>
    public async Task<(string Name, double Lat, double Lon)?> SearchAsync(string query)
    {
        var city = query.Trim();
        if (city.Length == 0) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                OpenMeteoSearch + "?name=" + Uri.EscapeDataString(city) +
                "&count=1&language=en&format=json");
            var hit = await ParseGeocodingAsync(request, city);
            if (hit is not null) return hit;
        }
        catch
        {
            // open-meteo unreachable or blocked — fall through to Nominatim.
        }
        return await SearchNominatimAsync(city);
    }

    /// <summary>Reverse-geocode coordinates to a nearby city (used by the
    /// "Use Windows location" weather flow), with the same fallback.</summary>
    public async Task<(string Name, double Lat, double Lon)?> SearchByCoordinatesAsync(double lat, double lon)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                OpenMeteoSearch + "?reverse=true&latitude=" + lat.ToStringInvariant() +
                "&longitude=" + lon.ToStringInvariant() +
                "&count=1&language=en&format=json");
            var hit = await ParseGeocodingAsync(request, null);
            if (hit is not null) return hit;
        }
        catch
        {
            // open-meteo unreachable or blocked — fall through to Nominatim.
        }
        using var reverse = new HttpRequestMessage(HttpMethod.Get,
            NominatimReverse + "?lat=" + lat.ToStringInvariant() +
            "&lon=" + lon.ToStringInvariant() + "&format=json&addressdetails=1");
        return await ParseNominatimAsync(reverse);
    }

    /// <summary>City search against Nominatim/OSM (forward geocoding).</summary>
    private async Task<(string Name, double Lat, double Lon)?> SearchNominatimAsync(string city)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            NominatimSearch + "?q=" + Uri.EscapeDataString(city) +
            "&format=json&addressdetails=1&limit=1");
        return await ParseNominatimAsync(request);
    }

    /// <summary>Parse a Nominatim response: forward searches return a results
    /// array, reverse lookups a single object. lat/lon arrive as strings and
    /// the display name is shortened from the address block.</summary>
    private async Task<(string Name, double Lat, double Lon)?> ParseNominatimAsync(HttpRequestMessage request)
    {
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        JsonElement hit = default;
        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() == 0) return null;
            hit = root[0];
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            hit = root;
        }
        else
        {
            return null;
        }
        if (!TryLatLon(hit, out var lat, out var lon)) return null;
        var name = NominatimName(hit);
        return name is null ? null : (name, lat, lon);
    }

    private static bool TryLatLon(JsonElement hit, out double lat, out double lon)
    {
        lat = 0;
        lon = 0;
        return TryDouble(hit, "lat", out lat) && TryDouble(hit, "lon", out lon);
    }

    private static bool TryDouble(JsonElement el, string prop, out double value)
    {
        value = 0;
        if (!el.TryGetProperty(prop, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Number)
        {
            value = v.GetDouble();
            return true;
        }
        if (v.ValueKind == JsonValueKind.String)
        {
            return double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }
        return false;
    }

    /// <summary>Short "city, region" from Nominatim's address block, falling
    /// back to the first parts of display_name when no settlement matched.</summary>
    private static string? NominatimName(JsonElement hit)
    {
        if (hit.TryGetProperty("address", out var address) && address.ValueKind == JsonValueKind.Object)
        {
            string? Part(string[] keys)
            {
                foreach (var key in keys)
                {
                    if (address.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    {
                        var s = v.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                }
                return null;
            }
            var city = Part(new[] { "city", "town", "village", "municipality", "hamlet", "suburb", "quarter", "county" })
                ?? Part(new[] { "state" })
                ?? Part(new[] { "country" });
            if (city is not null)
            {
                var region = Part(new[] { "state", "country" });
                return region is not null && !string.Equals(city, region, StringComparison.OrdinalIgnoreCase)
                    ? city + ", " + region
                    : city;
            }
        }
        if (hit.TryGetProperty("display_name", out var dn) && dn.ValueKind == JsonValueKind.String &&
            dn.GetString() is { Length: > 0 } display)
        {
            var parts = display.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).Take(2).ToArray();
            if (parts.Length > 0) return string.Join(", ", parts);
        }
        return null;
    }

    private async Task<(string Name, double Lat, double Lon)?> ParseGeocodingAsync(
        HttpRequestMessage request, string? fallbackName)
    {
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
        var name = hit.TryGetProperty("name", out var n) ? n.GetString() : null;
        var region = hit.TryGetProperty("admin1", out var a1) ? a1.GetString() : null;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = fallbackName is { Length: > 0 } ? fallbackName : $"({lat.GetDouble():0.##}, {lon.GetDouble():0.##})";
        }
        if (!string.IsNullOrWhiteSpace(region)) name += ", " + region;
        return (name, lat.GetDouble(), lon.GetDouble());
    }

    /// <summary>Precipitation buckets for the rain alert: 15-minute steps
    /// covering the recent past (to tell "starting" from "already raining")
    /// and the next half hour. Times are unix seconds UTC, probability is %,
    /// precipitation is mm per bucket. Null when the request fails.</summary>
    public async Task<PrecipitationProbe?> GetPrecipitationProbeAsync(double lat, double lon)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            OpenMeteoForecast + "?latitude=" + lat.ToStringInvariant() +
            "&longitude=" + lon.ToStringInvariant() +
            "&minutely_15=precipitation_probability,precipitation" +
            "&timeformat=unixtime&past_minutely_15=1&forecast_minutely_15=3&forecast_days=1");
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var block = doc.RootElement.TryGetProperty("minutely_15", out var m) ? m : default;
        if (block.ValueKind != JsonValueKind.Object ||
            !block.TryGetProperty("time", out var timeEl) ||
            timeEl.ValueKind != JsonValueKind.Array ||
            !block.TryGetProperty("precipitation_probability", out var probEl) ||
            probEl.ValueKind != JsonValueKind.Array ||
            !block.TryGetProperty("precipitation", out var mmEl) ||
            mmEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var n = Math.Min(timeEl.GetArrayLength(),
            Math.Min(probEl.GetArrayLength(), mmEl.GetArrayLength()));
        if (n == 0) return null;
        var times = new long[n];
        var probability = new double[n];
        var mm = new double[n];
        for (var i = 0; i < n; i++)
        {
            times[i] = timeEl[i].GetInt64();
            probability[i] = probEl[i].ValueKind == JsonValueKind.Number ? probEl[i].GetDouble() : 0;
            mm[i] = mmEl[i].ValueKind == JsonValueKind.Number ? mmEl[i].GetDouble() : 0;
        }
        return new PrecipitationProbe(times, probability, mm);
    }

    /// <summary>Fetch current + today's min/max for a location.</summary>
    public async Task<WeatherSnapshot?> GetForecastAsync(string city, double lat, double lon)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            OpenMeteoForecast + "?latitude=" + lat.ToStringInvariant() +
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

/// <summary>15-minute precipitation buckets around "now" (one past, three
/// ahead) used by the rain-start alert.</summary>
public sealed record PrecipitationProbe(long[] Times, double[] Probability, double[] Mm);

internal static class DoubleExtensions
{
    public static string ToStringInvariant(this double value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}