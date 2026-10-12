using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Winotch;

public sealed record CalendarEvent(string Title, DateTimeOffset Start, DateTimeOffset End, string? Location = null);

public sealed record CalendarWidgetConfig
{
    public string FilePath { get; init; } = "";
    public bool UseWindowsCalendars { get; init; }
    public List<CalendarEvent> Events { get; init; } = new();
    public CalendarEvent? PinnedMeeting { get; init; }

    public CalendarWidgetConfig Normalize() => this with
    {
        FilePath = FilePath ?? "",
        Events = (Events ?? []).Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Title))
            .OrderBy(item => item.Start).Take(5000).ToList(),
        PinnedMeeting = PinnedMeeting is { } meeting && !string.IsNullOrWhiteSpace(meeting.Title) ? meeting : null,
    };
}

/// <summary>Reads an imported local iCalendar (.ics) file; no cloud account or network access is used.</summary>
public static class CalendarService
{
    public static IReadOnlyList<CalendarEvent> Parse(string icalText, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(icalText);
        var unfolded = Regex.Replace(icalText.Replace("\r\n", "\n").Replace('\r', '\n'), "\n[ \\t]", "");
        var reference = now ?? DateTimeOffset.Now;
        var events = new List<CalendarEvent>();
        Dictionary<string, string>? fields = null;
        foreach (var line in unfolded.Split('\n'))
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                fields = new(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (fields is not null && TryCreate(fields, out var item)) events.Add(item);
                fields = null;
                continue;
            }
            if (line.Equals("END:VTODO", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("END:VJOURNAL", StringComparison.OrdinalIgnoreCase))
            {
                fields = null;
                continue;
            }
            if (fields is null) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var rawKey = line[..colon];
            var key = rawKey.Split(';', 2)[0].ToUpperInvariant();
            var value = line[(colon + 1)..];
            if (key == "DTSTART" && TryParameter(rawKey, "TZID", out var timeZone) &&
                TryDate(value, out var localDate))
            {
                fields["DTSTART"] = InTimeZone(localDate, timeZone);
                continue;
            }
            if (key is "SUMMARY" or "DTSTART" or "DTEND" or "LOCATION")
                fields[key] = Unescape(value);
        }
        return ExpandRecurrences(events, unfolded, reference).OrderBy(item => item.Start).Take(5000).ToArray();
    }

