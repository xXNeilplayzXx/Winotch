namespace Winotch;

public sealed record BatteryInfo(int Percent, bool IsCharging);
public sealed record WifiStatus(string? Name);

/// <summary>Priority status alerts — battery, Wi-Fi and mic/camera use. The
/// tracker is edge-triggered: only CHANGES produce alerts, and the very first
/// snapshot stays quiet (except camera/mic/low-battery) so boot never spams.</summary>
public sealed class PriorityStatusTracker
{
    public const int LowBatteryPercent = 20;
    private readonly Queue<PriorityStatusAlert> _pending = new();
    private PriorityStatusSnapshot? _last;

    public PriorityStatusAlert? Next(PriorityStatusSnapshot current)
    {
        if (_last != current)
        {
            EnqueueChanges(current, _last);
            _last = current;
        }
        return DequeueNext();
    }

    private PriorityStatusAlert? DequeueNext() =>
        _pending.Count > 0 ? _pending.Dequeue() : null;

    private void EnqueueChanges(PriorityStatusSnapshot current, PriorityStatusSnapshot? last)
    {
        if (last is null)
        {
            if (current.CameraActive) _pending.Enqueue(PriorityStatusAlert.CameraActive());
            if (current.MicrophoneActive) _pending.Enqueue(PriorityStatusAlert.MicrophoneActive());
            if (IsLowBattery(current)) _pending.Enqueue(PriorityStatusAlert.LowBattery(current.Battery.Percent));
            return;
        }

        if (!last.CameraActive && current.CameraActive)
            _pending.Enqueue(PriorityStatusAlert.CameraActive());
        if (!last.MicrophoneActive && current.MicrophoneActive)
            _pending.Enqueue(PriorityStatusAlert.MicrophoneActive());

        if (IsLowBattery(current) && !IsLowBattery(last))
            _pending.Enqueue(PriorityStatusAlert.LowBattery(current.Battery.Percent));

        if (!last.Battery.IsCharging && current.Battery.IsCharging)
            _pending.Enqueue(PriorityStatusAlert.ChargerConnected(current.Battery.Percent));
        else if (last.Battery.IsCharging && !current.Battery.IsCharging)
            _pending.Enqueue(PriorityStatusAlert.ChargerDisconnected(current.Battery.Percent));

        EnqueueWifiChange(current.Wifi.Name, last.Wifi.Name);
    }

    private void EnqueueWifiChange(string? currentName, string? lastName)
    {
        if (!string.IsNullOrWhiteSpace(lastName) && string.IsNullOrWhiteSpace(currentName))
            _pending.Enqueue(PriorityStatusAlert.WifiLost(lastName!));
        else if (!string.IsNullOrWhiteSpace(currentName) &&
                 !string.Equals(currentName, lastName, StringComparison.OrdinalIgnoreCase))
            _pending.Enqueue(PriorityStatusAlert.WifiReconnected(currentName));
    }

    private static bool IsLowBattery(PriorityStatusSnapshot snapshot) =>
        !snapshot.Battery.IsCharging && snapshot.Battery.Percent <= LowBatteryPercent;
}

public sealed record PriorityStatusSnapshot(
    BatteryInfo Battery, WifiStatus Wifi, bool MicrophoneActive, bool CameraActive);

public sealed record PriorityStatusAlert(
    string Icon, string Title, string Body, int? BatteryPercent = null, bool ShowsChargingFlourish = false)
{
    public static PriorityStatusAlert LowBattery(int percent) =>
        new("🔋", "Low battery", $"{percent}% remaining", percent);
    public static PriorityStatusAlert ChargerConnected(int percent) =>
        new("⚡", "Charger connected", $"{percent}% and charging", percent, ShowsChargingFlourish: true);
    public static PriorityStatusAlert ChargerDisconnected(int percent) =>
        new("🔋", "Charger disconnected", $"{percent}% battery", percent);
    public static PriorityStatusAlert WifiLost(string name) =>
        new("📡", "Wi-Fi disconnected", $"{name} connection lost");
    public static PriorityStatusAlert WifiReconnected(string name) =>
        new("📶", "Wi-Fi reconnected", $"Connected to {name}");
    public static PriorityStatusAlert MicrophoneActive() =>
        new("🎤", "Microphone active", "An app is using your microphone");
    public static PriorityStatusAlert CameraActive() =>
        new("📷", "Camera active", "An app is using your camera");
}
