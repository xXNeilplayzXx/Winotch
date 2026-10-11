using System.Runtime.InteropServices;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Winotch;

/// <summary>Optional reader for the Windows notification center. Requires package identity,
/// the userNotificationListener capability, and explicit user approval.</summary>
public sealed class WindowsNotificationInbox
{
    private UserNotificationListener Listener => UserNotificationListener.Current;

    public async Task<UserNotificationListenerAccessStatus> RequestAccessAsync() =>
        await Listener.RequestAccessAsync();

    public async Task<IReadOnlyList<InboxNotification>> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var listener = Listener;
        if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed)
            return Array.Empty<InboxNotification>();

        var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
        var result = new List<InboxNotification>(Math.Min(notifications.Count, NotificationInboxStore.MaximumEntries));
        foreach (var notification in notifications.OrderByDescending(item => item.CreationTime)
                     .Take(NotificationInboxStore.MaximumEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var appName = notification.AppInfo?.DisplayInfo?.DisplayName;
                var appId = notification.AppInfo?.AppUserModelId ?? "unknown";
                if (string.Equals(appId, "unknown", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(appName, "Winotch", StringComparison.OrdinalIgnoreCase)) continue;
                var binding = notification.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                var elements = binding?.GetTextElements();
                if (elements is null || elements.Count == 0) continue;
                var title = elements[0].Text;
                if (string.IsNullOrWhiteSpace(title)) continue;
                var body = string.Join("\n", elements.Skip(1).Select(element => element.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
                if (!string.IsNullOrWhiteSpace(appName)) body = string.IsNullOrEmpty(body) ? appName : $"{appName}: {body}";
                var id = NotificationInboxStore.WindowsNotificationId(appId, notification.Id, notification.CreationTime);
                result.Add(new InboxNotification(id, notification.CreationTime, title, body, "\uE7BA"));
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or NullReferenceException)
            {
                // Skip an individual notification whose platform payload is unavailable.
            }
        }
        return result;
    }
}