    public static IReadOnlyList<CalendarEvent> ReadFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Array.Empty<CalendarEvent>();
        return Parse(File.ReadAllText(path));
    }

    public static CalendarEvent? Next(IEnumerable<CalendarEvent> events, DateTimeOffset now) =>
        events.Where(item => item.End > now).OrderBy(item => item.Start).FirstOrDefault();

    public static CalendarEvent? From(CalendarWidgetConfig config, DateTimeOffset now) =>
        config.PinnedMeeting is { } pinned && pinned.End > now ? pinned : Next(config.Events, now);

    private static bool TryCreate(IReadOnlyDictionary<string, string> fields, out CalendarEvent item)
    {
        item = null!;
        if (!fields.TryGetValue("DTSTART", out var startText) || !TryDate(startText, out var start)) return false;
        var end = fields.TryGetValue("DTEND", out var endText) && TryDate(endText, out var parsedEnd)
            ? parsedEnd : start.AddHours(1);
        if (end < start) return false;
        fields.TryGetValue("SUMMARY", out var title);
        fields.TryGetValue("LOCATION", out var location);
        item = new(string.IsNullOrWhiteSpace(title) ? "Untitled meeting" : title, start, end, location);
        return true;
    }

    private static bool TryDate(string value, out DateTimeOffset result)
    {
        var formats = new[] { "yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm'Z'", "yyyyMMdd'T'HHmm", "yyyyMMdd" };
        foreach (var format in formats)
        {
            if (!DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) continue;
            var utc = format.EndsWith("'Z'", StringComparison.Ordinal)
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            result = new DateTimeOffset(utc);
            return true;
        }
        result = default;
        return false;
    }

    private static IReadOnlyList<CalendarEvent> ExpandRecurrences(List<CalendarEvent> events, string source, DateTimeOffset now)
    {
        // Recurrence expansion includes today plus the next 31 calendar days;
        // RRULE is expanded here for common DAILY/WEEKLY rules without pulling in a heavyweight dependency.
        var lines = Regex.Replace(source.Replace("\r\n", "\n").Replace('\r', '\n'), "\n[ \\t]", "").Split('\n');
        var result = new List<CalendarEvent>(events);
        Dictionary<string, string>? fields = null;
        string? startZone = null;
        foreach (var line in lines)
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase)) { fields = new(); startZone = null; continue; }
            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase)) { fields = null; startZone = null; continue; }
            if (fields is null) continue;
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                var property = line[..colon];
                var key = property.Split(';', 2)[0].ToUpperInvariant();
                if (key == "DTSTART") TryParameter(property, "TZID", out startZone);
                fields[key] = line[(colon + 1)..];
            }
            if (line.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase) &&
                fields.TryGetValue("DTSTART", out var startText) && TryDateWithTimeZone(startZone, startText, out var start) &&
                TryCreate(fields, out var prototype))
            {
                var rule = ParseRule(line[(colon + 1)..]);
                var interval = Math.Clamp(rule.Interval, 1, 365);
                var horizon = new DateTimeOffset(now.Date, now.Offset).AddDays(32).AddTicks(-1);
                var cursor = start;
                var occurrence = 1;
                var produced = 0;
                while (cursor <= horizon && produced < 64 && occurrence < Math.Clamp(rule.Count, 1, 5000))
                {
                    cursor = rule.Frequency switch
                    {
                        "DAILY" => cursor.AddDays(interval),
                        "WEEKLY" => cursor.AddDays(interval * 7),
                        _ => horizon.AddTicks(1),
                    };
                    occurrence++;
                    if (cursor > horizon || rule.Until is { } until && cursor > until) continue;
                    var duration = prototype.End - prototype.Start;
                    result.Add(prototype with { Start = cursor, End = cursor + duration });
                    produced++;
                }
            }
        }
        return result;
    }

    private static (string Frequency, int Interval, int Count, DateTimeOffset? Until) ParseRule(string text)
    {
        var parts = text.Split(';').Select(part => part.Split('=', 2)).Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0].ToUpperInvariant(), pair => pair[1], StringComparer.OrdinalIgnoreCase);
        var frequency = parts.GetValueOrDefault("FREQ", "").ToUpperInvariant();
        var interval = int.TryParse(parts.GetValueOrDefault("INTERVAL"), out var value) ? value : 1;
        var count = int.TryParse(parts.GetValueOrDefault("COUNT"), out var countValue) ? countValue : 5000;
        DateTimeOffset? until = parts.TryGetValue("UNTIL", out var untilText) && TryDate(untilText, out var parsed)
            ? parsed : null;
        return (frequency, interval, count, until);
    }

    private static bool TryParameter(string property, string name, out string value)
    {
        value = "";
        foreach (var part in property.Split(';').Skip(1))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0].Equals(name, StringComparison.OrdinalIgnoreCase))
            { value = pair[1]; return true; }
        }
        return false;
    }

    private static bool TryDateWithTimeZone(string? zoneId, string value, out DateTimeOffset result)
    {
        if (!string.IsNullOrWhiteSpace(zoneId) && TryDate(value, out var local))
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
                result = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.DateTime, DateTimeKind.Unspecified), zone);
                return true;
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TryDate(value, out result);
    }

    private static string InTimeZone(DateTimeOffset localDate, string zoneId)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDate.DateTime, DateTimeKind.Unspecified), zone)
                .ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        }
        catch (TimeZoneNotFoundException) { return localDate.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture); }
        catch (InvalidTimeZoneException) { return localDate.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture); }
    }

    private static string Unescape(string value)
    {
        var decoded = Regex.Replace(value, "\\\\u([0-9A-Fa-f]{4})", match =>
            ((char)int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
        return decoded.Replace("\\n", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("\\,", ",", StringComparison.Ordinal).Replace("\\;", ";", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }
}
