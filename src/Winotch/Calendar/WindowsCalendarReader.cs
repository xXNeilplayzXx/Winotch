using Windows.ApplicationModel.Appointments;

namespace Winotch;

/// <summary>Read-only adapter over the Windows appointment store. Package identity and the
/// appointmentsSystem capability are required by the platform API.</summary>
public static class WindowsCalendarReader
{
    public static async Task<IReadOnlyList<CalendarEvent>> ReadUpcomingAsync(
        DateTimeOffset now, TimeSpan horizon, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly);
        if (store is null) return Array.Empty<CalendarEvent>();
        var calendars = await store.FindAppointmentCalendarsAsync();
        var options = new FindAppointmentsOptions();
        options.FetchProperties.Add(AppointmentProperties.Subject);
        options.FetchProperties.Add(AppointmentProperties.Location);
        options.FetchProperties.Add(AppointmentProperties.StartTime);
        options.FetchProperties.Add(AppointmentProperties.Duration);
        options.FetchProperties.Add(AppointmentProperties.IsCanceledMeeting);

        var events = new List<CalendarEvent>();
        foreach (var calendar in calendars)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var appointments = await calendar.FindAppointmentsAsync(now, horizon, options);
            foreach (var appointment in appointments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (appointment.IsCanceledMeeting || string.IsNullOrWhiteSpace(appointment.Subject)) continue;
                var start = appointment.StartTime;
                var end = start + appointment.Duration;
                if (end <= now) continue;
                events.Add(new CalendarEvent(appointment.Subject, start, end,
                    string.IsNullOrWhiteSpace(appointment.Location) ? null : appointment.Location));
            }
        }
        return events.OrderBy(item => item.Start).Take(5000).ToArray();
    }
}
