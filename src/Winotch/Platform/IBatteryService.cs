namespace Winotch.Platform;

/// <summary>Snapshot of battery status.</summary>
public sealed record BatterySnapshot(
    double ChargePercent,
    bool IsCharging);

/// <summary>Abstracts battery status.
/// Windows: SystemInformation.PowerStatus (GetSystemPowerStatus)
/// Linux: /sys/class/power_supply/BAT* or UPower D-Bus</summary>
public interface IBatteryService
{
    BatterySnapshot? GetSnapshot();
}
