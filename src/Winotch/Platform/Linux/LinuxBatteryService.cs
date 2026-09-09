namespace Winotch.Platform.Linux;

/// <summary>Battery service reading from /sys/class/power_supply/.</summary>
public sealed class LinuxBatteryService : IBatteryService
{
    public BatterySnapshot? GetSnapshot()
    {
        try
        {
            // Find first battery
            var powerSupplyPath = "/sys/class/power_supply";
            if (!Directory.Exists(powerSupplyPath))
                return null;

            string? batteryPath = null;
            foreach (var entry in Directory.EnumerateDirectories(powerSupplyPath))
            {
                var typeFile = Path.Combine(entry, "type");
                if (File.Exists(typeFile))
                {
                    var type = File.ReadAllText(typeFile).Trim();
                    if (type == "Battery")
                    {
                        batteryPath = entry;
                        break;
                    }
                }
            }

            if (batteryPath == null)
                return null;

            // Read capacity
            var capacityFile = Path.Combine(batteryPath, "capacity");
            if (!File.Exists(capacityFile))
                return null;

            var capacityStr = File.ReadAllText(capacityFile).Trim();
            if (!double.TryParse(capacityStr, out var capacity))
                return null;

            // Read status
            var statusFile = Path.Combine(batteryPath, "status");
            var isCharging = false;
            if (File.Exists(statusFile))
            {
                var status = File.ReadAllText(statusFile).Trim();
                isCharging = status == "Charging";
            }

            return new BatterySnapshot(ChargePercent: capacity, IsCharging: isCharging);
        }
        catch
        {
            return null;
        }
    }
}
