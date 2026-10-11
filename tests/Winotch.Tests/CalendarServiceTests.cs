using Winotch;

namespace Winotch.Tests;

public sealed class CalendarServiceTests
{
    [Fact]
    public void Parse_UnfoldsEscapesAndSortsEvents()
    {
        var text = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:20300102T100000Z\r\nDTEND:20300102T110000Z\r\nSUMMARY:Plan\\,\r\n ning\\nReview\r\nLOCATION:Room\\; A\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nDTSTART:20300101T100000Z\r\nSUMMARY:Earlier\r\nEND:VEVENT\r\nEND:VCALENDAR";
        var events = CalendarService.Parse(text);
        Assert.Equal(new[] { "Earlier", "Plan,ning\nReview" }, events.Select(item => item.Title));
        Assert.Equal("Room; A", events[1].Location);
        Assert.Equal(TimeSpan.FromHours(1), events[0].End - events[0].Start);
    }

    [Fact]
    public void Parse_HandlesTzidAndDailyRecurrenceWithinBoundedLookahead()
    {
        var text = "BEGIN:VCALENDAR\nBEGIN:VEVENT\nDTSTART;TZID=UTC:20300101T100000\nDTEND;TZID=UTC:20300101T110000\nSUMMARY:Daily\nRRULE:FREQ=DAILY;COUNT=99\nEND:VEVENT\nEND:VCALENDAR";
        var events = CalendarService.Parse(text, new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(32, events.Count);
        Assert.Equal(TimeSpan.FromDays(1), events[1].Start - events[0].Start);
        Assert.Equal(TimeSpan.Zero, events[0].Start.Offset);
    }

    [Fact]
    public void Next_IncludesInProgressButSkipsFinishedMeetings()
    {
        var now = new DateTimeOffset(2030, 1, 1, 10, 30, 0, TimeSpan.Zero);
        var old = new CalendarEvent("Done", now.AddHours(-2), now.AddMinutes(-1));
        var active = new CalendarEvent("Active", now.AddMinutes(-10), now.AddMinutes(20));
        var upcoming = new CalendarEvent("Later", now.AddHours(1), now.AddHours(2));
        Assert.Equal(active, CalendarService.Next(new[] { upcoming, old, active }, now));
    }

    [Fact]
    public void From_PrefersPinnedMeetingOnlyUntilItEnds()
    {
        var now = DateTimeOffset.UtcNow;
        var pinned = new CalendarEvent("Pinned", now.AddMinutes(1), now.AddMinutes(15));
        var normal = new CalendarEvent("Normal", now.AddMinutes(2), now.AddMinutes(20));
        var config = new CalendarWidgetConfig { PinnedMeeting = pinned, Events = new() { normal } };
        Assert.Equal(pinned, CalendarService.From(config, now));
        Assert.Equal(normal, CalendarService.From(config, pinned.End.AddTicks(1)));
    }
}

public sealed class NotificationInboxTests
{
    [Fact]
    public void Add_DeduplicatesByIdAndKeepsNewestHundred()
    {
        var now = DateTimeOffset.UtcNow;
        var entries = Enumerable.Range(0, 120)
            .Select(i => new InboxNotification($"id-{i}", now.AddMinutes(i), $"item {i}", "body"));
        var changed = NotificationInboxStore.Add(entries, new InboxNotification("id-119", now.AddHours(1), "new version", "body"));
        Assert.Equal(NotificationInboxStore.MaximumEntries, changed.Count);
        Assert.Equal("new version", changed[0].Title);
        Assert.Single(changed, item => item.Id == "id-119");
    }

    [Fact]
    public void NormalizeDropsInvalidAndBoundsPersistedEntries()
    {
        var entries = Enumerable.Range(0, 140)
            .Select(i => new InboxNotification($"id-{i}", DateTimeOffset.UtcNow.AddMinutes(i), $"item {i}", ""))
            .Append(new InboxNotification("", DateTimeOffset.UtcNow, "bad", ""));
        var normalized = NotificationInboxStore.Normalize(entries);
        Assert.Equal(NotificationInboxStore.MaximumEntries, normalized.Count);
        Assert.DoesNotContain(normalized, item => item.Id.Length == 0);
    }

