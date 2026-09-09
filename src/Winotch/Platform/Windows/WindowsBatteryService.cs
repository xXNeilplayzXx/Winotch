namespace Winotch.Platform.Windows;

/// <summary>Windows battery service using WinForms PowerStatus.</summary>
public sealed class WindowsBatteryService : IBatteryService
{
    public BatterySnapshot GetSnapshot()
    {
        var status = System.Windows.Forms.SystemInformation.PowerStatus;
        var percent = (int)(status.BatteryLifePercent * 100);
        var charging = status.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.Charging;
        return new BatterySnapshot(percent, charging);
    }
}
