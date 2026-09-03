using System.Windows.Forms;

namespace Winotch;

/// <summary>Battery readings from the WinForms power status API.</summary>
public static class SystemStatus
{
    public static BatteryInfo GetBattery()
    {
        var power = SystemInformation.PowerStatus;
        var percent = Math.Clamp((int)Math.Round(power.BatteryLifePercent * 100), 0, 100);
        var charging = power.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Charging);
        return new BatteryInfo(percent, charging);
    }
}