    [Fact]
    public void MarkReadAndClearAreImmutableOperations()
    {
        var items = new[] { new InboxNotification("a", DateTimeOffset.UtcNow, "A", "") };
        Assert.False(items[0].Read);
        Assert.True(NotificationInboxStore.MarkRead(items, "a")[0].Read);
        Assert.Empty(NotificationInboxStore.Clear(items));
        Assert.False(items[0].Read);
    }

    [Fact]
    public void MergeSystemNotificationsPreservesReadStateAndFiltersDismissedItems()
    {
        var now = DateTimeOffset.UtcNow;
        var app = new InboxNotification("system", now, "App event", "body");
        var oldSystem = new InboxNotification("windows:app:1:1", now, "Old", "body", Read: true);
        var carriedSystem = new InboxNotification("windows:app:2:2", now.AddMinutes(-1), "Read", "body", Read: true);
        var current = new[] { app, oldSystem, carriedSystem };
        var fresh = new[]
        {
            new InboxNotification(oldSystem.Id, now.AddSeconds(1), "Updated", "new body"),
            new InboxNotification(carriedSystem.Id, now, "Still read", "new body"),
            new InboxNotification("windows:app:3:3", now, "Dismissed", "body"),
        };
        var merged = NotificationInboxStore.MergeSystemNotifications(current, fresh, new[] { "windows:app:3:3" });
        Assert.Contains(app, merged);
        Assert.True(Assert.Single(merged, item => item.Id == oldSystem.Id).Read);
        Assert.True(Assert.Single(merged, item => item.Id == carriedSystem.Id).Read);
        Assert.DoesNotContain(merged, item => item.Id == "windows:app:3:3");
    }

    [Fact]
    public void WindowsNotificationIdIncludesApplicationAndStablePlatformIdentity()
    {
        var created = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal($"windows:app!id:42:{created.UtcTicks}", NotificationInboxStore.WindowsNotificationId("app!id", 42, created));
    }
}

public sealed class MonitorProfileTests
{
    [Fact]
    public void ResolveAndSave_IsolatesDisplayProfileAndPreservesGlobalDisplayTarget()
    {
        var settings = WinotchSettings.Defaults with
        {
            Appearance = WinotchSettings.Defaults.Appearance with { UseMonitorProfiles = true, Display = DisplayMode.All },
        };
        var profile = settings with
        {
            General = settings.General with { Use24HourClock = true },
            Appearance = settings.Appearance with { Theme = ThemeMode.Light },
            Widgets = settings.Widgets with { Notes = "Screen one" },
        };
        var saved = MonitorProfileStore.Save(settings, "DISPLAY1", profile);
        var resolved = MonitorProfileStore.Resolve(saved, "display1");
        Assert.Equal(ThemeMode.Light, resolved.Appearance.Theme);
        Assert.Equal("Screen one", resolved.Widgets.Notes);
        Assert.Equal(DisplayMode.All, resolved.Appearance.Display);
        Assert.False(MonitorProfileStore.Resolve(saved, "DISPLAY2").General.Use24HourClock);
    }

    [Fact]
    public void ResolveWithoutProfilesReturnsGlobalSettings()
    {
        var settings = WinotchSettings.Defaults with
        {
            MonitorProfiles = new Dictionary<string, MonitorProfile>
            {
                ["DISPLAY1"] = MonitorProfile.Capture(WinotchSettings.Defaults with
                {
                    Appearance = WinotchSettings.Defaults.Appearance with { Theme = ThemeMode.Light },
                }),
            },
        };
        Assert.Equal(ThemeMode.Dark, MonitorProfileStore.Resolve(settings, "DISPLAY1").Appearance.Theme);
    }
}
